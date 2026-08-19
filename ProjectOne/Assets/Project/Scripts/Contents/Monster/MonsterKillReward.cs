using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Reward;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Monsters
{
	// 몬스터 처치 보상 지급 (몬스터 설계 9장).
	//
	//   경험치 = (BaseExp + PerLevelExp × (Level - 1)) × (1 + Stat_ExpBonus)
	//   드랍   = Monster.RewardGroupID(고유) + MonsterSpawn.RewardGroupID(지역) 를 **함께** 굴린다
	//
	// `Stat_ExpBonus` / `Stat_GoldDropBonus` 는 **적 처치분에만** 곱한다 — 퀘스트·던전 클리어
	// 보상은 고정값이다 (기반테이블 8.1). 그래서 던전 클리어 보상 경로(서버 권위)와 이 경로는 분리되어 있다.
	//
	// 지급 결과는 목록으로 남긴다. 지금은 로컬 지급뿐이지만, 이 목록이 그대로
	// 서버 배치 업로드의 페이로드가 된다 (STEP 14).
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
			grantExp(e);
			grantDrops(e);
		}

		private void grantExp(MonsterKillEvent e)
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

		// 고유 드랍과 지역 드랍은 **둘 다** 굴린다 (보상 설계 9장). 하나를 고르는 것이 아니다.
		// 보상 그룹이 지정되지 않았으면(대부분의 경우) 아무 일도 일어나지 않는다.
		private void grantDrops(MonsterKillEvent e)
		{
			_granted.Clear();

			Table_Monster.Row monster = MonsterCatalog.GetMonster(e.MonsterID);
			if (monster != null)
			{
				RewardGranter.Grant(monster.RewardGroupID, RewardContext.MonsterKill, _granted);
			}

			RewardGranter.Grant(e.SpawnRewardGroupID, RewardContext.MonsterKill, _granted);

			// 드랍 연출(바닥 오브젝트)은 붙이지 않는다 — 인벤토리에 바로 들어간다.
			// 결과 목록은 서버 배치 업로드(STEP 14)가 소비할 자리다.
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
