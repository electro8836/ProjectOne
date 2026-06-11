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

		private FlowField _flowField = new FlowField();
		private Vector3Int _boundsMin;

		// 충돌 핫 루프(ResolveWallCollision)용 캐시 — 네이티브 Grid/Tilemap 호출을 산술/배열 조회로 대체.
		private bool[]  _walkable;       // 셀 통행 가능 여부(InitializeFlowField 에서 1회 구축)
		private int     _fieldWidth;
		private int     _fieldHeight;
		private Vector2 _cellSize;       // 셀 크기
		private Vector2 _cellOrigin;     // _boundsMin 셀의 중심 월드좌표

		[SerializeField] bool DEV_ShowDrawGizmos = false;

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

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Vector3Int cell = new Vector3Int(_boundsMin.x + x, _boundsMin.y + y, 0);
					walkable[y * width + x] = IsCellWalkable(cell);
				}
			}

			_flowField.Initialize(width, height, walkable);

			// 충돌 핫 루프 캐시 — 동일 walkable 배열과 셀 좌표 변환 상수를 보관
			_walkable    = walkable;
			_fieldWidth  = width;
			_fieldHeight = height;
			_cellSize    = (Vector2)_grid.cellSize;
			_cellOrigin  = CellToWorld(_boundsMin);   // 네이티브 호출 1회 — 이후 산술로 변환
		}

		public void BakeFlowField(Vector2 worldPos)
		{
			Vector3Int cell = WorldToCell(worldPos);
			int fx = cell.x - _boundsMin.x;
			int fy = cell.y - _boundsMin.y;
			_flowField.Bake(fx, fy);
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
			if (_obstacleMap == null)
			{
				return true;
			}

			return _obstacleMap.GetTile(cellPos) == null;
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
			if (_walkable == null)
			{
				return pos;
			}

			Vector2 half      = _cellSize * 0.5f;
			float   radiusSqr = radius * radius;

			// 코너/오목부 안정화를 위해 최대 2패스 — 변화 없으면 조기 종료
			for (int pass = 0; pass < 2; pass++)
			{
				int minX = LocalCellX(pos.x - radius);
				int maxX = LocalCellX(pos.x + radius);
				int minY = LocalCellY(pos.y - radius);
				int maxY = LocalCellY(pos.y + radius);
				bool pushed = false;

				for (int ly = minY; ly <= maxY; ly++)
				{
					for (int lx = minX; lx <= maxX; lx++)
					{
						if (IsWalkableLocal(lx, ly) == true)
						{
							continue;
						}

						Vector2 center  = new Vector2(_cellOrigin.x + lx * _cellSize.x, _cellOrigin.y + ly * _cellSize.y);
						Vector2 minB    = center - half;
						Vector2 maxB    = center + half;
						Vector2 closest = new Vector2(Mathf.Clamp(pos.x, minB.x, maxB.x), Mathf.Clamp(pos.y, minB.y, maxB.y));
						Vector2 delta   = pos - closest;
						float   distSqr = delta.sqrMagnitude;

						if (distSqr >= radiusSqr)
						{
							continue;
						}

						if (distSqr > 1e-8f)
						{
							float dist = Mathf.Sqrt(distSqr);
							pos += delta / dist * (radius - dist);
						}
						else
						{
							// 중심이 셀 내부 — 4변 중 최소 침투 축으로 밀어낸다
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
