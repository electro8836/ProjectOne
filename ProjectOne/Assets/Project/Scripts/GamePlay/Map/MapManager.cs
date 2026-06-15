using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Resources;

namespace ProjectOne.Map
{
	// 맵 로드/수명/플로우필드 질의를 전담하는 Battle 수명 매니저.
	// 플로우필드 계산(좌표/통행/BFS)만 담당 — "누구를 향해 베이크할지"는 호출자(전투 규칙)가 정한다.
	public class MapManager : MonoSingleton<MapManager>
	{
		// 전투 전용 — 전투씬 수명에만 존재(로비 등으로 따라가지 않음)
		protected override bool Persistent => false;

		private GameObject _mapInstance;
		private TilemapGrid _current;

		public bool HasMap => _current != null;
		public TilemapGrid Current => _current;

		// 맵 프리팹을 인스턴스화하고 플로우필드를 초기화한다. 성공 시 true.
		public async UniTask<bool> LoadMapAsync(string mapPrefabAddress, CancellationToken ct = default)
		{
			if (string.IsNullOrEmpty(mapPrefabAddress))
			{
				Debug.LogError("[MapManager] 맵 주소가 비어 있음");
				return false;
			}

			UnloadMap();

			GameObject mapGo = await AddressableHelper.InstantiateAsync(mapPrefabAddress, null, true, ct);

			TilemapGrid tilemapGrid = mapGo.GetComponent<TilemapGrid>();
			if (tilemapGrid == null)
			{
				Debug.LogError($"[MapManager] 맵 프리팹에 TilemapGrid 없음: {mapPrefabAddress}");
				AddressableHelper.ReleaseInstance(mapGo);
				return false;
			}

			tilemapGrid.InitializeFlowField();
			_mapInstance = mapGo;
			_current = tilemapGrid;
			return true;
		}

		public void UnloadMap()
		{
			if (_mapInstance != null)
			{
				AddressableHelper.ReleaseInstance(_mapInstance);
				_mapInstance = null;
			}

			_current = null;
		}

		// 주어진 월드 위치를 플로우필드 타겟으로 재베이크 (호출자가 베이크 시점을 결정)
		public void BakeFlowField(Vector2 targetWorldPos)
		{
			if (_current != null)
			{
				_current.BakeFlowField(targetWorldPos);
			}
		}

		public Vector2 GetFlowDirection(Vector2 worldPos)
		{
			return _current != null ? _current.GetFlowDirection(worldPos) : Vector2.zero;
		}

		// 반지름 radius 인 원을 장애물 밖으로 밀어낸 위치를 반환 (맵 없으면 그대로)
		public Vector2 ResolveWallCollision(Vector2 pos, float radius)
		{
			return _current != null ? _current.ResolveWallCollision(pos, radius) : pos;
		}

		public Vector3Int WorldToCell(Vector2 worldPos)
		{
			return _current != null ? _current.WorldToCell(worldPos) : default;
		}

		// 해당 월드 위치가 발사체 차단 셀인지 (맵 없으면 차단 없음)
		public bool IsProjectileBlocked(Vector2 worldPos)
		{
			return _current != null ? _current.IsProjectileBlocked(worldPos) : false;
		}

		// 두 월드점 사이 발사체 경로 시야 확보 여부 (맵 없으면 항상 확보)
		public bool HasLineOfSight(Vector2 from, Vector2 to)
		{
			return _current != null ? _current.HasLineOfSight(from, to) : true;
		}
	}
}
