using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Monsters
{
	// 몬스터 처치 보상 지급 (몬스터 설계 9장).
	//
	// 지금은 **경험치만** 담당한다. 드랍은 Reward 테이블 지급기가 필요하므로 STEP 10 이고,
	// 그때 이 클래스가 `Monster.RewardGroupID`(고유 드랍) + `MonsterSpawn.RewardGroupID`(지역 드랍)를
	// 함께 굴리는 자리가 된다.
	//
	// 경험치 = (BaseExp + PerLevelExp × (Level - 1)) × (1 + Stat_ExpBonus)
	//
	// `Stat_ExpBonus` 는 **적 처치분에만** 곱한다 — 퀘스트·던전 클리어 경험치는 고정값이다
	// (기반테이블 8.1). 그래서 던전 클리어 보상 경로(서버 권위)와 이 경로는 분리되어 있다.
	public sealed class MonsterKillReward : MonoSingleton<MonsterKillReward>
	{
		// 전투 수명 — 마을로 따라가지 않는다.
		protected override bool Persistent => false;

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
			int baseExp = MonsterCatalog.GetKillExp(e.MonsterID, e.Level);
			if (baseExp <= 0)
			{
				return;		// BaseExp 미입력 — MonsterCatalog 가 별도로 경고하지 않는 값이라 조용히 넘긴다
			}

			int exp = Mathf.RoundToInt(baseExp * (1f + getExpBonus()));
			if (exp <= 0)
			{
				return;
			}

			// 캐릭터와 현재 장착 무기의 마스터리에 같은 값이 들어간다 (마스터리 설계 5.2).
			Account.Instance.AddExp(exp);
		}

		// 살아있는 히어로의 경험치 획득량 보너스. 없으면 0.
		private static float getExpBonus()
		{
			if (UnitContainer.HasInstance == false)
			{
				return 0f;
			}

			System.Collections.Generic.IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
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
