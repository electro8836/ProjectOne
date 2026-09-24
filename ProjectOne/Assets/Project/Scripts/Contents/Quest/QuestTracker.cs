using EDT;
using ProjectOne.Event;
using ProjectOne.Map;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Quests
{
	// 퀘스트 진행 추적 — 게임 이벤트를 QuestBook 의 진행도 갱신으로 옮긴다.
	//
	// MonsterKillReward 와 같은 골격의 이벤트 구독형 MonoSingleton 이다.
	// 다른 점은 **영속**이라는 것 — 마을·필드·던전을 가로질러 살아 있어야 한다.
	public sealed class QuestTracker : MonoSingleton<QuestTracker>
	{
		protected override bool Persistent => true;

		// 완료 판정은 재진입한다 — 보상 경험치 지급이 CharacterChangeEvent 를 발행하고
		// 그 핸들러가 다시 완료 판정을 부른다. 안쪽 요청은 플래그로 미뤘다가
		// 바깥 순회가 끝난 뒤 한 번 더 돈다.
		private bool _checking;
		private bool _recheckRequested;

		protected override void Awake()
		{
			base.Awake();

			EventManager.Instance.Subscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(onDungeonStageCleared);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(onDungeonStageCleared);

			base.OnDestroy();
		}

		// 인스턴스 생성만을 목적으로 하는 호출 지점 — Instance 접근이 곧 생성이라 본문이 필요 없다.
		public void Touch()
		{
		}

		// 로그인 직후 호출 — 저장된 진행도로 시작하고 진행할 퀘스트를 연다.
		public void OnDataLoaded()
		{
			Account.Instance.Quests.RefreshCurrent();
			CheckCompletable();
		}

		// CompleteType=Auto 인 퀘스트를 목표 달성 즉시 완료 처리한다.
		// UI 완료형은 플레이어가 눌러야 하므로 여기서 건드리지 않는다.
		public void CheckCompletable()
		{
			if (_checking == true)
			{
				_recheckRequested = true;
				return;
			}

			_checking = true;

			do
			{
				_recheckRequested = false;
				checkOnce();
			}
			while (_recheckRequested == true);

			_checking = false;
		}

		private void checkOnce()
		{
			QuestBook book = Account.Instance.Quests;

			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();
			if (baked == null || baked.row.CompleteType != QuestCompleteType.Auto)
			{
				return;
			}

			book.TryComplete(baked.row.ID);
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onMonsterKill(MonsterKillEvent e)
		{
			// 지역 한정 목표는 "어디서 잡았는가"를 본다. 그리드맵이 10000 간격이라
			// 사망 좌표만으로 지역이 확정된다.
			int mapId = 0;
			if (MapManager.HasInstance == true)
			{
				mapId = MapManager.Instance.GetMapIdAt(e.Position);
			}

			// 이벤트에는 등급이 실리지 않는다 — EliteKill / BossKill 판정은 테이블로 역조회한다.
			MonsterType monsterType = MonsterType.None;
			Table_Monster.Row monster = Table_Monster.Get(e.MonsterID);
			if (monster != null)
			{
				monsterType = monster.MonsterType;
			}

			if (Account.Instance.Quests.AddKill(mapId, monsterType) == true)
			{
				publishActive();
			}

			CheckCompletable();
		}

		// 레벨업 — ReachLevel 목표가 풀린다. 전용 이벤트가 없어 이 알림으로 재평가한다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			CheckCompletable();
			publishActive();
		}

		private void onDungeonStageCleared(DungeonStageClearedEvent e)
		{
			CheckCompletable();
			publishActive();
		}

		// 진행도가 바뀐 퀘스트를 알린다. HUD 는 이 이벤트로만 갱신한다.
		private void publishActive()
		{
			QuestProgress current = Account.Instance.Quests.Current;
			if (current.IsActive == false)
			{
				return;
			}

			EventManager.Instance.Publish(new QuestChangeEvent(current.questId));
		}
	}
}
