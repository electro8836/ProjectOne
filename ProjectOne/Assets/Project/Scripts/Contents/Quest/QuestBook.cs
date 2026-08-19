using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Quests
{
	// 퀘스트 진행 도메인 모델 — Account 가 소유한다 (Inventory / Loadout / MasteryBook 과 같은 층위).
	//
	// 핵심 규칙 (설계 1.2)
	// - 메인은 항상 1개만 진행하고 포기할 수 없다. 선형 체인이다.
	// - 서브는 최대 2개까지 동시 수주하고 포기할 수 있다. 반복 서브는 무한 반복이다.
	//
	// 목표 판정은 counter 를 쌓는 KillMonster 만 상태를 갖고, 나머지는 매번 재평가한다.
	public sealed class QuestBook
	{
		// 동시 수주 가능한 서브 퀘스트 수 (설계 1.2). 데이터가 아니라 규칙이다.
		public const int MaxSubQuests = 2;

		private int _clearedMainQuestId;

		private readonly QuestProgress _main = new QuestProgress();

		private readonly List<QuestProgress> _subs = new List<QuestProgress>(MaxSubQuests);

		// 지급 결과 버퍼 — 완료는 메인 스레드 단일 경로라 재사용해도 안전하다.
		private static readonly List<GrantedReward> _granted = new List<GrantedReward>(8);

		public QuestBook(QuestDto dto)
		{
			LoadFrom(dto);
		}

		public int ClearedMainQuestId
		{
			get { return _clearedMainQuestId; }
		}

		public QuestProgress Main
		{
			get { return _main; }
		}

		public IReadOnlyList<QuestProgress> Subs
		{
			get { return _subs; }
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 활성화 조건 — 메인·서브 모두 같은 식이다 (설계 4.1).
		public bool IsUnlocked(QuestCatalog.BakedQuest baked)
		{
			if (baked == null || baked.isValid == false)
			{
				return false;
			}

			if (_clearedMainQuestId < baked.row.ReqQuestID)
			{
				return false;
			}

			return Account.Instance.Loadout.Level >= baked.row.ReqLevel;
		}

		public bool IsAccepted(int questId)
		{
			return FindProgress(questId) != null;
		}

		public QuestProgress FindProgress(int questId)
		{
			if (questId <= 0)
			{
				return null;
			}

			if (_main.questId == questId)
			{
				return _main;
			}

			for (int i = 0; i < _subs.Count; i++)
			{
				if (_subs[i].questId == questId)
				{
					return _subs[i];
				}
			}

			return null;
		}

		public bool CanAccept(int questId)
		{
			QuestCatalog.BakedQuest baked = QuestCatalog.Get(questId);
			if (baked == null || IsUnlocked(baked) == false || IsAccepted(questId) == true)
			{
				return false;
			}

			if (baked.row.Category == QuestCategory.Main)
			{
				// 메인은 체인상 다음 하나만 수주할 수 있다.
				if (_main.IsActive == true)
				{
					return false;
				}

				return baked.row.ReqQuestID == _clearedMainQuestId;
			}

			// 서브 슬롯이 차면 수락 불가다. 어느 것을 버릴지는 플레이어가 정한다 (설계 4.5).
			if (_subs.Count >= MaxSubQuests)
			{
				return false;
			}

			// **1회성 서브 퀘스트를 막을 근거가 없다.** 설계 4.4가 서브의 클리어 이력을 저장하지
			// 않기로 했기 때문이다(반복 서브가 무한이라 쌓을 이유가 없었다). 그 결과 IsRepeatable=FALSE
			// 인 서브도 완료 후 다시 수주된다.
			//
			// 1회성 서브가 실제로 필요해지면 저장 항목(완료한 서브 목록)이 하나 늘어야 한다 — STEP 14.
			return true;
		}

		// ── 수락 / 포기 ───────────────────────────────────────────────

		public bool TryAccept(int questId)
		{
			if (CanAccept(questId) == false)
			{
				return false;
			}

			QuestCatalog.BakedQuest baked = QuestCatalog.Get(questId);
			if (baked.row.Category == QuestCategory.Main)
			{
				_main.Begin(questId);
			}
			else
			{
				QuestProgress progress = new QuestProgress();
				progress.Begin(questId);
				_subs.Add(progress);
			}

			EventManager.Instance.Publish(new QuestChangeEvent(questId));
			return true;
		}

		// 메인은 포기할 수 없다 (설계 1.2).
		public bool TryAbandon(int questId)
		{
			for (int i = 0; i < _subs.Count; i++)
			{
				if (_subs[i].questId != questId)
				{
					continue;
				}

				_subs.RemoveAt(i);
				EventManager.Instance.Publish(new QuestChangeEvent(questId));
				return true;
			}

			return false;
		}

		// ── 목표 판정 ─────────────────────────────────────────────────

		// KillMonster 만 누적 카운터를 보고, 나머지는 현재 상태를 재평가한다 (설계 3.3).
		public bool IsObjectiveMet(int questId)
		{
			QuestProgress progress = FindProgress(questId);
			QuestCatalog.BakedQuest baked = QuestCatalog.Get(questId);
			if (progress == null || baked == null || baked.isValid == false)
			{
				return false;
			}

			switch (baked.row.QuestTargetType)
			{
				case QuestTargetType.KillMonster:
				case QuestTargetType.Talk:
					return progress.counter >= GetRequiredCount(baked);

				case QuestTargetType.EquipItemGrade:
					return hasEquipmentOfGrade(baked.reqGrade, baked.reqSlot);

				case QuestTargetType.EquipItemLevel:
					return hasEquipmentOfLevel(baked.reqEquipLevel, baked.reqSlot);

				case QuestTargetType.DungeonClear:
					return DungeonProgress.GetHighestStage(baked.dungeon) >= baked.dungeonStage;

				case QuestTargetType.ReachLevel:
					return Account.Instance.Loadout.Level >= baked.reachLevel;
			}

			return false;
		}

		// 진행도 분모 — KillMonster 만 데이터가 정하고 나머지는 조건 충족형(0/1)이다 (설계 3.3).
		public static int GetRequiredCount(QuestCatalog.BakedQuest baked)
		{
			if (baked.row.QuestTargetType == QuestTargetType.KillMonster)
			{
				return baked.killCount;
			}

			return 1;
		}

		// ── 진행도 갱신 ───────────────────────────────────────────────

		// 몬스터 처치. limitMapId 가 있으면 그 맵에서 잡은 것만 센다 (설계 3.3).
		// 진행도가 바뀌었으면 true — 호출자가 완료 판정을 이어서 한다.
		public bool AddKill(int monsterId, int mapId)
		{
			bool changed = false;

			changed |= addKillTo(_main, monsterId, mapId);
			for (int i = 0; i < _subs.Count; i++)
			{
				changed |= addKillTo(_subs[i], monsterId, mapId);
			}

			return changed;
		}

		private bool addKillTo(QuestProgress progress, int monsterId, int mapId)
		{
			if (progress.IsActive == false)
			{
				return false;
			}

			QuestCatalog.BakedQuest baked = QuestCatalog.Get(progress.questId);
			if (baked == null || baked.isValid == false || baked.row.QuestTargetType != QuestTargetType.KillMonster)
			{
				return false;
			}

			if (baked.monsterId != monsterId)
			{
				return false;
			}

			if (baked.limitMapId > 0 && baked.limitMapId != mapId)
			{
				return false;
			}

			if (progress.counter >= baked.killCount)
			{
				return false;
			}

			progress.counter++;
			return true;
		}

		// 대화. Talk 목표는 이벤트가 아니라 상호작용이 직접 통지한다.
		public bool NotifyTalk(int npcId)
		{
			bool changed = false;

			changed |= notifyTalkTo(_main, npcId);
			for (int i = 0; i < _subs.Count; i++)
			{
				changed |= notifyTalkTo(_subs[i], npcId);
			}

			return changed;
		}

		private bool notifyTalkTo(QuestProgress progress, int npcId)
		{
			if (progress.IsActive == false)
			{
				return false;
			}

			QuestCatalog.BakedQuest baked = QuestCatalog.Get(progress.questId);
			if (baked == null || baked.isValid == false || baked.row.QuestTargetType != QuestTargetType.Talk)
			{
				return false;
			}

			if (baked.talkNpcId != npcId || progress.counter >= 1)
			{
				return false;
			}

			progress.counter = 1;
			return true;
		}

		// ── 완료 ──────────────────────────────────────────────────────

		public bool TryComplete(int questId)
		{
			QuestProgress progress = FindProgress(questId);
			QuestCatalog.BakedQuest baked = QuestCatalog.Get(questId);
			if (progress == null || baked == null || IsObjectiveMet(questId) == false)
			{
				return false;
			}

			grantReward(baked);

			if (baked.row.Category == QuestCategory.Main)
			{
				// 메인 체인은 여기서만 전진한다. NPC 등장 조건도 이 값을 본다.
				_clearedMainQuestId = questId;
				_main.Clear();
			}
			else
			{
				TryAbandonInternal(questId);
			}

			EventManager.Instance.Publish(new QuestChangeEvent(questId));

			// 클리어로 조건이 풀린 자동 수락 퀘스트를 이어서 처리한다.
			RefreshAutoAccept();
			return true;
		}

		private void TryAbandonInternal(int questId)
		{
			for (int i = 0; i < _subs.Count; i++)
			{
				if (_subs[i].questId == questId)
				{
					_subs.RemoveAt(i);
					return;
				}
			}
		}

		// 퀘스트 보상은 고정값이다 — Stat_ExpBonus 는 적 처치분에만 곱한다 (기반테이블 8.1).
		private static void grantReward(QuestCatalog.BakedQuest baked)
		{
			if (baked.row.RewardExp > 0)
			{
				Account.Instance.AddExp(baked.row.RewardExp);
			}

			if (baked.row.RewardGroupID > 0)
			{
				_granted.Clear();
				RewardGranter.Grant(baked.row.RewardGroupID, RewardContext.QuestComplete, _granted);
			}
		}

		// ── 자동 수락 ─────────────────────────────────────────────────

		// AcceptType=Auto 인 퀘스트를 훑어 조건을 만족하면 자동 수주한다.
		// 로그인 직후 / 레벨업 / 메인 클리어 시점에 호출된다.
		public void RefreshAutoAccept()
		{
			IReadOnlyList<QuestCatalog.BakedQuest> chain = QuestCatalog.MainChain;
			for (int i = 0; i < chain.Count; i++)
			{
				tryAutoAccept(chain[i]);
			}

			Dictionary<int, Table_Quest.Row> all = Table_Quest.All();
			Dictionary<int, Table_Quest.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Quest.Row row = e.Current.Value;
				if (row.Category == QuestCategory.Main)
				{
					continue;		// 위에서 이미 돌았다
				}

				tryAutoAccept(QuestCatalog.Get(row.ID));
			}
		}

		private void tryAutoAccept(QuestCatalog.BakedQuest baked)
		{
			if (baked == null || baked.row.AcceptType != QuestAcceptType.Auto)
			{
				return;
			}

			TryAccept(baked.row.ID);
		}

		// ── 장비 조건 ─────────────────────────────────────────────────
		//
		// 장착 시점뿐 아니라 이미 착용 중인 장비도 판정한다 — 그러지 않으면 조건을 이미 만족한
		// 플레이어가 장비를 벗었다 껴야 한다 (설계 3.3). 조건은 항상 "이상"이다.

		private static bool hasEquipmentOfGrade(ItemGradeType grade, EquipSlotTypes slot)
		{
			if (slot != EquipSlotTypes.None)
			{
				EquipmentInstance one = Account.Instance.Loadout.GetEquipped(slot);
				return one != null && one.grade >= grade;
			}

			for (int i = 1; i < LoadoutDto.SlotCount; i++)
			{
				EquipmentInstance instance = Account.Instance.Loadout.GetEquipped((EquipSlotTypes)i);
				if (instance != null && instance.grade >= grade)
				{
					return true;
				}
			}

			return false;
		}

		private static bool hasEquipmentOfLevel(int level, EquipSlotTypes slot)
		{
			if (slot != EquipSlotTypes.None)
			{
				EquipmentInstance one = Account.Instance.Loadout.GetEquipped(slot);
				return one != null && one.level >= level;
			}

			for (int i = 1; i < LoadoutDto.SlotCount; i++)
			{
				EquipmentInstance instance = Account.Instance.Loadout.GetEquipped((EquipSlotTypes)i);
				if (instance != null && instance.level >= level)
				{
					return true;
				}
			}

			return false;
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public QuestDto ToDto()
		{
			QuestDto dto = new QuestDto();
			dto.clearedMainQuestId = _clearedMainQuestId;
			dto.main = _main.ToDto();

			for (int i = 0; i < _subs.Count; i++)
			{
				dto.subs.Add(_subs[i].ToDto());
			}

			return dto;
		}

		public void LoadFrom(QuestDto dto)
		{
			_subs.Clear();

			if (dto == null)
			{
				_clearedMainQuestId = 0;
				_main.Clear();
				return;
			}

			_clearedMainQuestId = dto.clearedMainQuestId;
			_main.LoadFrom(dto.main);

			if (dto.subs == null)
			{
				return;
			}

			for (int i = 0; i < dto.subs.Count; i++)
			{
				if (_subs.Count >= MaxSubQuests)
				{
					Debug.LogWarning("[QuestBook] 저장된 서브 퀘스트가 상한을 넘었습니다 — 초과분을 버립니다.");
					break;
				}

				QuestProgress progress = new QuestProgress();
				progress.LoadFrom(dto.subs[i]);
				if (progress.IsActive == true)
				{
					_subs.Add(progress);
				}
			}
		}
	}
}
