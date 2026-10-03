using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 던전 입장·소탕 — 입장 횟수와 런(시드)을 서버가 소유한다. 정산은 Dungeon.DungeonClear 가 한다.
	public class DungeonRun
	{
		// DungeonEnter — 해금·남은 횟수를 확인해 1회 차감하고 새 런을 발급한다.
		// 이전 런이 정산되지 않았으면(크래시·강제 종료) 그대로 덮는다 — 그 런의 상자 보상은 유실된다(허용).
		public Stream DungeonEnter()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				DungeonEnterRequest req = JsonConvert.DeserializeObject<DungeonEnterRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				EDT.Dungeon type = (EDT.Dungeon)req.dungeonType;
				if (Table_Dungeon.Get(type) == null || type == EDT.Dungeon.None)
				{
					return FuncResult.Error("invalid dungeonType: " + req.dungeonType);
				}

				if (MyData.Load("USER_DUNGEON", out DungeonProgressDto progress, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				DungeonEntryDto entry = DungeonProgressOps.GetEntry(progress, type);
				if (DungeonRules.CanEnterStage(type, entry.highestStage, req.stage) == false)
				{
					return FuncResult.Error("stage locked: " + type + " " + req.stage);
				}

				if (DungeonProgressOps.TryConsumeEnter(entry, type) == false)
				{
					return FuncResult.Error("no enter count: " + type);
				}

				progress.runCounter++;
				DungeonRunDto run = new DungeonRunDto();
				run.runId = progress.runCounter;
				run.seed = DungeonProgressOps.NewSeed();
				run.dungeonType = req.dungeonType;
				run.stage = req.stage;
				run.startUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				progress.run = run;

				var updateResult = Backend.GameData.Update("USER_DUNGEON", new Where(), DungeonProgressOps.ToParam(progress));
				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("USER_DUNGEON Update Failed: " + updateResult.GetErrorCode());
				}

				DungeonEnterResponse response = new DungeonEnterResponse();
				response.success = true;
				response.run = run;
				response.entry = entry;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// DungeonSweep — 균열만. 1웨이브라도 넘긴 기록이 있어야 하고, 입장 1회를 쓰고 입장보상(1 ~ 체크포인트)을 받는다.
		public Stream DungeonSweep()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				DungeonSweepRequest req = JsonConvert.DeserializeObject<DungeonSweepRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if ((EDT.Dungeon)req.dungeonType != EDT.Dungeon.Rift)
				{
					return FuncResult.Error("unsupported sweep: " + req.dungeonType);
				}

				if (MyData.Load("USER_DUNGEON", out DungeonProgressDto progress, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				DungeonEntryDto entry = DungeonProgressOps.GetEntry(progress, EDT.Dungeon.Rift);
				if (entry.highestStage < 1)
				{
					return FuncResult.Error("no rift record");
				}

				if (DungeonProgressOps.TryConsumeEnter(entry, EDT.Dungeon.Rift) == false)
				{
					return FuncResult.Error("no enter count: Rift");
				}

				RewardApplier applier = new RewardApplier(new InventoryDto(), currency);
				DungeonProgressOps.ApplyCurrency(applier, DungeonRules.GetRiftRewardCurrency(), DungeonRules.GetRiftSweepReward(entry.highestStage));

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_DUNGEON", new Where(), DungeonProgressOps.ToParam(progress)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), DungeonProgressOps.ToParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				DungeonSweepResponse response = new DungeonSweepResponse();
				response.success = true;
				response.rewards = applier.Granted;
				response.entry = entry;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// DungeonRevive — 진행 중인 런의 유료 부활. 상한(MaxRevivalCount)과 회차별 비용을 서버가 판정해 즉시 차감한다.
		// 부활 횟수는 런 단위다 — 다음 단계·재도전은 DungeonEnter 가 새 런을 주므로 0 부터 다시 센다.
		public Stream DungeonRevive()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				DungeonReviveRequest req = JsonConvert.DeserializeObject<DungeonReviveRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_DUNGEON", out DungeonProgressDto progress, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				DungeonRunDto run = progress.run;
				if (run == null || run.runId != req.runId || run.settled == true)
				{
					return FuncResult.Error("no active run: " + req.runId);
				}

				Table_Dungeon.Row dungeon = Table_Dungeon.Get((EDT.Dungeon)run.dungeonType);
				if (dungeon == null)
				{
					return FuncResult.Error("invalid dungeonType: " + run.dungeonType);
				}

				if (run.reviveCount >= dungeon.MaxRevivalCount)
				{
					return FuncResult.Error("no revive left: " + run.reviveCount);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				CurrencyCost cost = new CurrencyCost();
				cost.currency = dungeon.RevivalCostType;
				cost.amount = DungeonRules.GetRevivalCost(dungeon, run.reviveCount + 1);
				List<CurrencyCost> costs = new List<CurrencyCost>();
				costs.Add(cost);

				if (CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency: " + cost.currency);
				}

				run.reviveCount++;

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_DUNGEON", new Where(), DungeonProgressOps.ToParam(progress)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), DungeonProgressOps.ToParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				DungeonReviveResponse response = new DungeonReviveResponse();
				response.success = true;
				response.spent = new CurrencyAmountDto();
				response.spent.currencyId = (int)cost.currency;
				response.spent.amount = cost.amount;
				response.reviveCount = run.reviveCount;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}
	}

	// 던전 진행도 DTO 공용 조작 — 입장·소탕·정산이 함께 쓴다.
	public static class DungeonProgressOps
	{
		// 그 던전의 진행도. 없으면 만들고, 오늘(서버 시간 기준 KST 06:00 경계)이 아니면 사용 횟수를 0 으로 돌린다.
		public static DungeonEntryDto GetEntry(DungeonProgressDto progress, EDT.Dungeon type)
		{
			int today = ResetDay.FromUtc(DateTime.UtcNow);

			DungeonEntryDto entry = null;
			for (int i = 0; i < progress.entries.Count; i++)
			{
				if (progress.entries[i] != null && progress.entries[i].dungeonType == (int)type)
				{
					entry = progress.entries[i];
					break;
				}
			}

			if (entry == null)
			{
				entry = new DungeonEntryDto();
				entry.dungeonType = (int)type;
				entry.resetDay = today;
				progress.entries.Add(entry);
			}

			if (entry.resetDay != today)
			{
				entry.usedToday = 0;
				entry.resetDay = today;
			}

			return entry;
		}

		// 입장 1회 소모. 상한은 테이블 기본 횟수다(확장 시스템이 생기면 entry 에 확장분을 더한다).
		public static bool TryConsumeEnter(DungeonEntryDto entry, EDT.Dungeon type)
		{
			if (entry.usedToday >= DungeonRules.GetDefaultEnterCount(type))
			{
				return false;
			}

			entry.usedToday++;
			return true;
		}

		public static void MarkStageCleared(DungeonEntryDto entry, int stage)
		{
			if (stage > entry.highestStage)
			{
				entry.highestStage = stage;
			}
		}

		public static void ApplyCurrency(RewardApplier applier, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			RolledReward reward = default(RolledReward);
			reward.type = RewardType.Currency;
			reward.currency = currency;
			reward.count = amount;
			applier.Apply(reward, 0);
		}

		// 런 시드 — 예측 불가(암호학적 난수). 0 은 클라에서 "런 없음" 으로 쓴다.
		public static long NewSeed()
		{
			long seed = BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8), 0);
			return (seed == 0) ? 1 : seed;
		}

		public static Param ToParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
