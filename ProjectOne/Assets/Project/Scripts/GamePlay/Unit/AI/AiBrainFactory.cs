using EDT;

namespace ProjectOne.Unit.AI
{
	// AI 타입 분기를 한 곳에 격리 — 유닛 정보로 적절한 behavior 를 만들어 AiBrain 을 생성.
	public static class AiBrainFactory
	{
		// 히어로: 이동/시선은 플레이어가 하고 AI 는 스킬 자동 시전만 (근접/원거리 구분 없음)
		public static AiBrain CreateForHero(UnitBase hero)
		{
			return new AiBrain(hero, new HeroAutoBehavior());
		}

		// 몬스터: MonsterAI.AIType 이 FSM/BT 선택까지 겸한다 (몬스터 설계 5장).
		// row 가 null 이면(AIGroupID 미지정 등) 근접 접근형으로 폴백한다 — 경고는 MonsterCatalog 가 낸다.
		public static AiBrain CreateForMonster(UnitBase monster, Table_MonsterAI.Row row)
		{
			IAiBehavior behavior = createBehavior(row);
			AiBrain brain = new AiBrain(monster, behavior);
			brain.Blackboard.ApplyAI(row);
			return brain;
		}

		// 소환물: Table_Summon.AIType 이 behavior 를 정한다 (설계 7.5).
		// row 가 null 이면 고정형으로 폴백한다 — 움직이지 않는 쪽이 안전하다.
		public static AiBrain CreateForSummon(UnitBase summon, Table_Summon.Row row)
		{
			IAiBehavior behavior = createSummonBehavior(row);
			AiBrain brain = new AiBrain(summon, behavior);
			brain.Blackboard.ApplySummon(row);
			return brain;
		}

		private static IAiBehavior createSummonBehavior(Table_Summon.Row row)
		{
			SummonAIType type = (row != null) ? row.AIType : SummonAIType.None;

			switch (type)
			{
				case SummonAIType.Follow:
					return new SummonFollowBehavior();

				case SummonAIType.Chase:
					return new SummonChaseBehavior();

				case SummonAIType.Wander:
					return new SummonWanderBehavior();

				case SummonAIType.Orbit:
					// 설계 2.11 — 예약된 값이다. 데이터에 들어오면 즉시 드러내야 한다.
					UnityEngine.Debug.LogError($"[AiBrainFactory] SummonAIType.Orbit 은 예약 값입니다 — Summon:{(row != null ? row.ID.ToString() : "null")}");
					break;
			}

			// Stationary 와 None(데이터 누락) 은 고정형. 몬스터와 같은 behavior 를 쓴다.
			return new MonsterStationaryBehavior();
		}

		private static IAiBehavior createBehavior(Table_MonsterAI.Row row)
		{
			MonsterAIType type = (row != null) ? row.AIType : MonsterAIType.None;

			switch (type)
			{
				case MonsterAIType.Ranged:
					return new MonsterRangedBehavior();

				case MonsterAIType.Stationary:
					return new MonsterStationaryBehavior();

				case MonsterAIType.Boss:
					return new BossBehavior();
			}

			// Melee 와 None(데이터 누락) 은 접근형
			return new MonsterApproachBehavior();
		}
	}
}
