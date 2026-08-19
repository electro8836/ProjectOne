using System.Collections.Generic;
using EDT;
using UnityEngine;
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
	//
	// Talk 목표만 이벤트가 아니라 NpcInteraction 이 직접 통지한다. 대화는 유저 조작이라
	// 이벤트로 흘리면 "언제 말을 걸었는가"가 흐려진다.
	public sealed class QuestTracker : MonoSingleton<QuestTracker>
	{
		protected override bool Persistent => true;

		// 완료 판정 대상 임시 버퍼 — 순회 중 목록이 바뀌므로(완료하면 서브가 빠진다) 복사해서 돈다.
		private readonly List<int> _checkBuffer = new List<int>(4);

		// 완료 판정은 재진입한다 — 보상 경험치 지급이 CharacterChangeEvent 를 발행하고
		// 그 핸들러가 다시 완료 판정을 부른다. 버퍼를 공유하므로 중첩되면 바깥 순회가 깨진다.
		// 안쪽 요청은 플래그로 미뤘다가 바깥 순회가 끝난 뒤 한 번 더 돈다.
		private bool _checking;
		private bool _recheckRequested;

		protected override void Awake()
		{
			base.Awake();

			EventManager.Instance.Subscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(onPresetChanged);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(onDungeonStageCleared);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(onMonsterKill);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(onDungeonStageCleared);

			base.OnDestroy();
		}

		// 인스턴스 생성만을 목적으로 하는 호출 지점 — Instance 접근이 곧 생성이라 본문이 필요 없다.
		public void Touch()
		{
		}

		// 로그인 직후 호출 — 저장된 진행도로 시작하고 자동 수락 퀘스트를 훑는다.
		public void OnDataLoaded()
		{
			Account.Instance.Quests.RefreshAutoAccept();
			CheckCompletable();
		}

		// 대화로 Talk 목표를 달성시킨다. 목표 갱신 → 완료 판정 순서를 지킨다 (설계 4.5).
		public void NotifyTalk(int npcId)
		{
			if (Account.Instance.Quests.NotifyTalk(npcId) == true)
			{
				publishActive();
			}

			CheckCompletable();
		}

		// CompleteType=Auto 인 퀘스트를 목표 달성 즉시 완료 처리한다.
		// Npc / UI 완료형은 플레이어가 눌러야 하므로 여기서 건드리지 않는다.
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

			_checkBuffer.Clear();
			if (book.Main.IsActive == true)
			{
				_checkBuffer.Add(book.Main.questId);
			}

			IReadOnlyList<QuestProgress> subs = book.Subs;
			for (int i = 0; i < subs.Count; i++)
			{
				_checkBuffer.Add(subs[i].questId);
			}

			for (int i = 0; i < _checkBuffer.Count; i++)
			{
				QuestCatalog.BakedQuest baked = QuestCatalog.Get(_checkBuffer[i]);
				if (baked == null || baked.row.CompleteType != QuestCompleteType.Auto)
				{
					continue;
				}

				book.TryComplete(_checkBuffer[i]);
			}

			_checkBuffer.Clear();
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onMonsterKill(MonsterKillEvent e)
		{
			// 지역 한정 목표는 "어디서 잡았는가"를 본다. 그리드맵이 10000 간격이라
			// 사망 좌표만으로 지역이 확정된다 (설계 3.3).
			int mapId = 0;
			if (MapManager.HasInstance == true)
			{
				mapId = MapManager.Instance.GetMapIdAt(e.Position);
			}

			if (Account.Instance.Quests.AddKill(e.MonsterID, mapId) == true)
			{
				publishActive();
			}

			CheckCompletable();
		}

		// 레벨업 — ReachLevel 목표와 ReqLevel 로 막혀 있던 자동 수락이 함께 풀린다.
		private void onCharacterChanged(CharacterChangeEvent e)
		{
			Account.Instance.Quests.RefreshAutoAccept();
			CheckCompletable();
		}

		// 장비 조건은 카운터가 없어 매번 재평가한다 — 벗었다 껴야 하는 일이 없다 (설계 3.3).
		private void onPresetChanged(PresetChangeEvent e)
		{
			CheckCompletable();
		}

		private void onDungeonStageCleared(DungeonStageClearedEvent e)
		{
			CheckCompletable();
		}

		// 진행도가 바뀐 퀘스트를 알린다. HUD 는 이 이벤트로만 갱신한다 (설계 5.5).
		private void publishActive()
		{
			QuestBook book = Account.Instance.Quests;

			if (book.Main.IsActive == true)
			{
				EventManager.Instance.Publish(new QuestChangeEvent(book.Main.questId));
			}

			IReadOnlyList<QuestProgress> subs = book.Subs;
			for (int i = 0; i < subs.Count; i++)
			{
				EventManager.Instance.Publish(new QuestChangeEvent(subs[i].questId));
			}
		}
	}
}
