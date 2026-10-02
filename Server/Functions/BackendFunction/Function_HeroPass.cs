using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 히어로패스 진행도 공용 조작 — 처치·던전 클리어 정산 경로(FieldSettle·FieldBossKill·DungeonClear)가 같이 쓴다.
	// 규칙은 클라와 같은 HeroPassRules 다. 시즌은 매번 EnsureSeason 으로 서버 시간 기준 맞춘다.
	public static class HeroPassOps
	{
		public static bool Load(out HeroPassDto dto, out string err)
		{
			if (MyData.Load("USER_HEROPASS", out dto, out err) == false)
			{
				return false;
			}

			HeroPassRules.EnsureSeason(dto, Today());
			return true;
		}

		// 처치 몬스터 1마리 — 등급(Normal/Elite/Boss)별 활동으로 센다.
		public static void CountKill(HeroPassDto dto, int monsterId)
		{
			Table_Monster.Row monster = Table_Monster.Get(monsterId);
			if (monster == null)
			{
				return;
			}

			HeroPassRules.AddCount(dto, HeroPassRules.ToKillType(monster.MonsterType));
		}

		public static void CountDungeonClear(HeroPassDto dto)
		{
			HeroPassRules.AddCount(dto, HeroPassExpType.DungeonClear);
		}

		public static TransactionValue ToUpdate(HeroPassDto dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return TransactionValue.SetUpdate("USER_HEROPASS", new Where(), param);
		}

		public static int Today()
		{
			return ResetDay.FromUtc(DateTime.UtcNow);
		}
	}

	// 히어로패스 레벨 보상 수령. 하나라도 받을 수 없는 항목이 섞이면 전체를 거절한다.
	public class HeroPassFunctions
	{
		public Stream HeroPassClaim()
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

				HeroPassClaimRequest req = JsonConvert.DeserializeObject<HeroPassClaimRequest>(Backend.Content["req"].ToString());
				if (req == null || req.levels == null || req.advanced == null || req.levels.Length == 0 || req.levels.Length != req.advanced.Length)
				{
					return FuncResult.Error("req parse failed");
				}

				if (HeroPassOps.Load(out HeroPassDto pass, out string passErr) == false)
				{
					return FuncResult.Error(passErr);
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				IReadOnlyList<Table_HeroPass.Row> levels = HeroPassRules.GetLevels();
				RewardApplier applier = new RewardApplier(inventory, currency);
				List<RolledReward> rolled = new List<RolledReward>();
				ServerRandomSource random = new ServerRandomSource();

				for (int i = 0; i < req.levels.Length; i++)
				{
					int level = req.levels[i];
					bool advanced = req.advanced[i];

					// CanClaim 은 앞에서 수령 표시한 항목을 다시 거른다 — 같은 항목이 두 번 오면 여기서 막힌다.
					if (level > levels.Count || HeroPassRules.CanClaim(pass, level, advanced) == false)
					{
						return FuncResult.Error("cannot claim: level " + level + (advanced ? " advanced" : " normal") + " (exp " + pass.exp + ")");
					}

					Table_HeroPass.Row row = levels[level - 1];
					int groupId = advanced ? row.RewardGroupID_Advanced : row.RewardGroupID_Normal;
					if (groupId > 0)
					{
						// 패스 보상은 고정값이다 — 보너스를 곱하지 않는다(0‰).
						rolled.Clear();
						RewardRoller.Roll(groupId, 0, random, rolled, null);
						applier.ApplyAll(rolled);
					}

					HeroPassRules.MarkClaimed(pass, level, advanced);
				}

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(HeroPassOps.ToUpdate(pass));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				HeroPassClaimResponse response = new HeroPassClaimResponse();
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

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
