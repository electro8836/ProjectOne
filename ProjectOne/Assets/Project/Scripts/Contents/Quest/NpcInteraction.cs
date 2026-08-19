using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.UserData;

namespace ProjectOne.Quests
{
	// NPC 상호작용 1회의 판정 결과. UI 가 이 값을 그대로 소비한다.
	public struct NpcInteractionResult
	{
		public int npcId;

		// 어느 대사를 띄울 것인가. Default 는 퀘스트 무관 기본 대사다.
		public DialogTriggerType trigger;

		// trigger 가 퀘스트 계열일 때만 유효하다. 0이면 퀘스트와 무관하다.
		public int questId;

		// 대사 페이지. Text 를 '|' 로 나눈 것이다 (설계 5.4). 대사가 없으면 길이 0.
		public string[] lines;

		// trigger == Default 일 때 열 기능. None 이면 그냥 대사만 띄운다.
		public NpcType function;
		public int functionId;
	}

	// NPC 상호작용 우선순위 판정 (설계 5.5).
	//
	//   1. 완료 가능한 퀘스트 있음  → QuestComplete
	//   2. 수락 가능한 퀘스트 있음  → QuestAccept
	//   3. 진행 중 퀘스트 있음      → QuestProgress
	//   4. 그 외                    → NpcType 기능 UI 또는 Default
	//
	// **판정만 한다.** 대화창을 띄우고 수락/완료 버튼을 붙이는 것은 UI 몫이다.
	// 퀘스트 마커(느낌표/물음표)도 같은 판정을 쓴다.
	public static class NpcInteraction
	{
		private static readonly string[] _emptyLines = new string[0];

		// 대사 구분자. 셀 내 줄바꿈(Alt+Enter)은 컨버터에서 행이 깨질 위험이 있어 쓰지 않는다 (설계 5.4).
		private static readonly char[] _lineSeparator = new char[] { '|' };

		public static NpcInteractionResult Resolve(int npcId)
		{
			NpcInteractionResult result = default(NpcInteractionResult);
			result.npcId = npcId;
			result.lines = _emptyLines;

			Table_Npc.Row npc = Table_Npc.Get(npcId);
			if (npc == null)
			{
				Debug.LogError($"[NpcInteraction] Npc {npcId} 행이 없습니다.");
				result.trigger = DialogTriggerType.Default;
				return result;
			}

			QuestBook book = Account.Instance.Quests;

			// 1. 완료 가능 — 이 NPC 가 완료 담당이고 목표를 채웠는가.
			int completable = findCompletable(book, npcId);
			if (completable > 0)
			{
				return fill(result, npc, DialogTriggerType.QuestComplete, completable);
			}

			// 2. 수락 가능
			int acceptable = findAcceptable(book, npcId);
			if (acceptable > 0)
			{
				return fill(result, npc, DialogTriggerType.QuestAccept, acceptable);
			}

			// 3. 진행 중 — 단, 목표를 이미 달성했는데 완료 담당이 아니면 "아직 남았다"고 말하면 안 된다.
			//    그 경우는 Default 로 떨어진다 (설계 5.5 폴백 규칙).
			int inProgress = findInProgress(book, npcId);
			if (inProgress > 0)
			{
				return fill(result, npc, DialogTriggerType.QuestProgress, inProgress);
			}

			// 4. 그 외 — 기능 UI 또는 기본 대사
			return fill(result, npc, DialogTriggerType.Default, 0);
		}

		// 퀘스트 마커 표시용. 상호작용과 같은 판정을 쓴다 — 두 벌로 나뉘면 반드시 어긋난다.
		public static DialogTriggerType GetMarker(int npcId)
		{
			return Resolve(npcId).trigger;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int findCompletable(QuestBook book, int npcId)
		{
			IReadOnlyList<QuestCatalog.BakedQuest> quests = QuestCatalog.GetCompletableAt(npcId);
			for (int i = 0; i < quests.Count; i++)
			{
				int questId = quests[i].row.ID;
				if (book.IsAccepted(questId) == true && book.IsObjectiveMet(questId) == true)
				{
					return questId;
				}
			}

			return 0;
		}

		private static int findAcceptable(QuestBook book, int npcId)
		{
			IReadOnlyList<QuestCatalog.BakedQuest> quests = QuestCatalog.GetAcceptableAt(npcId);
			for (int i = 0; i < quests.Count; i++)
			{
				int questId = quests[i].row.ID;
				if (book.CanAccept(questId) == true)
				{
					return questId;
				}
			}

			return 0;
		}

		// 이 NPC 와 엮인 진행 중 퀘스트 — 완료 담당이거나 대화 대상인 것만 본다.
		// 무관한 NPC 가 남의 퀘스트 진행 대사를 하면 안 된다.
		private static int findInProgress(QuestBook book, int npcId)
		{
			int found = matchInProgress(book, book.Main, npcId);
			if (found > 0)
			{
				return found;
			}

			IReadOnlyList<QuestProgress> subs = book.Subs;
			for (int i = 0; i < subs.Count; i++)
			{
				found = matchInProgress(book, subs[i], npcId);
				if (found > 0)
				{
					return found;
				}
			}

			return 0;
		}

		private static int matchInProgress(QuestBook book, QuestProgress progress, int npcId)
		{
			if (progress == null || progress.IsActive == false)
			{
				return 0;
			}

			QuestCatalog.BakedQuest baked = QuestCatalog.Get(progress.questId);
			if (baked == null)
			{
				return 0;
			}

			bool related = baked.row.CompleteNpcID == npcId
				|| (baked.row.QuestTargetType == QuestTargetType.Talk && baked.talkNpcId == npcId);
			if (related == false)
			{
				return 0;
			}

			// 목표를 이미 달성했으면 진행 대사를 쓰지 않는다 — 완료 담당이면 위 1단계에서 이미 걸렀다.
			if (book.IsObjectiveMet(progress.questId) == true)
			{
				return 0;
			}

			return progress.questId;
		}

		// 해당 QuestID 대사가 없으면 Default 로 폴백한다 (설계 5.5).
		private static NpcInteractionResult fill(NpcInteractionResult result, Table_Npc.Row npc,
			DialogTriggerType trigger, int questId)
		{
			string text = QuestCatalog.GetDialog(npc.ID, questId, trigger);
			if (string.IsNullOrEmpty(text) == true && trigger != DialogTriggerType.Default)
			{
				trigger = DialogTriggerType.Default;
				questId = 0;
				text = QuestCatalog.GetDialog(npc.ID, 0, DialogTriggerType.Default);
			}

			result.trigger = trigger;
			result.questId = questId;
			result.lines = splitLines(text);

			// 기능은 퀘스트가 걸려 있지 않을 때만 연다 — 퀘스트 대화가 상점보다 우선한다.
			if (trigger == DialogTriggerType.Default)
			{
				result.function = npc.NpcType;
				result.functionId = npc.FunctionID;
			}

			return result;
		}

		private static string[] splitLines(string text)
		{
			if (string.IsNullOrEmpty(text) == true)
			{
				return _emptyLines;
			}

			return text.Split(_lineSeparator, System.StringSplitOptions.RemoveEmptyEntries);
		}
	}
}
