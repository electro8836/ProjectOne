using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 필드보스 처치 — 처치 즉시 서버가 하루 1회 제한을 확인하고 경험치·보상을 지급한다.
	//
	// 필드보스는 MonsterSpawn.RespawnType = DailyReset 행으로 배치된 Boss 몬스터다. 잡몹 배치 정산(FieldSettle)에서는 거절되고
	// 이 경로로만 보상을 받는다. 처치 기록은 USER_FIELD.bossKills 에 (필드, 스폰 행, 갱신일) 로 남는다.
	//
	// 한계 — 서버는 어느 필드에 그 스폰 그룹이 배치됐는지 모른다(맵 프리팹). fieldId 는 실존만 확인한다.
	public class FieldBoss
	{
		// 보너스 퍼밀 상한 — FieldSettlement 와 같은 값.
		private const int MaxBonusPermille = 1000;

		public Stream FieldBossKill()
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

				FieldBossKillRequest req = JsonConvert.DeserializeObject<FieldBossKillRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 1. 보스 검증 — Boss 몬스터, 그 몬스터의 DailyReset 스폰 행, 행의 레벨, 실존하는 필드
				Table_Monster.Row monster = Table_Monster.Get(req.monsterId);
				Table_MonsterSpawn.Row spawn = Table_MonsterSpawn.Get(req.spawnId);
				if (monster == null || monster.MonsterType != MonsterType.Boss
					|| spawn == null || spawn.MonsterID != req.monsterId || spawn.RespawnType != RespawnType.DailyReset)
				{
					return FuncResult.Error("not a field boss: monster " + req.monsterId + " spawn " + req.spawnId);
				}

				if (req.level != spawn.Level)
				{
					return FuncResult.Error("invalid level " + req.level);
				}

				if (Table_Field.Get(req.fieldId) == null)
				{
					return FuncResult.Error("invalid field " + req.fieldId);
				}

				if (req.expBonusPermille < 0 || req.expBonusPermille > MaxBonusPermille
					|| req.goldBonusPermille < 0 || req.goldBonusPermille > MaxBonusPermille)
				{
					return FuncResult.Error("bonus out of range");
				}

				// 2. 하루 1회
				if (MyData.Load("USER_FIELD", out FieldSessionDto field, out string fieldErr) == false)
				{
					return FuncResult.Error(fieldErr);
				}

				int today = ResetDay.FromUtc(DateTime.UtcNow);
				FieldBossKillDto kill = findKill(field, req.fieldId, req.spawnId);
				if (kill != null && kill.resetDay == today)
				{
					return FuncResult.Error("already killed today: field " + req.fieldId + " spawn " + req.spawnId);
				}

				if (kill == null)
				{
					kill = new FieldBossKillDto();
					kill.fieldId = req.fieldId;
					kill.spawnId = req.spawnId;
					field.bossKills.Add(kill);
				}

				kill.resetDay = today;

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

				if (HeroPassOps.Load(out HeroPassDto heroPass, out string heroPassErr) == false)
				{
					return FuncResult.Error(heroPassErr);
				}

				// 보스 보상에는 수집품(펫)이 섞일 수 있다.
				if (MyData.Load("USER_PET", out PetDto pet, out string petErr) == false)
				{
					return FuncResult.Error(petErr);
				}

				if (MyData.Load("USER_COSTUME", out CostumeDto costume, out string costumeErr) == false)
				{
					return FuncResult.Error(costumeErr);
				}

				// 3. 경험치 — 캐릭터와 장착 무기 마스터리에 같은 값(마스터리 설계 5.2). 무기가 없으면 마스터리 적립만 건너뛴다.
				int levelKeyBefore = RankOps.LevelKey(loadout, mastery);
				int exp = MonsterExp.Calc(req.monsterId, req.level, req.expBonusPermille);
				loadout.exp += exp;
				loadout.level = MasteryRules.CharacterLevelFromExp(loadout.exp);
				if (exp > 0 && MasteryFunctions.OwnsWeaponFor(inventory, req.masteryId) == true)
				{
					MasteryRules.AddExp(mastery, (WeaponMastery)req.masteryId, exp);
				}

				// 4. 보상 — 잡몹과 같은 순서(고유 드랍 → 지역 드랍). 적 처치분이라 골드 보너스를 받는다.
				List<RolledReward> rolled = new List<RolledReward>();
				ServerRandomSource rng = new ServerRandomSource();
				RewardRoller.Roll(monster.RewardGroupID, req.goldBonusPermille, rng, rolled, null);
				RewardRoller.Roll(spawn.RewardGroupID, req.goldBonusPermille, rng, rolled, null);

				RewardApplier applier = new RewardApplier(inventory, currency, pet, costume);
				applier.AutoDecompose = true;
				applier.ApplyAll(rolled);

				// 히어로패스 활동 — 필드보스는 배치에서 빠지므로 여기서 센다.
				HeroPassOps.CountKill(heroPass, req.monsterId);

				// 5. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_FIELD", new Where(), DungeonProgressOps.ToParam(field)));
				tx.Add(TransactionValue.SetUpdate("USER_LOADOUT", new Where(), DungeonProgressOps.ToParam(loadout)));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), DungeonProgressOps.ToParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), DungeonProgressOps.ToParam(currency)));
				tx.Add(TransactionValue.SetUpdate("USER_MASTERY", new Where(), DungeonProgressOps.ToParam(mastery)));
				tx.Add(HeroPassOps.ToUpdate(heroPass));

				if (applier.PetChanged == true)
				{
					tx.Add(TransactionValue.SetUpdate("USER_PET", new Where(), DungeonProgressOps.ToParam(pet)));
				}

				if (applier.CostumeChanged == true)
				{
					tx.Add(TransactionValue.SetUpdate("USER_COSTUME", new Where(), DungeonProgressOps.ToParam(costume)));
				}

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				// 레벨이 올랐거나 펫을 얻었으면 전투력이 바뀐다.
				if (applier.PetChanged == true || RankOps.LevelKey(loadout, mastery) != levelKeyBefore)
				{
					RankOps.Refresh(loadout, inventory, mastery, pet, costume);
				}

				FieldBossKillResponse response = new FieldBossKillResponse();
				response.success = true;
				response.exp = loadout.exp;
				response.rewards = applier.Granted;
				response.equipments = applier.Equipments;
				response.kill = kill;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		private static FieldBossKillDto findKill(FieldSessionDto field, int fieldId, int spawnId)
		{
			for (int i = 0; i < field.bossKills.Count; i++)
			{
				FieldBossKillDto kill = field.bossKills[i];
				if (kill != null && kill.fieldId == fieldId && kill.spawnId == spawnId)
				{
					return kill;
				}
			}

			return null;
		}
	}
}
