using System;
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

			GameObject mapGo;
			try
			{
				mapGo = await AddressableHelper.InstantiateAsync(mapPrefabAddress, null, true, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Debug.LogError($"[MapManager] 맵 인스턴스화 실패: {mapPrefabAddress} ({e.Message})");
				return false;
			}

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

		// 맵이 없으면 통행 가능으로 간주 (기존 UnitMover 동작과 동일)
		public bool IsWalkable(Vector2 position, float radius)
		{
			return _current == null || _current.IsWalkable(position, radius);
		}

		public Vector3Int WorldToCell(Vector2 worldPos)
		{
			return _current != null ? _current.WorldToCell(worldPos) : default;
		}
	}
}
