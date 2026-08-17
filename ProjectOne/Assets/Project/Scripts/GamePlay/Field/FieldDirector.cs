using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Loading;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Unit;

namespace ProjectOne.Field
{
	// 필드(4.Field)의 오케스트레이터. 씬이 비어 있으므로 코드가 직접 생성한다.
	//
	// 액트 하나의 스테이지 그리드맵을 전부 로드해두고 그 안을 이동한다.
	// - 같은 액트 안 스테이지 이동 → 로드 없음. 이미 떠 있으므로 위치만 옮긴다.
	// - 다른 액트로 이동 → 씬 전환 없이 로딩창 + 그리드맵 전량 교체.
	//
	// 스테이지 이동에서는 회복하지 않는다 — 액트 진행이 연속된 소모전이 되도록 (기반테이블 5.3).
	public sealed class FieldDirector : MonoBehaviour
	{
		private static FieldDirector _instance;

		// 현재 로드된 액트
		private int _currentActId;

		// 현재 히어로가 있는 스테이지 (Map.ID)
		private int _currentStageId;

		private Hero _hero;

		// 스폰 포인트 수집 + 개체 단위 리젠. 필드 전용이다(던전에는 리젠이 없다).
		private FieldMonsterSpawner _spawner;

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		public int CurrentActId => _currentActId;
		public int CurrentStageId => _currentStageId;

		public static FieldDirector EnsureInstance()
		{
			if (_instance != null)
			{
				return _instance;
			}

			GameObject go = new GameObject("FieldDirector");
			_instance = go.AddComponent<FieldDirector>();
			_instance._spawner = go.AddComponent<FieldMonsterSpawner>();

			// 처치 경험치 지급기는 이벤트 구독형이라 킬이 나기 전에 살아 있어야 한다.
			// MonoSingleton 이 접근 시점에 자동 생성하므로 여기서 한 번 건드린다.
			MonsterKillReward.Instance.Touch();
			return _instance;
		}

		public static bool HasInstance => _instance != null;

		public static FieldDirector Instance => _instance;

		private void OnDestroy()
		{
			if (_instance == this)
			{
				_instance = null;
			}
		}

		// 필드 진입 — 스테이지가 속한 액트를 통째로 로드하고 히어로를 그 스테이지에 놓는다.
		public async UniTask Begin(int stageId, CancellationToken ct)
		{
			Table_MapStage.Row stage = Table_MapStage.Get(stageId);
			if (stage == null)
			{
				Debug.LogError($"[FieldDirector] Table_MapStage.Get({stageId}) == null");
				return;
			}

			await loadActAsync(stage.ActID, ct);

			_hero = await UnitFactory.Instance.CreateHeroAsync(GetStageCenter(stageId), Faction.Player, true, ct);
			moveHeroToStage(stageId);
		}

		// 같은 액트 안에서 스테이지 이동 — 로드가 없다. 회복도 없다.
		public void MoveToStage(int stageId)
		{
			Table_MapStage.Row stage = Table_MapStage.Get(stageId);
			if (stage == null)
			{
				Debug.LogError($"[FieldDirector] Table_MapStage.Get({stageId}) == null");
				return;
			}

			if (stage.ActID != _currentActId)
			{
				Debug.LogError($"[FieldDirector] 다른 액트의 스테이지입니다 — ChangeActAsync 를 쓰세요. stage:{stageId}");
				return;
			}

			moveHeroToStage(stageId);
		}

		// 액트 전환 — 씬은 그대로 두고 로딩창만 띄운 뒤 그리드맵을 통째로 교체한다.
		public async UniTask ChangeActAsync(int stageId, CancellationToken ct)
		{
			Table_MapStage.Row stage = Table_MapStage.Get(stageId);
			if (stage == null)
			{
				Debug.LogError($"[FieldDirector] Table_MapStage.Get({stageId}) == null");
				return;
			}

			if (stage.ActID == _currentActId)
			{
				MoveToStage(stageId);
				return;
			}

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToField, ct);

			// 이전 액트의 잔존 몬스터를 먼저 걷어낸다 — 맵이 사라지면 갈 곳 없는 유닛이 남는다.
			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.ClearAlive();
			}

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, 0f);
			await loadActAsync(stage.ActID, ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, 1f);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			moveHeroToStage(stageId);
			await UniTask.NextFrame(ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);

			await LoadingManager.Instance.HideAsync();
		}

		// 스테이지의 그리드맵 중심 좌표. 히어로 배치 기준점이다.
		public static Vector3 GetStageCenter(int stageId)
		{
			Table_MapStage.Row stage = Table_MapStage.Get(stageId);
			if (stage == null)
			{
				return Vector3.zero;
			}

			Table_Act.Row act = Table_Act.Get(stage.ActID);
			return MapManager.GetStageOrigin(act != null ? act.Order : 1, stage.Order);
		}

		private async UniTask loadActAsync(int actId, CancellationToken ct)
		{
			await MapManager.Instance.LoadActAsync(actId, ct);
			_currentActId = actId;
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

			// 맵이 떠야 스폰 포인트를 찾을 수 있다.
			if (_spawner != null)
			{
				_spawner.BeginAct(actId);
			}
		}

		private void moveHeroToStage(int stageId)
		{
			_currentStageId = stageId;

			if (_hero == null)
			{
				return;
			}

			// 그리드맵이 10000 간격으로 떨어져 있어 스테이지 이동은 곧 순간이동이다.
			_hero.transform.position = GetStageCenter(stageId);
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);
		}

		private void Update()
		{
			updateFlowFieldBake();
		}

		// 기준 히어로의 셀 변경 시에만 플로우필드 재베이크 (히어로가 있는 그리드만)
		private void updateFlowFieldBake()
		{
			if (MapManager.HasInstance == false || MapManager.Instance.HasMap == false)
			{
				return;
			}

			if (_hero == null || _hero.IsDead == true)
			{
				return;
			}

			Vector3Int currentCell = MapManager.Instance.WorldToCell(_hero.transform.position);
			if (currentCell == _lastHeroCell)
			{
				return;
			}

			_lastHeroCell = currentCell;
			MapManager.Instance.BakeFlowField(_hero.transform.position);
		}
	}
}
