using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Quests
{
	// 퀘스트 진행 도메인 모델 — Account 가 소유한다 (Inventory / Loadout / MasteryBook 과 같은 층위).
	//
	// 핵심 규칙
	// - 진행 중 퀘스트는 항상 1개다. 메인/서브 구분이 없고 포기할 수 없다.
	// - ID 오름차순이 곧 진행 순서다. 이전 퀘스트를 클리어해야 다음 퀘스트가 열린다.
	//
	// 목표 판정은 counter 를 쌓는 MonsterKill / EliteKill / BossKill 만 상태를 갖고,
	// DungeonClear / ReachLevel 은 조건 충족형이라 매번 재평가한다 — 그래야 이미 조건을
	// 만족한 채로 퀘스트가 열려도 즉시 달성된다.
	public sealed class QuestBook
	{
		private int _clearedQuestId;

		private readonly QuestProgress _current = new QuestProgress();

		// 지급 결과 버퍼 — 완료는 메인 스레드 단일 경로라 재사용해도 안전하다.
		private static readonly List<GrantedReward> _granted = new List<GrantedReward>(8);

		public QuestBook(QuestDto dto)
		{
			LoadFrom(dto);
		}

		public int ClearedQuestId
		{
			get { return _clearedQuestId; }
		}

		public QuestProgress Current
		{
			get { return _current; }
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 진행 중 퀘스트의 굽힌 데이터. 진행 중이 아니거나 파싱 실패면 null.
		public QuestCatalog.BakedQuest GetCurrentBaked()
		{
			if (_current.IsActive == false)
			{
				return null;
			}

			QuestCatalog.BakedQuest baked = QuestCatalog.Get(_current.questId);
			if (baked == null || baked.isValid == false)
			{
				return null;
			}

			return baked;
		}

		// ── 목표 판정 ─────────────────────────────────────────────────

		public bool IsObjectiveMet(int questId)
		{
			if (_current.IsActive == false || _current.questId != questId)
			{
				return false;
			}

			QuestCatalog.BakedQuest baked = GetCurrentBaked();
			if (baked == null)
			{
				return false;
			}

			switch (baked.row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.EliteKill:
				case QuestTargetType.BossKill:
					return _current.counter >= GetRequiredCount(baked);

				case QuestTargetType.DungeonClear:
					return DungeonProgress.GetHighestStage(baked.dungeon) >= baked.dungeonStage;

				case QuestTargetType.ReachLevel:
					return Account.Instance.Loadout.Level >= baked.reachLevel;
			}

			return false;
		}

		// 진행도 분모. BossKill / DungeonClear 는 조건 충족형이라 1 이다.
		public static int GetRequiredCount(QuestCatalog.BakedQuest baked)
		{
			switch (baked.row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.EliteKill:
					return baked.killCount;

				case QuestTargetType.ReachLevel:
					return baked.reachLevel;
			}

			return 1;
		}

		// 진행도 분자. ReachLevel 만 카운터가 아니라 현재 레벨을 쓴다 — 목표를 넘겨도 분모에서 멈춘다.
		public int GetCurrentCount(QuestCatalog.BakedQuest baked)
		{
			switch (baked.row.QuestTargetType)
			{
				case QuestTargetType.MonsterKill:
				case QuestTargetType.EliteKill:
				case QuestTargetType.BossKill:
					return _current.counter;

				case QuestTargetType.DungeonClear:
					return IsObjectiveMet(baked.row.ID) ? 1 : 0;

				case QuestTargetType.ReachLevel:
					return Mathf.Min(Account.Instance.Loadout.Level, baked.reachLevel);
			}

			return 0;
		}

		// ── 진행도 갱신 ───────────────────────────────────────────────

		// 몬스터 처치. mapId 는 사망 좌표로 역산한 맵, monsterType 은 그 몬스터의 등급이다.
		// 진행도가 바뀌었으면 true — 호출자가 완료 판정을 이어서 한다.
		public bool AddKill(int mapId, MonsterType monsterType)
		{
			QuestCatalog.BakedQuest baked = GetCurrentBaked();
			if (baked == null || baked.mapId != mapId)
			{
				return false;
			}

			if (baked.row.QuestTargetType == QuestTargetType.MonsterKill)
			{
				if (_current.counter >= baked.killCount)
				{
					return false;
				}

				_current.counter++;
				return true;
			}

			if (baked.row.QuestTargetType == QuestTargetType.EliteKill)
			{
				if (monsterType != MonsterType.Elite || _current.counter >= baked.killCount)
				{
					return false;
				}

				_current.counter++;
				return true;
			}

			if (baked.row.QuestTargetType == QuestTargetType.BossKill)
			{
				if (monsterType != MonsterType.Boss || _current.counter >= 1)
				{
					return false;
				}

				_current.counter = 1;
				return true;
			}

			return false;
		}

		// ── 완료 ──────────────────────────────────────────────────────

		public bool TryComplete(int questId)
		{
			QuestCatalog.BakedQuest baked = GetCurrentBaked();
			if (baked == null || _current.questId != questId || IsObjectiveMet(questId) == false)
			{
				return false;
			}

			grantReward(baked);

			// 체인은 여기서만 전진한다. NPC 등장 조건도 이 값을 본다.
			_clearedQuestId = questId;
			_current.Clear();

			EventManager.Instance.Publish(new QuestChangeEvent(questId));

			// 다음 퀘스트를 바로 연다.
			RefreshCurrent();
			return true;
		}

		// 퀘스트 보상은 고정값이다 — Stat_ExpBonus 는 적 처치분에만 곱한다 (기반테이블 8.1).
		private static void grantReward(QuestCatalog.BakedQuest baked)
		{
			if (baked.row.RewardGroupID <= 0)
			{
				return;
			}

			_granted.Clear();
			RewardGranter.Grant(baked.row.RewardGroupID, RewardContext.QuestComplete, _granted);
		}

		// ── 진행 퀘스트 갱신 ──────────────────────────────────────────

		// 진행 중 퀘스트가 없으면 체인에서 다음 것을 연다.
		// 마지막까지 다 깼으면 아무 일도 하지 않는다 — Current 가 비어 있는 채로 남는다.
		public void RefreshCurrent()
		{
			if (_current.IsActive == true)
			{
				return;
			}

			QuestCatalog.BakedQuest next = QuestCatalog.GetNext(_clearedQuestId);
			if (next == null)
			{
				return;
			}

			_current.Begin(next.row.ID);
			EventManager.Instance.Publish(new QuestChangeEvent(next.row.ID));
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public QuestDto ToDto()
		{
			QuestDto dto = new QuestDto();
			dto.clearedQuestId = _clearedQuestId;
			dto.current = _current.ToDto();
			return dto;
		}

		public void LoadFrom(QuestDto dto)
		{
			if (dto == null)
			{
				_clearedQuestId = 0;
				_current.Clear();
				return;
			}

			_clearedQuestId = dto.clearedQuestId;
			_current.LoadFrom(dto.current);
		}
	}
}
