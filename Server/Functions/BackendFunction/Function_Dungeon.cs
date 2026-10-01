using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 던전 클리어 — 서버 권위로 경험치와 보상을 지급한다.
	//
	// 클라는 (dungeonType, stage, cleared) 만 보낸다. 경험치·보상 그룹은 서버가 동봉된 테이블에서 찾고,
	// 추첨은 클라와 공유하는 RewardRoller 로 굴린다(같은 테이블·같은 규칙).
	//
	// 지금은 골드 던전만 서버가 정산한다. 균열·미궁·유적은 클라 로컬 정산이다.
	// TODO — 단계 해금·입장 횟수 검증 없음. DungeonProgress 가 서버에 저장되면 여기서 검증한다.
	public class Dungeon
	{
		public Stream DungeonClear()
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

				DungeonClearRequest req = JsonConvert.DeserializeObject<DungeonClearRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 실패(사망→귀환 등) — 지급 없이 성공 빈 응답. 클라는 실패 경로에서 응답을 반영하지 않는다.
				if (req.cleared == false)
				{
					DungeonClearResponse failLog = new DungeonClearResponse();
					failLog.success = true;
					failLog.rewards = new GrantedRewardDto[0];
					failLog.equipments = new EquipmentInstanceDto[0];
					return FuncResult.Json(failLog);
				}

				if ((EDT.Dungeon)req.dungeonType != EDT.Dungeon.Gold)
				{
					return FuncResult.Error("unsupported dungeonType: " + req.dungeonType);
				}

				Table_GoldDungeon.Row stage = findGoldStage(req.stage);
				if (stage == null)
				{
					return FuncResult.Error("invalid stage: " + req.stage);
				}

				// 1. 유저 데이터 로드
				if (MyData.Load("USER_LOADOUT", out LoadoutDto loadout, out string loadoutErr) == false)
				{
					return FuncResult.Error(loadoutErr);
				}

				if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				if (MyData.Load("USER_MASTERY", out MasteryDto mastery, out string masteryErr) == false)
				{
					return FuncResult.Error(masteryErr);
				}

				// 2. 경험치 — 누적값에 더한다(레벨은 클라가 누적 경험치에서 파생). 장착 무기 마스터리에도 같은 값(마스터리 설계 5.2).
				//    마스터리는 그 무기를 실제로 가졌을 때만 적립한다 — 못 찾으면 클리어는 그대로 두고 적립만 건너뛴다.
				loadout.exp += stage.RewardExp;
				loadout.level = MasteryRules.CharacterLevelFromExp(loadout.exp);
				if (MasteryFunctions.OwnsWeaponFor(inventory, req.masteryId) == true)
				{
					MasteryRules.AddExp(mastery, (WeaponMastery)req.masteryId, stage.RewardExp);
				}

				// 3. 보상 추첨 → 반영. 던전 클리어 보상은 골드 보너스를 받지 않는다(보너스 0‰).
				List<RolledReward> rolled = new List<RolledReward>();
				RewardRoller.Roll(stage.RewardGroupID, 0, new ServerRandomSource(), rolled, null);

				RewardApplier applier = new RewardApplier(inventory, currency);
				applier.ApplyAll(rolled);

				// 4. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_LOADOUT", new Where(), toParam(loadout)));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));
				tx.Add(TransactionValue.SetUpdate("USER_MASTERY", new Where(), toParam(mastery)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = loadout.exp;
				response.rewards = applier.Granted;
				response.equipments = applier.Equipments;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		private static Table_GoldDungeon.Row findGoldStage(int stage)
		{
			Dictionary<int, Table_GoldDungeon.Row> all = Table_GoldDungeon.All();
			Dictionary<int, Table_GoldDungeon.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.Stage == stage)
				{
					return e.Current.Value;
				}
			}

			return null;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
