using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Loading;
using ProjectOne.Map;
using ProjectOne.Monsters;
using ProjectOne.Npcs;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Field
{
	// 필드(4.Field)의 오케스트레이터. 씬이 비어 있으므로 코드가 직접 생성한다.
	//
	// 액트 하나의 필드 그리드맵을 전부 로드해두고 그 안을 이동한다.
	// - 같은 액트 안 필드 이동 → 로드 없음. 이미 떠 있으므로 위치만 옮긴다.
	// - 다른 액트로 이동 → 씬 전환 없이 로딩창 + 그리드맵 전량 교체.
	//
	// 필드 이동에서는 회복하지 않는다 — 액트 진행이 연속된 소모전이 되도록 (기반테이블 5.3).
	public sealed class FieldDirector : MonoBehaviour
	{
		private static FieldDirector _instance;

		// 현재 로드된 액트
		private int _currentActId;

		// 현재 히어로가 있는 필드 (Map.ID)
		private int _currentFieldId;

		private Hero _hero;

		// 스폰 포인트 수집 + 개체 단위 리젠. 필드 전용이다(던전에는 리젠이 없다).
		private FieldMonsterSpawner _spawner;

		// NPC 배치. 필드 맵에도 NpcSpawn 행이 걸릴 수 있다 (MapID 는 MapType 을 가리지 않는다).
		private NpcSpawner _npcSpawner;

		// 플로우필드 재베이크 임계값 — 기준 히어로가 다른 셀로 이동했을 때만 재계산
		private Vector3Int _lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

		public int CurrentActId => _currentActId;
		public int CurrentFieldId => _currentFieldId;

		public static FieldDirector EnsureInstance()
		{
			if (_instance != null)
			{
				return _instance;
			}

			GameObject go = new GameObject("FieldDirector");
			_instance = go.AddComponent<FieldDirector>();
			_instance._spawner = go.AddComponent<FieldMonsterSpawner>();
			_instance._npcSpawner = go.AddComponent<NpcSpawner>();

			// 처치 경험치 지급기는 이벤트 구독형이라 킬이 나기 전에 살아 있어야 한다.
			// MonoSingleton 이 접근 시점에 자동 생성하므로 여기서 한 번 건드린다.
			MonsterKillReward.Instance.Touch();
			return _instance;
		}

		public static bool HasInstance => _instance != null;

		public static FieldDirector Instance => _instance;

		private void OnDestroy()
		{
			// 풀 오브젝트는 씬과 함께 사라지지만 Addressable 핸들은 명시적으로 놓아야 한다.
			if (DropManager.HasInstance == true)
			{
				DropManager.Instance.Clear();
			}

			if (UIManager.HasInstance == true)
			{
				UIManager.Instance.ReleaseWorldGauge();
			}

			if (_instance == this)
			{
				_instance = null;
			}
		}

		// 필드 진입 — 필드가 속한 액트를 통째로 로드하고 히어로를 그 필드에 놓는다.
		public async UniTask Begin(int fieldId, CancellationToken ct)
		{
			Table_Field.Row field = Table_Field.Get(fieldId);
			if (field == null)
			{
				Debug.LogError($"[FieldDirector] Table_Field.Get({fieldId}) == null");
				return;
			}

			// 이전 씬(마을·던전)의 유닛을 먼저 걷어낸다 — 아래에서 히어로를 새로 스폰한다.
			GameplaySceneSetup.ClearGameplayUnits();

			// 카메라 리그는 씬과 함께 파괴된다. 없으면 vcam 이 없어 화면이 히어로를 따라가지 않는다.
			await GameplaySceneSetup.EnsureCameraAsync(ct);

			await loadActAsync(field.ActID, ct);

			// 그리드 중심은 벽 속일 수 있다. 맵 프리팹의 MapAnchor 를 시작 지점으로 쓴다(마을과 같은 규약).
			Vector3 spawnPos = MapManager.Instance.GetAnchorPosition(fieldId);

			_hero = await UnitFactory.Instance.CreateHeroAsync(spawnPos, Faction.Player, true, ct);
			_currentFieldId = fieldId;

			// 월드 오브젝트 풀은 첫 처치 전에 준비돼 있어야 한다 — 없으면 보상 드랍이 유실된다.
			await DropManager.Instance.PrepareAsync(ct);

			// 유닛 위에 뜨는 게이지(보스 캐스팅·상호작용 진행)를 Canvas_World 에 올린다.
			await UIManager.Instance.EnsureWorldGaugeAsync(ct);

			if (_spawner != null)
			{
				_spawner.BeginField(fieldId);
			}
		}

		// 같은 액트 안에서 필드 이동 — 로드가 없다. 회복도 없다.
		public void MoveToField(int fieldId)
		{
			Table_Field.Row field = Table_Field.Get(fieldId);
			if (field == null)
			{
				Debug.LogError($"[FieldDirector] Table_Field.Get({fieldId}) == null");
				return;
			}

			if (field.ActID != _currentActId)
			{
				Debug.LogError($"[FieldDirector] 다른 액트의 필드입니다 — ChangeActAsync 를 쓰세요. field:{fieldId}");
				return;
			}

			enterField(fieldId);
		}

		// 액트 전환 — 씬은 그대로 두고 로딩창만 띄운 뒤 그리드맵을 통째로 교체한다.
		public async UniTask ChangeActAsync(int fieldId, CancellationToken ct)
		{
			Table_Field.Row field = Table_Field.Get(fieldId);
			if (field == null)
			{
				Debug.LogError($"[FieldDirector] Table_Field.Get({fieldId}) == null");
				return;
			}

			if (field.ActID == _currentActId)
			{
				MoveToField(fieldId);
				return;
			}

			await LoadingManager.Instance.ShowAsync(LoadingFlow.ToField, ct);

			// 이전 액트의 잔존 몬스터를 먼저 걷어낸다 — 맵이 사라지면 갈 곳 없는 유닛이 남는다.
			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.ClearAlive();
			}

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, 0f);
			await loadActAsync(field.ActID, ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, 1f);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			enterField(fieldId);
			await UniTask.NextFrame(ct);
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);

			await LoadingManager.Instance.HideAsync();
		}

		private async UniTask loadActAsync(int actId, CancellationToken ct)
		{
			await MapManager.Instance.LoadActAsync(actId, ct);
			_currentActId = actId;
			_lastHeroCell = new Vector3Int(int.MinValue, int.MinValue, 0);

			if (_npcSpawner != null)
			{
				_npcSpawner.Clear();
				_npcSpawner.Refresh(ct);
			}
		}

		// 필드 경계 — 떠나는 필드의 몬스터를 걷어내고 히어로를 옮긴 뒤 새 필드 것으로 갈아끼운다.
		// 몬스터는 히어로가 있는 필드에만 존재한다. 나머지 필드는 리젠 시각만 흐른다.
		private void enterField(int fieldId)
		{
			if (_spawner != null)
			{
				_spawner.EndField();
			}

			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.ClearAlive();
			}

			moveHeroToField(fieldId);

			if (_spawner != null)
			{
				_spawner.BeginField(fieldId);
			}
		}

		private void moveHeroToField(int fieldId)
		{
			_currentFieldId = fieldId;

			if (_hero == null)
			{
				return;
			}

			// 그리드맵이 10000 간격으로 떨어져 있어 필드 이동은 곧 순간이동이다.
			// 도착 지점은 Begin() 과 같은 규약 — 맵 프리팹의 MapAnchor(Entry) 위치.
			_hero.transform.position = MapManager.Instance.GetAnchorPosition(fieldId);
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
