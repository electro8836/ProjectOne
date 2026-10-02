using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 퀘스트 — 완료(보상 지급 + 체인 전진), 처치 카운터 저장. 목표 해석은 클라와 같은 QuestRules 로 한다.
	//
	// 처치형 목표의 카운터는 클라 값을 신뢰한다(보상이 고정·소량이라 조작 이득이 작다 — 알려진 한계).
	// 던전 클리어는 USER_DUNGEON, 레벨은 USER_LOADOUT 저장값으로 직접 판정한다.
	public class QuestFunctions
	{
		public Stream QuestComplete()
		{
			try
			{
				if (begin(out QuestCompleteRequest req, out QuestDto quest, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				// 체인 순서 — 클리어한 마지막 퀘스트의 바로 다음만 완료할 수 있다.
				if (req.questId <= 0 || req.questId != QuestRules.GetNextQuestId(quest.clearedQuestId))
				{
					return FuncResult.Error("quest out of order: " + req.questId + " (cleared " + quest.clearedQuestId + ")");
				}

				Table_Quest.Row row = Table_Quest.Get(req.questId);
				if (row == null || QuestRules.TryParseTarget(row, out QuestTarget target) == false)
				{
					return FuncResult.Error("invalid quest: " + req.questId);
				}

				if (isObjectiveMet(target, req.counter, out string objErr) == false)
				{
					return FuncResult.Error(objErr);
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				// 퀘스트 보상은 고정값이다 — 보너스를 곱하지 않는다(0‰).
				RewardApplier applier = new RewardApplier(inventory, currency);
				if (row.RewardGroupID > 0)
				{
					List<RolledReward> rolled = new List<RolledReward>();
					RewardRoller.Roll(row.RewardGroupID, 0, new ServerRandomSource(), rolled, null);
					applier.ApplyAll(rolled);
				}

				// 체인 전진 — 다음 퀘스트를 바로 연다(클라 QuestBook.RefreshCurrent 와 같은 규칙).
				int nextQuestId = QuestRules.GetNextQuestId(req.questId);
				quest.clearedQuestId = req.questId;
				quest.current = new QuestProgressDto();
				quest.current.questId = nextQuestId;

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_QUEST", new Where(), toParam(quest)));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				QuestCompleteResponse response = new QuestCompleteResponse();
				response.success = true;
				response.rewards = applier.Granted;
				response.equipments = applier.Equipments;
				response.nextQuestId = nextQuestId;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 진행 중 퀘스트의 처치 카운터 저장 — 진행 중(없으면 다음) 퀘스트와 같을 때만, 목표치를 상한으로 저장한다.
		public Stream SaveQuestProgress()
		{
			try
			{
				if (begin(out SaveQuestProgressRequest req, out QuestDto quest, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				int expected = QuestRules.GetNextQuestId(quest.clearedQuestId);
				if (req.questId <= 0 || req.questId != expected)
				{
					return FuncResult.Error("quest mismatch: " + req.questId + " (expected " + expected + ")");
				}

				Table_Quest.Row row = Table_Quest.Get(req.questId);
				if (row == null || QuestRules.TryParseTarget(row, out QuestTarget target) == false || QuestRules.IsKillTarget(target.type) == false)
				{
					return FuncResult.Error("not a kill quest: " + req.questId);
				}

				int required = QuestRules.GetRequiredCount(target);
				int counter = req.counter;
				if (counter < 0)
				{
					counter = 0;
				}

				if (counter > required)
				{
					counter = required;
				}

				quest.current = new QuestProgressDto();
				quest.current.questId = req.questId;
				quest.current.counter = counter;

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_QUEST", new Where(), toParam(quest)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				SaveQuestProgressResponse response = new SaveQuestProgressResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 공용 ──────────────────────────────────────────────────────

		private static bool begin<T>(out T req, out QuestDto quest, out string err) where T : class
		{
			req = null;
			quest = null;

			if (GameData.EnsureLoaded(out err) == false)
			{
				return false;
			}

			if (Backend.HasKey("req") == false)
			{
				err = "req key is not exist";
				return false;
			}

			req = JsonConvert.DeserializeObject<T>(Backend.Content["req"].ToString());
			if (req == null)
			{
				err = "req parse failed";
				return false;
			}

			if (MyData.Load("USER_QUEST", out quest, out err) == false)
			{
				return false;
			}

			if (quest.current == null)
			{
				quest.current = new QuestProgressDto();
			}

			return true;
		}

		// 목표 달성 판정 — 처치형은 클라 카운터, 던전·레벨은 서버 저장값.
		private static bool isObjectiveMet(QuestTarget target, int counter, out string err)
		{
			err = null;

			if (QuestRules.IsKillTarget(target.type) == true)
			{
				if (counter < QuestRules.GetRequiredCount(target))
				{
					err = "kill objective not met: " + counter;
					return false;
				}

				return true;
			}

			if (target.type == QuestTargetType.DungeonClear)
			{
				if (MyData.Load("USER_DUNGEON", out DungeonProgressDto dungeon, out err) == false)
				{
					return false;
				}

				DungeonEntryDto entry = DungeonProgressOps.GetEntry(dungeon, target.dungeon);
				if (entry.highestStage < target.dungeonStage)
				{
					err = "dungeon objective not met: " + target.dungeon + " " + entry.highestStage + "/" + target.dungeonStage;
					return false;
				}

				return true;
			}

			if (target.type == QuestTargetType.ReachLevel)
			{
				if (MyData.Load("USER_LOADOUT", out LoadoutDto loadout, out err) == false)
				{
					return false;
				}

				if (loadout.level < target.reachLevel)
				{
					err = "level objective not met: " + loadout.level + "/" + target.reachLevel;
					return false;
				}

				return true;
			}

			err = "unsupported objective: " + target.type;
			return false;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
