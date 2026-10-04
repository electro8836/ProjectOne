using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 던전 런 종료 정산 — 서버 권위로 클리어 보상·상자 보상·최고 단계를 저장한다.
	//
	// 클라는 (런, 던전, 단계, 클리어 여부, 균열 웨이브, 연 상자) 만 보낸다. 경험치·보상 그룹은 서버가 동봉된 테이블에서 찾고,
	// 추첨은 클라와 공유하는 RewardRoller 로 굴린다(같은 테이블·같은 규칙).
	// 미궁 상자는 런 시드로 굴린다 — 클라가 바닥에 떨군 것 중 주운 것(pickedMask)만 같은 값으로 재현해 저장한다.
	// 실패·강제 귀환도 정산한다 — 주운 상자 보상은 결과와 무관하게 남는다.
	// 처치 드랍·경험치는 필드 배치 정산(FieldSettle)이 따로 한다 — 클라가 이 요청 직전에 배치를 먼저 보낸다.
	public class Dungeon
	{
		// 균열 웨이브 최소 시간 검증의 여유 — 프레임 지연·시간 오차를 흡수한다.
		private const double RiftTimeTolerance = 1.1;
		private const double RiftTimeSlackSeconds = 5.0;

		// pickedMask(long) 의 비트 수 — 상자 1개에서 추적할 수 있는 보상 수 상한.
		private const int MaxTrackedRewards = 64;

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

				// 1. 유저 데이터 로드
				if (MyData.Load("USER_DUNGEON", out DungeonProgressDto progress, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				// 런 확인 — 입장으로 발급된 그 런이어야 하고, 한 번만 정산된다.
				DungeonRunDto run = progress.run;
				if (run == null || run.settled == true || run.runId != req.runId
					|| run.dungeonType != req.dungeonType || run.stage != req.stage)
				{
					return FuncResult.Error("stale run: " + req.runId);
				}

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

				int levelKeyBefore = RankOps.LevelKey(loadout, mastery);

				EDT.Dungeon type = (EDT.Dungeon)req.dungeonType;
				DungeonEntryDto entry = DungeonProgressOps.GetEntry(progress, type);
				run.settled = true;

				// 2. 던전별 정산 — 클리어 보상은 clearApplier(응답에 싣는다), 상자는 chestApplier(클라가 이미 지급)
				RewardApplier clearApplier = new RewardApplier(inventory, currency);
				RewardApplier chestApplier = new RewardApplier(inventory, currency);

				string invalid;
				switch (type)
				{
					case EDT.Dungeon.Gold:
						invalid = settleGold(req, entry, loadout, inventory, mastery, clearApplier);
						break;
					case EDT.Dungeon.Rift:
						invalid = settleRift(req, run, entry, clearApplier);
						break;
					case EDT.Dungeon.Labyrinth:
						invalid = settleLabyrinth(req, run, entry, clearApplier, chestApplier);
						break;
					case EDT.Dungeon.Ruins:
						invalid = settleRuins(req, run, entry, clearApplier);
						break;
					default:
						invalid = "unsupported dungeonType: " + req.dungeonType;
						break;
				}

				// 검증 위반 — 아무것도 지급하지 않고 런만 닫는다. 클라 로컬 반영분은 다음 로그인에 서버값으로 정리된다.
				if (invalid != null)
				{
					var closeResult = Backend.GameData.Update("USER_DUNGEON", new Where(), DungeonProgressOps.ToParam(progress));
					if (!closeResult.IsSuccess())
					{
						return FuncResult.Error("USER_DUNGEON Update Failed: " + closeResult.GetErrorCode());
					}

					return FuncResult.Error("rejected: " + invalid);
				}

				// 히어로패스 활동 — 정상 정산된 클리어만 센다(클라 DungeonStageClearedEvent 와 같은 시점).
				if (req.cleared == true)
				{
					HeroPassOps.CountDungeonClear(heroPass);
				}

				// 3. 원자 저장
				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_DUNGEON", new Where(), DungeonProgressOps.ToParam(progress)));
				tx.Add(TransactionValue.SetUpdate("USER_LOADOUT", new Where(), DungeonProgressOps.ToParam(loadout)));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), DungeonProgressOps.ToParam(inventory)));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), DungeonProgressOps.ToParam(currency)));
				tx.Add(TransactionValue.SetUpdate("USER_MASTERY", new Where(), DungeonProgressOps.ToParam(mastery)));
				tx.Add(HeroPassOps.ToUpdate(heroPass));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				if (RankOps.LevelKey(loadout, mastery) != levelKeyBefore)
				{
					RankOps.Refresh(loadout, inventory, mastery, null, null);
				}

				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = loadout.exp;
				response.rewards = clearApplier.Granted;
				response.equipments = clearApplier.Equipments;
				response.entry = entry;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 던전별 ────────────────────────────────────────────────────

		// 골드 — 클리어 경험치(장착 무기 마스터리에도 같은 값, 마스터리 설계 5.2) + 보상 그룹. 상자 없음.
		// 마스터리는 그 무기를 실제로 가졌을 때만 적립한다 — 못 찾으면 클리어는 그대로 두고 적립만 건너뛴다.
		private static string settleGold(DungeonClearRequest req, DungeonEntryDto entry, LoadoutDto loadout,
			InventoryDto inventory, MasteryDto mastery, RewardApplier clearApplier)
		{
			if (req.cleared == false)
			{
				return null;
			}

			Table_GoldDungeon.Row stage = DungeonRules.FindGoldStage(req.stage);
			if (stage == null)
			{
				return "invalid stage: " + req.stage;
			}

			loadout.exp += stage.RewardExp;
			loadout.level = MasteryRules.CharacterLevelFromExp(loadout.exp);
			if (MasteryFunctions.OwnsWeaponFor(inventory, req.masteryId) == true)
			{
				MasteryRules.AddExp(mastery, (WeaponMastery)req.masteryId, stage.RewardExp);
			}

			// 던전 클리어 보상은 골드 보너스를 받지 않는다(보너스 0‰).
			List<RolledReward> rolled = new List<RolledReward>();
			RewardRoller.Roll(stage.RewardGroupID, 0, new ServerRandomSource(), rolled, null);
			clearApplier.ApplyAll(rolled);

			DungeonProgressOps.MarkStageCleared(entry, req.stage);
			return null;
		}

		// 균열 — 한 판 보상(입장보상 + 통과 웨이브) 재화 1종. 웨이브에 끝이 없어 클리어 웨이브를 경과 시간으로 검증한다 —
		// 웨이브마다 마리 수 × 스폰 간격보다 빨리 끝날 수 없다(DungeonRules.GetRiftWaveMinSeconds).
		private static string settleRift(DungeonClearRequest req, DungeonRunDto run, DungeonEntryDto entry, RewardApplier clearApplier)
		{
			if (req.cleared == false)
			{
				return null;
			}

			int minWave = run.stage - 1;
			if (req.clearedWave < minWave)
			{
				return "invalid clearedWave " + req.clearedWave + " < " + minWave;
			}

			double elapsed = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - run.startUnixMs) / 1000.0;
			double allowed = Math.Max(elapsed, 0.0) * RiftTimeTolerance + RiftTimeSlackSeconds;
			double required = 0.0;
			for (int wave = run.stage; wave <= req.clearedWave; wave++)
			{
				required += DungeonRules.GetRiftWaveMinSeconds(wave);
				if (required > allowed)
				{
					return "clearedWave " + req.clearedWave + " too fast: wave " + wave + " needs " + required + "s > " + allowed + "s";
				}
			}

			int reward = DungeonRules.GetRiftRunReward(run.stage, req.clearedWave);
			DungeonProgressOps.ApplyCurrency(clearApplier, DungeonRules.GetRiftRewardCurrency(), reward);

			if (req.clearedWave > 0)
			{
				DungeonProgressOps.MarkStageCleared(entry, req.clearedWave);
			}

			return null;
		}

		// 미궁 — 주운 상자 드랍(등급별 그룹, 런 시드) + 클리어 보상. 상자 등급은 맵 배치가 정해 서버가 알 수 없다 — 개수만 제한한다.
		private static string settleLabyrinth(DungeonClearRequest req, DungeonRunDto run, DungeonEntryDto entry,
			RewardApplier clearApplier, RewardApplier chestApplier)
		{
			Table_LabyrinthDungeon.Row row = DungeonRules.FindLabyrinthStage(run.stage);
			if (row == null)
			{
				return "invalid stage: " + run.stage;
			}

			string invalid = validateChests(req.chests, row.ChestCount);
			if (invalid != null)
			{
				return invalid;
			}

			if (req.chests != null)
			{
				for (int i = 0; i < req.chests.Length; i++)
				{
					DungeonChestDto chest = req.chests[i];
					int group = DungeonRules.GetLabyrinthChestGroup(row, (DungeonChestGrade)chest.grade);
					invalid = rollChest(run, chest, group, chestApplier);
					if (invalid != null)
					{
						return invalid;
					}
				}
			}

			if (req.cleared == true)
			{
				List<RolledReward> rolled = new List<RolledReward>();
				RewardRoller.Roll(row.ClearRewardGroupID, 0, new ServerRandomSource(), rolled, null);
				clearApplier.ApplyAll(rolled);

				DungeonProgressOps.MarkStageCleared(entry, run.stage);
			}

			return null;
		}

		// 유적 — 보스(코어) 처치가 곧 클리어다. 클리어 보상만 있고 상자는 없다.
		private static string settleRuins(DungeonClearRequest req, DungeonRunDto run, DungeonEntryDto entry, RewardApplier clearApplier)
		{
			Table_RuinsDungeon.Row row = DungeonRules.FindRuinsStage(run.stage);
			if (row == null)
			{
				return "invalid stage: " + run.stage;
			}

			if (req.chests != null && req.chests.Length > 0)
			{
				return "ruins has no chest";
			}

			if (req.cleared == true)
			{
				List<RolledReward> rolled = new List<RolledReward>();
				RewardRoller.Roll(row.ClearRewardGroupID, 0, new ServerRandomSource(), rolled, null);
				clearApplier.ApplyAll(rolled);

				DungeonProgressOps.MarkStageCleared(entry, run.stage);
			}

			return null;
		}

		// ── 상자 ──────────────────────────────────────────────────────

		// 개수 ≤ 상자 수, 인덱스 0 ~ 상자 수-1, 중복 없음.
		private static string validateChests(DungeonChestDto[] chests, int chestCount)
		{
			if (chests == null)
			{
				return null;
			}

			if (chests.Length > chestCount)
			{
				return "too many chests: " + chests.Length + " > " + chestCount;
			}

			HashSet<int> seen = new HashSet<int>();
			for (int i = 0; i < chests.Length; i++)
			{
				if (chests[i] == null || chests[i].chestIndex < 0 || chests[i].chestIndex >= chestCount
					|| seen.Add(chests[i].chestIndex) == false)
				{
					return "invalid chest index";
				}
			}

			return null;
		}

		// 클라(DungeonRunLedger.OpenChest)와 같은 시드·같은 순서로 굴리고 주운 것(pickedMask)만 지급한다.
		// 장비 UID 도 상자 좌표로 같은 값을 쓴다. 존재하지 않는 보상 인덱스를 주웠다고 하면 조작이다.
		private static string rollChest(DungeonRunDto run, DungeonChestDto chest, int groupId, RewardApplier applier)
		{
			List<RolledReward> rolled = new List<RolledReward>();
			DeterministicRandom rng = new DeterministicRandom(DungeonRules.ChestSeed(run.seed, chest.chestIndex, groupId));
			RewardRoller.Roll(groupId, 0, rng, rolled, null);

			int tracked = Math.Min(rolled.Count, MaxTrackedRewards);
			long validBits = (tracked >= MaxTrackedRewards) ? -1L : (1L << tracked) - 1;
			if ((chest.pickedMask & ~validBits) != 0)
			{
				return "invalid pickedMask at chest " + chest.chestIndex;
			}

			for (int r = 0; r < tracked; r++)
			{
				if (((chest.pickedMask >> r) & 1L) == 0)
				{
					continue;
				}

				long uid = rolled[r].isEquipment ? EquipmentUid.ForDungeonChest(run.runId, chest.chestIndex, r) : 0;
				applier.Apply(rolled[r], uid);
			}

			return null;
		}
	}
}
