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
	// 필드 처치 배치 정산 — 클라가 모아 보낸 처치를 같은 시드로 재현해 경험치와 "주운 보상"만 지급한다.
	//
	// 클라는 처치마다 KillSeed(세션 시드, killIndex, monsterId) 로 굴린 결과를 로컬에 먼저 반영하고,
	// 드랍 정리가 끝난 처치를 killIndex 순서로 보낸다. 서버는 결과를 믿지 않고 직접 다시 굴린다.
	//
	// 시드는 "그 처치라면 그 보상"만 보장한다 — 처치 자체가 진짜인지는 아래 상한 검증으로 막는다.
	public class FieldSettlement
	{
		// 처치 수 상한 — 이론 최대(그룹 1001: 8마리/5초 = 1.6킬/초)에 여유를 둔 값.
		// TODO — 필드별 스폰 데이터가 테이블로 오면 필드 기준으로 계산한다.
		private const double MaxKillsPerSecond = 3.0;
		private const double MaxWindowSeconds = 600.0;

		// 처치 시점과 정산 시점의 차이(드랍 수명 60초 + 전송 주기)를 흡수하는 여유.
		private const double SettleLagSeconds = 60.0;
		private const int KillBurst = 30;

		// 보너스 퍼밀 상한. 지금은 Exp/Gold 보너스 옵션 데이터가 없다.
		// TODO — 서버 스탯 계산이 생기면 장비·마스터리·펫 이론 최대치로 교체한다.
		private const int MaxBonusPermille = 1000;

		// 세션이 바뀌어(재로그인) 이 배치를 받을 수 없을 때 — 클라가 원장을 전부 비우게 한다.
		private const int DiscardAllIndex = int.MaxValue;

		// 검증용 정적 데이터 — 테이블 로드 후 한 번만 만든다.
		private static Dictionary<int, HashSet<int>> _spawnGroupsByMonster;
		private static HashSet<int> _fieldBossOnly;
		private static int _maxMonsterLevel;

		public Stream FieldSettle()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				buildValidationData();

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				FieldSettleRequest req = JsonConvert.DeserializeObject<FieldSettleRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_FIELD", out FieldSessionDto field, out string fieldErr) == false)
				{
					return FuncResult.Error(fieldErr);
				}

				// 세션 확인 — 맵 이동·재로그인으로 시드가 바뀌었으면 이 배치는 재현할 수 없다.
				if (req.sessionSeed != field.seed)
				{
					return reject("stale session", DiscardAllIndex);
				}

				SettleResult result = settle(field, req.kills);
				switch (result.status)
				{
					case SettleStatus.Error:
						return FuncResult.Error(result.message);

					case SettleStatus.Nothing:
						return succeed(field.nextKillIndex, -1);

					case SettleStatus.Discarded:
					{
						var updateResult = Backend.GameData.Update("USER_FIELD", new Where(), toParam(field));
						if (!updateResult.IsSuccess())
						{
							return FuncResult.Error("USER_FIELD update failed: " + updateResult.GetErrorCode());
						}

						return reject("rejected: " + result.message, field.nextKillIndex);
					}
				}

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_FIELD", new Where(), toParam(field)));
				tx.AddRange(result.dataTx);

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				return succeed(field.nextKillIndex, result.exp);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// FieldSessionRotate — 로딩(맵 이동) 시점. 남은 처치를 정산하고 새 시드를 발급한다.
		//
		// 로딩 중에는 처치가 없고 바닥 드랍도 모두 정리되므로, 여기서 끊으면 옛 시드와 새 시드가 섞이지 않는다.
		// 정산이 폐기돼도(조작 의심) 교체는 진행한다 — 세션을 바꾸는 것이 이 호출의 본래 목적이다.
		public Stream FieldSessionRotate()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				buildValidationData();

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				FieldRotateRequest req = JsonConvert.DeserializeObject<FieldRotateRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_FIELD", out FieldSessionDto field, out string fieldErr) == false)
				{
					return FuncResult.Error(fieldErr);
				}

				// 시드가 다르면(다른 기기 재로그인 등) 이 처치들은 재현할 수 없다 — 정산 없이 교체만 한다.
				SettleResult result = SettleResult.NothingToDo;
				if (req.sessionSeed == field.seed)
				{
					result = settle(field, req.kills);
					if (result.status == SettleStatus.Error)
					{
						return FuncResult.Error(result.message);
					}
				}

				FieldSessions.Renew(field);

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_FIELD", new Where(), toParam(field)));
				if (result.status == SettleStatus.Settled)
				{
					tx.AddRange(result.dataTx);
				}

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				FieldRotateResponse response = new FieldRotateResponse();
				response.success = true;
				response.error = (result.status == SettleStatus.Discarded) ? "rejected: " + result.message : null;
				response.exp = result.exp;
				response.field = field;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 정산 핵심 ─────────────────────────────────────────────────

		private enum SettleStatus
		{
			Settled,		// 지급 완료 — dataTx 를 저장해야 한다
			Nothing,		// 새로 정산할 처치가 없다
			Discarded,		// 조작·이상치 — 지급 없이 인덱스만 넘겼다(field 만 저장)
			Error			// 데이터 로드 실패 등 — 아무것도 바꾸지 않았다
		}

		private sealed class SettleResult
		{
			public static readonly SettleResult NothingToDo = new SettleResult(SettleStatus.Nothing, null);

			public readonly SettleStatus status;
			public readonly string message;
			public int exp = -1;
			public readonly List<TransactionValue> dataTx = new List<TransactionValue>();

			public SettleResult(SettleStatus status, string message)
			{
				this.status = status;
				this.message = message;
			}
		}

		// 처치 목록을 검증·재현해 유저 데이터를 바꾼다. 저장은 호출자가 USER_FIELD 와 함께 한다.
		// Settled·Discarded 면 field(nextKillIndex·lastSettleUnixMs)도 갱신돼 있다.
		private static SettleResult settle(FieldSessionDto field, FieldKillDto[] requested)
		{
			// 1. 이미 정산된 처치는 건너뛴다(응답 유실 후 재전송). 남은 것은 연속이어야 한다.
			List<FieldKillDto> kills = new List<FieldKillDto>();
			if (requested != null)
			{
				for (int i = 0; i < requested.Length; i++)
				{
					if (requested[i] != null && requested[i].killIndex >= field.nextKillIndex)
					{
						kills.Add(requested[i]);
					}
				}
			}

			if (kills.Count == 0)
			{
				return SettleResult.NothingToDo;
			}

			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			for (int i = 1; i < kills.Count; i++)
			{
				if (kills[i].killIndex != kills[i - 1].killIndex + 1)
				{
					return discard(field, kills, nowMs, "kill index not contiguous");
				}
			}

			// 2. 처치 수 상한 — 직전 정산 이후 경과 시간으로 가능한 최대 처치 수.
			double elapsed = (field.lastSettleUnixMs > 0) ? (nowMs - field.lastSettleUnixMs) / 1000.0 : MaxWindowSeconds;
			double window = Math.Min(Math.Max(elapsed, 0.0) + SettleLagSeconds, MaxWindowSeconds);
			int allowed = (int)(MaxKillsPerSecond * window) + KillBurst;
			if (kills.Count > allowed)
			{
				return discard(field, kills, nowMs, "too many kills: " + kills.Count + " > " + allowed);
			}

			// 3. 유저 데이터 로드
			if (MyData.Load("USER_LOADOUT", out LoadoutDto loadout, out string loadoutErr) == false)
			{
				return new SettleResult(SettleStatus.Error, loadoutErr);
			}

			if (MyData.Load("USER_INVENTORY", out InventoryDto inventory, out string invErr) == false)
			{
				return new SettleResult(SettleStatus.Error, invErr);
			}

			if (MyData.Load("USER_CURRENCY", out CurrencyDto currency, out string curErr) == false)
			{
				return new SettleResult(SettleStatus.Error, curErr);
			}

			if (MyData.Load("USER_MASTERY", out MasteryDto mastery, out string masteryErr) == false)
			{
				return new SettleResult(SettleStatus.Error, masteryErr);
			}

			if (HeroPassOps.Load(out HeroPassDto heroPass, out string heroPassErr) == false)
			{
				return new SettleResult(SettleStatus.Error, heroPassErr);
			}

			// 4. 처치별 검증 → 재현 → 주운 것만 지급
			RewardApplier applier = new RewardApplier(inventory, currency);
			List<RolledReward> rolled = new List<RolledReward>();
			int gainedExp = 0;

			// 마스터리별 경험치 — 처치 시점의 장착 무기 마스터리에 같은 값이 들어간다(마스터리 설계 5.2).
			Dictionary<int, int> masteryExp = new Dictionary<int, int>();

			for (int i = 0; i < kills.Count; i++)
			{
				FieldKillDto kill = kills[i];
				string invalid = validateKill(kill);
				if (invalid != null)
				{
					return discard(field, kills, nowMs, invalid);
				}

				rolled.Clear();
				DeterministicRandom rng = new DeterministicRandom(KillSeed.Derive(field.seed, kill.killIndex, kill.monsterId));

				// 클라(MonsterKillReward.rollDrops)와 같은 순서 — 고유 드랍 → 지역 드랍, 같은 rng.
				Table_Monster.Row monster = Table_Monster.Get(kill.monsterId);
				RewardRoller.Roll(monster.RewardGroupID, kill.goldBonusPermille, rng, rolled, null);
				RewardRoller.Roll(kill.spawnRewardGroupId, kill.goldBonusPermille, rng, rolled, null);

				// 존재하지 않는 보상 인덱스를 주웠다고 하면 조작이다.
				int tracked = Math.Min(rolled.Count, 64);
				long validBits = (tracked >= 64) ? -1L : (1L << tracked) - 1;
				if ((kill.pickedMask & ~validBits) != 0)
				{
					return discard(field, kills, nowMs, "invalid pickedMask at kill " + kill.killIndex);
				}

				for (int r = 0; r < tracked; r++)
				{
					if (((kill.pickedMask >> r) & 1L) == 0)
					{
						continue;
					}

					long uid = rolled[r].isEquipment ? EquipmentUid.ForFieldDrop(field.epoch, kill.killIndex, r) : 0;
					applier.Apply(rolled[r], uid);
				}

				int exp = MonsterExp.Calc(kill.monsterId, kill.level, kill.expBonusPermille);
				gainedExp += exp;

				if (kill.masteryId != 0 && exp > 0)
				{
					masteryExp.TryGetValue(kill.masteryId, out int sum);
					masteryExp[kill.masteryId] = sum + exp;
				}
			}

			// 마스터리 대상은 그 무기를 실제로 가졌는지 본다 — 이번 배치에서 주운 무기까지 포함해 판단한다.
			// 못 찾으면 그 마스터리 적립만 건너뛴다. 아직 정산 전인 처치에서 주운 무기일 수 있어 배치 전체를 버리지 않는다.
			Dictionary<int, int>.Enumerator me = masteryExp.GetEnumerator();
			while (me.MoveNext() == true)
			{
				if (MasteryFunctions.OwnsWeaponFor(inventory, me.Current.Key) == true)
				{
					MasteryRules.AddExp(mastery, (WeaponMastery)me.Current.Key, me.Current.Value);
				}
			}

			// 히어로패스 활동 — 배치 전체가 검증을 통과한 뒤에만 센다(discard 된 배치는 세지 않는다).
			for (int i = 0; i < kills.Count; i++)
			{
				HeroPassOps.CountKill(heroPass, kills[i].monsterId);
			}

			loadout.exp += gainedExp;
			loadout.level = MasteryRules.CharacterLevelFromExp(loadout.exp);
			field.nextKillIndex = kills[kills.Count - 1].killIndex + 1;
			field.lastSettleUnixMs = nowMs;

			SettleResult result = new SettleResult(SettleStatus.Settled, null);
			result.exp = loadout.exp;
			result.dataTx.Add(TransactionValue.SetUpdate("USER_LOADOUT", new Where(), toParam(loadout)));
			result.dataTx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
			result.dataTx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));
			result.dataTx.Add(TransactionValue.SetUpdate("USER_MASTERY", new Where(), toParam(mastery)));
			result.dataTx.Add(HeroPassOps.ToUpdate(heroPass));
			return result;
		}

		// 조작·이상치 배치 — 지급 없이 인덱스만 넘겨 버린다. 클라 로컬 반영분은 다음 로그인에 서버값으로 정리된다.
		private static SettleResult discard(FieldSessionDto field, List<FieldKillDto> kills, long nowMs, string reason)
		{
			field.nextKillIndex = kills[kills.Count - 1].killIndex + 1;
			field.lastSettleUnixMs = nowMs;
			return new SettleResult(SettleStatus.Discarded, reason);
		}

		// ── 검증 ──────────────────────────────────────────────────────

		private static string validateKill(FieldKillDto kill)
		{
			if (Table_Monster.Get(kill.monsterId) == null)
			{
				return "unknown monster " + kill.monsterId;
			}

			// 필드보스(DailyReset 스폰으로만 나오는 몬스터)는 FieldBossKill 로만 정산한다 — 배치로 반복 청구하는 것을 막는다.
			if (_fieldBossOnly.Contains(kill.monsterId) == true)
			{
				return "field boss in batch: " + kill.monsterId;
			}

			// 지역 드랍 그룹은 그 몬스터가 실제로 배치된 MonsterSpawn 행의 값이어야 한다(0 = 없음).
			if (kill.spawnRewardGroupId != 0)
			{
				if (_spawnGroupsByMonster.TryGetValue(kill.monsterId, out HashSet<int> groups) == false
					|| groups.Contains(kill.spawnRewardGroupId) == false)
				{
					return "invalid spawn reward group " + kill.spawnRewardGroupId + " for monster " + kill.monsterId;
				}
			}

			// TODO — 몬스터별 레벨 범위. 던전 코어처럼 스폰 그룹 밖에서 단계 레벨로 소환되는 경로가 있어 지금은 전체 최대치로 본다.
			if (kill.level < 1 || kill.level > _maxMonsterLevel)
			{
				return "invalid level " + kill.level;
			}

			if (kill.expBonusPermille < 0 || kill.expBonusPermille > MaxBonusPermille
				|| kill.goldBonusPermille < 0 || kill.goldBonusPermille > MaxBonusPermille)
			{
				return "bonus out of range";
			}

			return null;
		}

		private static void buildValidationData()
		{
			if (_spawnGroupsByMonster != null)
			{
				return;
			}

			Dictionary<int, HashSet<int>> groups = new Dictionary<int, HashSet<int>>();
			HashSet<int> dailyMonsters = new HashSet<int>();
			HashSet<int> otherMonsters = new HashSet<int>();
			int maxLevel = 1;

			Dictionary<int, Table_MonsterSpawn.Row>.Enumerator se = Table_MonsterSpawn.All().GetEnumerator();
			while (se.MoveNext() == true)
			{
				Table_MonsterSpawn.Row row = se.Current.Value;
				if (groups.TryGetValue(row.MonsterID, out HashSet<int> set) == false)
				{
					set = new HashSet<int>();
					groups.Add(row.MonsterID, set);
				}

				if (row.RewardGroupID > 0)
				{
					set.Add(row.RewardGroupID);
				}

				if (row.RespawnType == RespawnType.DailyReset)
				{
					dailyMonsters.Add(row.MonsterID);
				}
				else
				{
					otherMonsters.Add(row.MonsterID);
				}

				maxLevel = Math.Max(maxLevel, row.Level);
			}

			// 던전은 스폰 행 레벨 대신 단계 레벨로 소환한다.
			Dictionary<int, Table_GoldDungeon.Row>.Enumerator ge = Table_GoldDungeon.All().GetEnumerator();
			while (ge.MoveNext() == true)
			{
				maxLevel = Math.Max(maxLevel, ge.Current.Value.MonsterLevel);
			}

			Dictionary<int, Table_RiftDungeon.Row>.Enumerator re = Table_RiftDungeon.All().GetEnumerator();
			while (re.MoveNext() == true)
			{
				maxLevel = Math.Max(maxLevel, re.Current.Value.MonsterLevel);
			}

			Dictionary<int, Table_LabyrinthDungeon.Row>.Enumerator le = Table_LabyrinthDungeon.All().GetEnumerator();
			while (le.MoveNext() == true)
			{
				maxLevel = Math.Max(maxLevel, le.Current.Value.MonsterLevel);
			}

			Dictionary<int, Table_RuinsDungeon.Row>.Enumerator ue = Table_RuinsDungeon.All().GetEnumerator();
			while (ue.MoveNext() == true)
			{
				maxLevel = Math.Max(maxLevel, ue.Current.Value.MonsterLevel);
			}

			dailyMonsters.ExceptWith(otherMonsters);

			_maxMonsterLevel = maxLevel;
			_fieldBossOnly = dailyMonsters;
			_spawnGroupsByMonster = groups;
		}

		// ── 응답 ──────────────────────────────────────────────────────

		private static Stream reject(string error, int nextKillIndex)
		{
			FieldSettleResponse response = new FieldSettleResponse();
			response.success = false;
			response.error = error;
			response.nextKillIndex = nextKillIndex;
			return FuncResult.Json(response);
		}

		private static Stream succeed(int nextKillIndex, int exp)
		{
			FieldSettleResponse response = new FieldSettleResponse();
			response.success = true;
			response.nextKillIndex = nextKillIndex;
			response.exp = exp;
			return FuncResult.Json(response);
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}

	// 필드 세션 발급 — 로그인(GetUserData)과 로딩(FieldSessionRotate)이 함께 쓴다.
	public static class FieldSessions
	{
		// 새 시드(예측 불가 — 암호학적 난수)와 epoch(필드 드랍 장비 UID 대역)를 발급하고 처치 번호를 0 으로 되돌린다.
		public static void Renew(FieldSessionDto field)
		{
			byte[] bytes = RandomNumberGenerator.GetBytes(8);
			long seed = BitConverter.ToInt64(bytes, 0);
			if (seed == 0)
			{
				seed = 1;	// 0 은 클라에서 "세션 없음" 으로 쓴다
			}

			field.seed = seed;
			field.epoch++;
			field.nextKillIndex = 0;
			field.lastSettleUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}
	}
}
