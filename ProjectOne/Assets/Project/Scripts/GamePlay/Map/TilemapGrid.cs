using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ProjectOne.Map
{
	public class TilemapGrid : MonoBehaviour
	{
		// 맵 프리팹에서 인스펙터로 직접 연결 — 자식 이름 매칭 대신 명시적 참조
		[SerializeField] private Grid _grid;
		[SerializeField] private Tilemap _groundMap;
		[SerializeField] private Tilemap _obstacleMap;
		// 발사체 차단 전용 타일맵 — 이 타일이 칠해진 셀은 발사체(직선/유도/포물선)도 막힌다.
		// obstacleMap(물 등)은 유닛만 막고 발사체는 통과. blockMap 은 유닛+발사체 모두 차단.
		[SerializeField] private Tilemap _blockMap;

		private FlowField _flowField = new FlowField();
		private Vector3Int _boundsMin;

		// 마지막 플로우필드 베이크 목표(필드 로컬 셀). 장막 해제로 통행 배열을 다시 만든 뒤 같은 목표로 재베이크한다.
		private int _lastBakeX;
		private int _lastBakeY;
		private bool _hasBaked;

		// 충돌 핫 루프(ResolveWallCollision)용 캐시 — 네이티브 Grid/Tilemap 호출을 산술/배열 조회로 대체.
		private bool[]  _walkable;       // 셀 통행 가능 여부(InitializeFlowField 에서 1회 구축)
		private bool[]  _blocked;        // 셀 발사체 차단 여부(blockMap 타일 유무, 1회 구축)
		private int     _fieldWidth;
		private int     _fieldHeight;
		private Vector2 _cellSize;       // 셀 크기
		private Vector2 _cellOrigin;     // _boundsMin 셀의 중심 월드좌표

		// 오브젝트 장애물(MapBlocker)은 셀에 굽지 않고 사각형 그대로 들고 검사한다 —
		// 셀에 구우면 차단 영역이 셀 크기 단위로 부풀어 스프라이트 주변에 못 다가간다.
		private Vector2[] _blockerMin;
		private Vector2[] _blockerMax;
		private bool[]    _blockerStopsProjectile;
		private int       _blockerCount;

		[SerializeField] bool DEV_ShowDrawGizmos = false;

		// ── 스폰 마커 (설계 7장) ──────────────────────────────────────
		//
		// 이름 문자열 탐색 대신 컴포넌트로 수집한다. 프리팹 인스턴스화 직후 1회만 훑으면 되고,
		// 유니티가 복제 시 이름에 "(1)" 을 붙여도 아무 영향이 없다.
		private MonsterSpawnPoint[] _spawnPoints;
		private DungeonSpawnSlot[] _slots;
		private NpcSpawnPoint[] _npcPoints;
		private MapAnchor _anchor;

		// 타일이 아니라 오브젝트로 놓인 장애물 — InitializeFlowField 가 사각형을 캐시한다.
		private MapBlocker[] _blockers;

		// 맵 이동 구역 — 도착 위치를 역방향 포털에서 찾는다.
		private MapPortal[] _portals;

		public IReadOnlyList<MonsterSpawnPoint> SpawnPoints
		{
			get
			{
				ensureMarkers();
				return _spawnPoints;
			}
		}

		// SlotIndex 오름차순. 던전 매니저가 이 순서로 채운다.
		public IReadOnlyList<DungeonSpawnSlot> Slots
		{
			get
			{
				ensureMarkers();
				return _slots;
			}
		}

		// NPC 배치 마커. 테이블(NpcSpawn.SpawnPointID)이 이 중에서 자리를 찾는다.
		public IReadOnlyList<NpcSpawnPoint> NpcPoints
		{
			get
			{
				ensureMarkers();
				return _npcPoints;
			}
		}

		public IReadOnlyList<MapPortal> Portals
		{
			get
			{
				ensureMarkers();
				return _portals;
			}
		}

		// 히어로 시작/부활 지점. 마커가 없으면 null — 호출자가 그리드 중심으로 폴백한다.
		public MapAnchor Anchor
		{
			get
			{
				ensureMarkers();
				return _anchor;
			}
		}

		private void ensureMarkers()
		{
			if (_spawnPoints != null)
			{
				return;
			}

			_spawnPoints = this.GetComponentsInChildren<MonsterSpawnPoint>(true);
			_slots = this.GetComponentsInChildren<DungeonSpawnSlot>(true);
			_npcPoints = this.GetComponentsInChildren<NpcSpawnPoint>(true);
			_anchor = this.GetComponentInChildren<MapAnchor>(true);
			_blockers = this.GetComponentsInChildren<MapBlocker>(true);
			_portals = this.GetComponentsInChildren<MapPortal>(true);

			System.Array.Sort(_slots, compareSlotIndex);
		}

		private static int compareSlotIndex(DungeonSpawnSlot a, DungeonSpawnSlot b)
		{
			return a.SlotIndex.CompareTo(b.SlotIndex);
		}

		public void InitializeFlowField()
		{
			if (_flowField == null || _groundMap == null)
			{
				return;
			}

			BoundsInt bounds = _groundMap.cellBounds;
			_boundsMin = bounds.min;

			int width  = bounds.size.x;
			int height = bounds.size.y;
			bool[] walkable = new bool[width * height];
			bool[] blocked  = new bool[width * height];

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Vector3Int cell = new Vector3Int(_boundsMin.x + x, _boundsMin.y + y, 0);
					walkable[y * width + x] = IsCellWalkable(cell);
					blocked[y * width + x]  = (_blockMap != null && _blockMap.GetTile(cell) != null);
				}
			}

			cacheBlockers();
			initializePathing(walkable, width, height);

			// 충돌 핫 루프 캐시 — 타일 전용 배열과 셀 좌표 변환 상수를 보관
			_walkable    = walkable;
			_blocked     = blocked;
			_fieldWidth  = width;
			_fieldHeight = height;
			_cellSize    = (Vector2)_grid.cellSize;
			_cellOrigin  = CellToWorld(_boundsMin);   // 네이티브 호출 1회 — 이후 산술로 변환

			// 월드 AABB 캐시 — 여러 그리드맵이 공존할 때 좌표로 담당 그리드를 찾는 데 쓴다.
			Vector2 min = _cellOrigin - _cellSize * 0.5f;
			Vector2 max = min + new Vector2(_cellSize.x * width, _cellSize.y * height);
			_worldMin = min;
			_worldMax = max;
			_boundsReady = true;
		}

		// 장막(MapPortal) 개폐 후 호출한다 — 활성 블로커를 다시 모으고 경로탐색 통행 배열을 새로 만든다.
		// 타일 셀 캐시(_walkable)는 바뀌지 않으므로 그대로 둔다.
		public void RefreshBlockers()
		{
			if (_walkable == null)
			{
				return;
			}

			cacheBlockers();
			initializePathing(_walkable, _fieldWidth, _fieldHeight);

			if (_hasBaked == true)
			{
				_flowField.Bake(_lastBakeX, _lastBakeY);
			}
		}

		// 경로탐색은 셀 격자라 사각형을 그대로 반영할 수 없다 — 셀 중심이 사각형 안일 때만 막는다.
		// 충돌용 walkable 은 타일만 담은 채로 둔다(블로커는 사각형으로 따로 검사).
		// FlowField.Initialize 가 배열을 복사하므로 임시 배열을 넘겨도 안전하다.
		private void initializePathing(bool[] walkable, int width, int height)
		{
			bool[] pathable = new bool[width * height];
			System.Array.Copy(walkable, pathable, width * height);
			applyBlockersToPathing(pathable, width, height);

			_flowField.Initialize(width, height, pathable);
		}

		// 활성 MapBlocker 의 월드 사각형을 캐시한다. 셀에 굽지 않는 이유 —
		// 셀 단위로 마킹하면 차단 영역이 셀 크기(0.32)만큼 부풀어 스프라이트 주변 빈 공간까지 막힌다.
		// 충돌·발사체 판정은 이 사각형을 직접 검사하므로 콜라이더 크기 그대로 막힌다.
		private void cacheBlockers()
		{
			ensureMarkers();

			_blockerCount = 0;
			if (_blockers == null || _blockers.Length == 0)
			{
				return;
			}

			if (_blockerMin == null || _blockerMin.Length < _blockers.Length)
			{
				_blockerMin             = new Vector2[_blockers.Length];
				_blockerMax             = new Vector2[_blockers.Length];
				_blockerStopsProjectile = new bool[_blockers.Length];
			}

			for (int i = 0; i < _blockers.Length; i++)
			{
				MapBlocker blocker = _blockers[i];
				if (blocker == null || blocker.isActiveAndEnabled == false)
				{
					continue;
				}

				Vector2 min;
				Vector2 max;
				if (blocker.GetWorldRect(out min, out max) == false)
				{
					continue;
				}

				_blockerMin[_blockerCount]             = min;
				_blockerMax[_blockerCount]             = max;
				_blockerStopsProjectile[_blockerCount] = blocker.BlocksProjectile;
				_blockerCount++;
			}
		}

		// 경로탐색(FlowField)용 마킹 — 셀 격자라 사각형을 그대로 반영할 수 없다.
		// 셀 중심이 사각형 안에 들어올 때만 막는다.
		//
		// FlowField 는 차단 셀로는 절대 경로를 내지 않는다(_costField 가 false 면 방향이 zero 이고,
		// 적분값이 ushort.MaxValue 라 이웃으로도 선택되지 않는다). 그래서 위험은 반대쪽 하나뿐이다 —
		// "경로탐색상 통행 가능인데 물리적으로는 사각형이 막고 있는 셀".
		//
		// 사각형 가장자리가 걸친 셀은 유닛이 파고들었다 밀려나며 벽면을 따라 미끄러질 뿐 관통하지 않는다.
		// 진짜 위험은 블로커 두 개 사이의 좁은 틈이다 — 셀 중심 기준으로는 뚫려 보이는데
		// 실제 폭이 유닛 지름보다 좁으면 몬스터가 낀다.
		// 따라서 몬스터가 경로탐색으로 돌아다니는 맵에서는 좁은 틈이 생기지 않는 크기로만 쓴다.
		//
		// _groundMap.cellBounds 밖의 셀은 애초에 배열이 없어 무시된다 — 타일 페인팅과 같은 제약이다.
		private void applyBlockersToPathing(bool[] pathable, int width, int height)
		{
			if (_grid == null || _blockerCount == 0)
			{
				return;
			}

			for (int i = 0; i < _blockerCount; i++)
			{
				Vector2 min = _blockerMin[i];
				Vector2 max = _blockerMax[i];

				Vector3Int minCell = _grid.WorldToCell(new Vector3(min.x, min.y, 0f));
				Vector3Int maxCell = _grid.WorldToCell(new Vector3(max.x, max.y, 0f));

				for (int cy = minCell.y; cy <= maxCell.y; cy++)
				{
					int ly = cy - _boundsMin.y;
					if (ly < 0 || ly >= height)
					{
						continue;
					}

					for (int cx = minCell.x; cx <= maxCell.x; cx++)
					{
						int lx = cx - _boundsMin.x;
						if (lx < 0 || lx >= width)
						{
							continue;
						}

						Vector2 center = CellToWorld(new Vector3Int(cx, cy, 0));
						if (center.x < min.x || center.x > max.x || center.y < min.y || center.y > max.y)
						{
							continue;
						}

						pathable[ly * width + lx] = false;
					}
				}
			}
		}

		private Vector2 _worldMin;
		private Vector2 _worldMax;
		private bool _boundsReady;

		// 이 그리드가 커버하는 월드 영역. InitializeFlowField 이후에만 유효하다.
		public bool ContainsWorldPos(Vector2 worldPos)
		{
			if (_boundsReady == false)
			{
				return false;
			}

			return worldPos.x >= _worldMin.x && worldPos.x <= _worldMax.x
				&& worldPos.y >= _worldMin.y && worldPos.y <= _worldMax.y;
		}

		// 영역 중심 — 히어로 시작 배치 등에 쓴다.
		public Vector2 WorldCenter
		{
			get { return (_worldMin + _worldMax) * 0.5f; }
		}

		public void BakeFlowField(Vector2 worldPos)
		{
			Vector3Int cell = WorldToCell(worldPos);
			int fx = cell.x - _boundsMin.x;
			int fy = cell.y - _boundsMin.y;
			_flowField.Bake(fx, fy);

			_lastBakeX = fx;
			_lastBakeY = fy;
			_hasBaked = true;
		}

		public Vector2 GetFlowDirection(Vector2 worldPos)
		{
			Vector3Int cell = WorldToCell(worldPos);
			int fx = cell.x - _boundsMin.x;
			int fy = cell.y - _boundsMin.y;
			return _flowField.GetDirection(fx, fy);
		}

		public Vector3Int WorldToCell(Vector2 worldPos)
		{
			return _grid.WorldToCell(new Vector3(worldPos.x, worldPos.y, 0f));
		}

		public Vector2 CellToWorld(Vector3Int cellPos)
		{
			return _grid.GetCellCenterWorld(cellPos);
		}

		public TileBase GetGroundTile(Vector3Int cellPos)
		{
			return _groundMap != null ? _groundMap.GetTile(cellPos) : null;
		}

		public TileBase GetObstacleTile(Vector3Int cellPos)
		{
			return _obstacleMap != null ? _obstacleMap.GetTile(cellPos) : null;
		}

		public bool IsCellWalkable(Vector3Int cellPos)
		{
			// 물(obstacleMap)·벽(blockMap) 어느 쪽이든 타일이 있으면 유닛 이동 불가.
			// 벽은 blockMap 에만 칠해도 유닛이 막히므로 중복 페인팅이 필요 없다.
			bool obstacle = (_obstacleMap != null && _obstacleMap.GetTile(cellPos) != null);
			bool block    = (_blockMap != null && _blockMap.GetTile(cellPos) != null);
			return obstacle == false && block == false;
		}

		// 발사체 차단 여부 — 셀에 blockMap 타일이 있거나, MapBlocker 사각형 안이면 true.
		// 타일은 캐시 O(1) 조회, 블로커는 사각형 직접 검사(셀 양자화 없음).
		public bool IsProjectileBlocked(Vector2 worldPos)
		{
			if (_blocked != null)
			{
				int lx = LocalCellX(worldPos.x);
				int ly = LocalCellY(worldPos.y);
				if (lx >= 0 && lx < _fieldWidth && ly >= 0 && ly < _fieldHeight
					&& _blocked[ly * _fieldWidth + lx] == true)
				{
					return true;
				}
			}

			for (int i = 0; i < _blockerCount; i++)
			{
				if (_blockerStopsProjectile[i] == false)
				{
					continue;
				}

				Vector2 min = _blockerMin[i];
				Vector2 max = _blockerMax[i];
				if (worldPos.x >= min.x && worldPos.x <= max.x && worldPos.y >= min.y && worldPos.y <= max.y)
				{
					return true;
				}
			}

			return false;
		}

		// 두 월드점 사이 시야(발사체 경로) 확보 여부 — 셀 크기 간격으로 샘플링하다 차단 셀을 만나면 false.
		public bool HasLineOfSight(Vector2 from, Vector2 to)
		{
			if (_blocked == null)
			{
				return true;
			}

			Vector2 delta = to - from;
			float dist = delta.magnitude;
			if (dist <= 1E-04f)
			{
				return IsProjectileBlocked(from) == false;
			}

			// 셀 절반 간격으로 샘플링 — 얇은 벽도 놓치지 않도록 촘촘하게
			float stepLen = Mathf.Max(_cellSize.x, _cellSize.y) * 0.5f;
			if (stepLen <= 1E-04f)
			{
				stepLen = 0.5f;
			}

			int steps = Mathf.CeilToInt(dist / stepLen);
			Vector2 dir = delta / dist;
			for (int i = 0; i <= steps; i++)
			{
				Vector2 p = from + dir * Mathf.Min(i * stepLen, dist);
				if (IsProjectileBlocked(p) == true)
				{
					return false;
				}
			}

			return true;
		}

		// 셀 통행 가능 여부 — 캐시 배열 O(1) 조회. 범위 밖은 walkable 취급(기존 GetTile 동작과 동일).
		// 핫 루프 전용: lx/ly 는 _boundsMin 기준 필드 로컬 좌표.
		private bool IsWalkableLocal(int lx, int ly)
		{
			if (lx < 0 || lx >= _fieldWidth || ly < 0 || ly >= _fieldHeight)
			{
				return true;
			}

			return _walkable[ly * _fieldWidth + lx];
		}

		// 월드 x/y → 필드 로컬 셀 인덱스 (네이티브 WorldToCell 대신 산술)
		private int LocalCellX(float worldX)
		{
			return Mathf.FloorToInt((worldX - _cellOrigin.x) / _cellSize.x + 0.5f);
		}

		private int LocalCellY(float worldY)
		{
			return Mathf.FloorToInt((worldY - _cellOrigin.y) / _cellSize.y + 0.5f);
		}

		// 연속 원-AABB 충돌 해소 — 반지름 radius 인 원을 장애물 셀 밖으로 밀어낸 위치를 반환한다.
		// 가장 가까운 점 기준 법선으로 수직 성분만 밀어내므로 접선 이동(미끄러짐)이 보존되고,
		// 볼록 코너에서는 꼭짓점 기준 방사형 법선이 나와 반지름 거리로 매끄럽게 돌아 나간다.
		// 핫 루프 — 네이티브 Grid/Tilemap 호출 없이 캐시된 셀 상수 + walkable 배열만 사용.
		public Vector2 ResolveWallCollision(Vector2 pos, float radius)
		{
			if (_walkable == null && _blockerCount == 0)
			{
				return pos;
			}

			Vector2 half      = _cellSize * 0.5f;
			float   radiusSqr = radius * radius;

			// 코너/오목부 안정화를 위해 최대 2패스 — 변화 없으면 조기 종료
			for (int pass = 0; pass < 2; pass++)
			{
				bool pushed = false;

				if (_walkable != null)
				{
					int minX = LocalCellX(pos.x - radius);
					int maxX = LocalCellX(pos.x + radius);
					int minY = LocalCellY(pos.y - radius);
					int maxY = LocalCellY(pos.y + radius);

					for (int ly = minY; ly <= maxY; ly++)
					{
						for (int lx = minX; lx <= maxX; lx++)
						{
							if (IsWalkableLocal(lx, ly) == true)
							{
								continue;
							}

							Vector2 center = new Vector2(_cellOrigin.x + lx * _cellSize.x, _cellOrigin.y + ly * _cellSize.y);
							if (pushOutOfRect(ref pos, center - half, center + half, radius, radiusSqr) == true)
							{
								pushed = true;
							}
						}
					}
				}

				// 오브젝트 장애물은 셀이 아니라 사각형 그대로 밀어낸다 — 같은 패스 안에서 처리해야
				// 셀 벽과 사각형이 만나는 코너에서도 2패스 안정화가 동작한다.
				for (int i = 0; i < _blockerCount; i++)
				{
					Vector2 minB = _blockerMin[i];
					Vector2 maxB = _blockerMax[i];

					// 브로드페이즈 — 원의 AABB 와 겹치지 않으면 정밀 검사 생략
					if (pos.x + radius < minB.x || pos.x - radius > maxB.x
						|| pos.y + radius < minB.y || pos.y - radius > maxB.y)
					{
						continue;
					}

					if (pushOutOfRect(ref pos, minB, maxB, radius, radiusSqr) == true)
					{
						pushed = true;
					}
				}

				if (pushed == false)
				{
					break;
				}
			}

			return pos;
		}

		// 반지름 radius 인 원을 AABB 밖으로 밀어낸다. 밀어냈으면 true.
		// 셀과 MapBlocker 사각형이 같은 수학을 쓴다 — 차단원이 무엇이든 거동이 동일해야 한다.
		private static bool pushOutOfRect(ref Vector2 pos, Vector2 minB, Vector2 maxB, float radius, float radiusSqr)
		{
			Vector2 closest = new Vector2(Mathf.Clamp(pos.x, minB.x, maxB.x), Mathf.Clamp(pos.y, minB.y, maxB.y));
			Vector2 delta   = pos - closest;
			float   distSqr = delta.sqrMagnitude;

			if (distSqr >= radiusSqr)
			{
				return false;
			}

			if (distSqr > 1e-8f)
			{
				float dist = Mathf.Sqrt(distSqr);
				pos += delta / dist * (radius - dist);
			}
			else
			{
				// 중심이 사각형 내부 — 4변 중 최소 침투 축으로 밀어낸다
				float left  = pos.x - minB.x;
				float right = maxB.x - pos.x;
				float down  = pos.y - minB.y;
				float up    = maxB.y - pos.y;
				float minPen = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));

				if (minPen == left)
				{
					pos.x = minB.x - radius;
				}
				else if (minPen == right)
				{
					pos.x = maxB.x + radius;
				}
				else if (minPen == down)
				{
					pos.y = minB.y - radius;
				}
				else
				{
					pos.y = maxB.y + radius;
				}
			}

			return true;
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (_grid == null || _flowField == null || _flowField.Width == 0)
			{
				return;
			}

			if(DEV_ShowDrawGizmos == false)
			{
				return;
			}

			Vector3 cellSize  = _grid.cellSize;
			float   halfW     = cellSize.x * 0.5f;
			float   halfH     = cellSize.y * 0.5f;
			float   innerHalf = Mathf.Min(cellSize.x, cellSize.y) * 0.4f;

			for (int y = 0; y < _flowField.Height; y++)
			{
				for (int x = 0; x < _flowField.Width; x++)
				{
					Vector3Int cellPos = new Vector3Int(_boundsMin.x + x, _boundsMin.y + y, 0);
					Vector3 center = _grid.GetCellCenterWorld(cellPos);

					DEV_DrawGizmosBox(center, new Vector2(halfW, halfH), Color.white);

					if (_flowField.GetCostAt(x, y) == false)
					{
						DEV_DrawGizmosX(center, innerHalf, Color.red);
					}
					else
					{
						Vector2 dir = _flowField.GetDirection(x, y);
						if (dir != Vector2.zero)
						{
							DEV_DrawGizmosArrow(center, dir, innerHalf * 2f, Color.yellow);
						}
					}
				}
			}
		}

		private void DEV_DrawGizmosX(Vector3 center, float size, Color color)
		{
			Gizmos.color = color;
			Gizmos.DrawLine(center + new Vector3(-size, -size, 0f), center + new Vector3(size, size, 0f));
			Gizmos.DrawLine(center + new Vector3(-size,  size, 0f), center + new Vector3(size, -size, 0f));
		}

		private void DEV_DrawGizmosArrow(Vector3 center, Vector2 dir, float length, Color color)
		{
			Gizmos.color = color;
			Vector3 d     = new Vector3(dir.x, dir.y, 0f);
			float   half  = length * 0.5f;
			Vector3 start = center - d * half;
			Vector3 end   = center + d * half;
			Gizmos.DrawLine(start, end);

			float   headSize = length * 0.3f;
			Vector3 right    = new Vector3(-d.y, d.x, 0f);
			Gizmos.DrawLine(end, end - d * headSize + right * headSize);
			Gizmos.DrawLine(end, end - d * headSize - right * headSize);
		}

		private void DEV_DrawGizmosBox(Vector3 center, Vector2 size, Color color)
		{
			Gizmos.color = color;
			float left = center.x - size.x;
			float right = center.x + size.x;
			float up = center.y - size.y;
			float down = center.y + size.y;

			Gizmos.DrawLine(new Vector2(left, up), new Vector2(right, up));
			Gizmos.DrawLine(new Vector2(left, down), new Vector2(right, down));
			Gizmos.DrawLine(new Vector2(left, up), new Vector2(left, down));
			Gizmos.DrawLine(new Vector2(right, up), new Vector2(right, down));
		}
#endif
	}
}
