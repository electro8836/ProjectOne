using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Monsters
{
	// 몬스터 처치 보상 지급 (몬스터 설계 9장).
	//
	//   경험치 = (BaseExp + PerLevelExp × (Level - 1)) × (1000 + Stat_ExpBonus‰) / 1000  — MonsterExp.Calc
	//   드랍   = Monster.RewardGroupID(고유) + MonsterSpawn.RewardGroupID(지역) 를 **함께** 굴린다
	//
	// `Stat_ExpBonus` / `Stat_GoldDropBonus` 는 **적 처치분에만** 곱한다 — 퀘스트·던전 클리어
	// 보상은 고정값이다 (기반테이블 8.1). 그래서 던전 클리어 보상 경로(서버 권위)와 이 경로는 분리되어 있다.
	//
	// 로그인 중이면 처치를 FieldKillLedger 에 기록해 서버 배치 정산(FieldSettle)으로 올린다.
	// 경험치·드랍은 화면 반응을 위해 로컬에 먼저 반영하고, 서버는 같은 시드로 재현해 같은 값을 지급한다.
	public sealed class MonsterKillReward : MonoSingleton<MonsterKillReward>
	{
		// 전투 수명 — 마을로 따라가지 않는다.
		protected override bool Persistent => false;

		// 처치 1회의 드랍 결과 버퍼 — 지급은 메인 스레드 단일 경로라 재사용해도 안전하다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>(8);

		protected override void Awake()
		{
			base.Awake();
			EventManager.Instance.Subscribe<MonsterKillEvent>(onMonsterKill);
		}

		// 인스턴스 생성만을 목적으로 하는 호출 지점 — Instance 접근이 곧 생성이라 본문이 필요 없다.
		public void Touch()
		{
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(onMonsterKill);
			base.OnDestroy();
		}

		private void onMonsterKill(MonsterKillEvent e)
		{
			// 필드보스는 FieldBossReward 가 서버 즉시 정산으로 처리한다 — 경험치·드랍·원장 모두 여기서 빠진다.
			if (e.FieldBossSpawnID != 0)
			{
				return;
			}

			// 보너스는 처치 시점 값을 퍼밀 정수로 고정한다 — 서버가 같은 값으로 재계산한다.
			int expBonusPermille = GetExpBonusPermille();
			int goldBonusPermille = RewardGranter.GetGoldBonusPermille();

			FieldKillLedger ledger = FieldKillLedger.Instance;
			bool settleOnServer = ledger.IsActive;
			int killIndex = settleOnServer ? ledger.NextKillIndex : -1;

			int exp = grantExp(e, expBonusPermille);
			rollDrops(e, goldBonusPermille, killIndex);

			if (settleOnServer == true)
			{
				FieldKillDto dto = new FieldKillDto();
				dto.killIndex = killIndex;
				dto.monsterId = e.MonsterID;
				dto.level = e.Level;
				dto.spawnRewardGroupId = e.SpawnRewardGroupID;
				dto.expBonusPermille = expBonusPermille;
				dto.goldBonusPermille = goldBonusPermille;
				dto.masteryId = CurrentMasteryId();
				ledger.Register(dto, _granted.Count, exp);
			}

			spawnDrops(e, killIndex);
		}

		// 지급한 경험치를 돌려준다(원장이 미정산 경험치로 들고 있는다).
		private int grantExp(MonsterKillEvent e, int expBonusPermille)
		{
			int exp = MonsterExp.Calc(e.MonsterID, e.Level, expBonusPermille);
			if (exp <= 0)
			{
				return 0;		// BaseExp 미입력 — MonsterCatalog 가 별도로 경고하지 않는 값이라 조용히 넘긴다
			}

			// 캐릭터와 현재 장착 무기의 마스터리에 같은 값이 들어간다 (마스터리 설계 5.2).
			Account.Instance.AddExp(exp);
			return exp;
		}

		// 고유 드랍과 지역 드랍은 **둘 다** 굴린다 (보상 설계 9장). 하나를 고르는 것이 아니다.
		// 보상 그룹이 지정되지 않았으면(대부분의 경우) 아무 일도 일어나지 않는다.
		//
		// 서버 정산 중이면 처치별 시드 난수로 굴린다 — 서버가 같은 시드로 같은 순서(고유 → 지역)를 재현한다.
		// 장비 UID 도 처치 좌표로 정해 서버와 맞춘다.
		private void rollDrops(MonsterKillEvent e, int goldBonusPermille, int killIndex)
		{
			_granted.Clear();

			IRandomSource rng = UnityRandomSource.Instance;
			if (killIndex >= 0)
			{
				rng = new DeterministicRandom(KillSeed.Derive(FieldKillLedger.Instance.Seed, killIndex, e.MonsterID));
			}

			Table_Monster.Row monster = MonsterCatalog.GetMonster(e.MonsterID);
			if (monster != null)
			{
				RewardGranter.RollWith(monster.RewardGroupID, goldBonusPermille, rng, _granted);
			}

			RewardGranter.RollWith(e.SpawnRewardGroupID, goldBonusPermille, rng, _granted);

			if (killIndex < 0)
			{
				return;
			}

			if (_granted.Count > FieldKillLedger.MaxTrackedRewards)
			{
				Debug.LogError($"[MonsterKillReward] 처치 1건의 보상이 {_granted.Count}개 — {FieldKillLedger.MaxTrackedRewards}개 초과분은 서버에 정산되지 않는다.");
			}

			for (int i = 0; i < _granted.Count; i++)
			{
				if (_granted[i].equipment != null)
				{
					_granted[i].equipment.uid = EquipmentUid.ForFieldDrop(FieldKillLedger.Instance.Epoch, killIndex, i);
				}
			}
		}

		// 여기서는 떨어뜨리기만 한다 — 인벤토리에 들어가는 것은 히어로가 바닥 드랍을 획득 범위에
		// 넣었을 때다(사용자 결정). 드랍마다 (killIndex, 보상 인덱스) 를 실어 원장이 획득 여부를 기록하게 한다.
		private void spawnDrops(MonsterKillEvent e, int killIndex)
		{
			if (_granted.Count == 0)
			{
				return;
			}

			if (DropManager.HasInstance == false)
			{
				Debug.LogError("[MonsterKillReward] DropManager 가 없어 처치 보상을 떨어뜨리지 못했다 — 보상이 유실된다.");
				resolveAllAsMissed(killIndex);
				return;
			}

			DropManager.Instance.SpawnRewardDrops(e.Position, _granted, killIndex);
		}

		private void resolveAllAsMissed(int killIndex)
		{
			if (killIndex < 0)
			{
				return;
			}

			for (int i = 0; i < _granted.Count; i++)
			{
				FieldKillLedger.Instance.TryResolve(killIndex, i, false);
			}
		}

		// 살아있는 히어로의 경험치 획득량 보너스(퍼밀). 필드보스 정산(FieldBossReward)도 같은 값을 보낸다.
		public static int GetExpBonusPermille()
		{
			return Mathf.RoundToInt(getExpBonus() * 1000f);
		}

		// 처치 시점 장착 무기의 마스터리 — 서버가 같은 마스터리에 경험치를 적립한다. 미착용이면 0.
		public static int CurrentMasteryId()
		{
			Table_WeaponMastery.Row current = Account.Instance.Mastery.CurrentMastery;
			return (current != null) ? (int)current.ID : 0;
		}

		// 살아있는 히어로의 경험치 획득량 보너스. 없으면 0.
		private static float getExpBonus()
		{
			if (UnitManager.HasInstance == false)
			{
				return 0f;
			}

			System.Collections.Generic.IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && hero.Stats != null)
				{
					return hero.Stats.GetStat(Stat.Stat_ExpBonus);
				}
			}

			return 0f;
		}
	}
}
