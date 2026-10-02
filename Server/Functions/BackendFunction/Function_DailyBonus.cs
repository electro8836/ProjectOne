using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 출석 — 오늘 몫 수령. 하루 경계는 서버 시간(ResetDay, KST 06시) 기준이다.
	//
	// 클라 DailyBonusBook 과 같은 규칙: 종류마다 "다음에 받을 일차" 와 "마지막 수령일" 을 들고,
	// 수령하면 일차가 주기 끝에서 1로 돌아온다. 보상은 서버가 공유 코어(RewardRoller)로 굴린다.
	public class DailyBonusFunctions
	{
		public Stream DailyBonusClaim()
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

				DailyBonusClaimRequest req = JsonConvert.DeserializeObject<DailyBonusClaimRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_DAILYBONUS", out DailyBonusDto daily, out string dailyErr) == false)
				{
					return FuncResult.Error(dailyErr);
				}

				DailyBonusType type = (DailyBonusType)req.typeId;
				DailyBonusProgressDto entry = getOrCreate(daily, req.typeId);

				int today = ResetDay.FromUtc(DateTime.UtcNow);
				if (entry.lastClaimedDay == today)
				{
					return FuncResult.Error("already claimed today: " + type);
				}

				if (entry.dayCount != req.dayCount)
				{
					return FuncResult.Error("day mismatch: " + req.dayCount + " (server " + entry.dayCount + ")");
				}

				int cycleLength = DailyBonusRules.GetCycleLength(type);
				Table_DailyBonus.Row row = DailyBonusRules.GetDay(type, entry.dayCount);
				if (cycleLength <= 0 || row == null)
				{
					return FuncResult.Error("no daily bonus row: " + type + " day " + entry.dayCount);
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				// 출석 보상은 고정값이다 — 보너스를 곱하지 않는다(0‰).
				RewardApplier applier = new RewardApplier(inventory, currency);
				if (row.RewardGroupID > 0)
				{
					List<RolledReward> rolled = new List<RolledReward>();
					RewardRoller.Roll(row.RewardGroupID, 0, new ServerRandomSource(), rolled, null);
					applier.ApplyAll(rolled);
				}

				entry.lastClaimedDay = today;
				entry.dayCount = DailyBonusRules.NextDayCount(entry.dayCount, cycleLength);

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_DAILYBONUS", new Where(), toParam(daily)));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				DailyBonusClaimResponse response = new DailyBonusClaimResponse();
				response.success = true;
				response.rewards = applier.Granted;
				response.equipments = applier.Equipments;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 처음 받는 종류는 1일차부터다(클라 DailyBonusBook.getOrCreate 와 같은 기본값).
		private static DailyBonusProgressDto getOrCreate(DailyBonusDto daily, int typeId)
		{
			for (int i = 0; i < daily.progress.Count; i++)
			{
				DailyBonusProgressDto progress = daily.progress[i];
				if (progress != null && progress.typeId == typeId)
				{
					if (progress.dayCount <= 0)
					{
						progress.dayCount = 1;
					}

					return progress;
				}
			}

			DailyBonusProgressDto created = new DailyBonusProgressDto();
			created.typeId = typeId;
			created.dayCount = 1;
			daily.progress.Add(created);
			return created;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
