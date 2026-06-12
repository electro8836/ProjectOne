using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Skill
{
	// 코드 스킬(ISkillBehavior) 생성 registry — 스킬 ID 와 실행 로직(액션 조합)을 명시적으로 매핑한다.
	// 리플렉션(Type.GetType by-name)을 대체 — 리네임/IL2CPP 스트리핑에 안전하고, 누락이 등록 시점에 드러난다.
	// 등록 안 된 스킬은 null → SkillExecutor 가 데이터 스킬(테이블 효과) 경로로 처리한다.
	public static class SkillBehaviorRegistry
	{
		// registry 표는 "콜백 등록"이 아니라 "데이터 선언"이며 액션 조합은 메서드 그룹화가 불가능 → 인라인 델리게이트 사용.
		static readonly Dictionary<SkillInfo, Func<ISkillBehavior>> _factories = new Dictionary<SkillInfo, Func<ISkillBehavior>>
		{
			{ SkillInfo.SKILL_DASH_01,        () => new SkillSequence(new DashAction()) },
			{ SkillInfo.SKILL_DASH_ATTACK_01, () => new SkillSequence(new DashAttackAction()) },
		};

		public static ISkillBehavior Create(SkillInfo id)
		{
			Func<ISkillBehavior> factory;
			if (_factories.TryGetValue(id, out factory) == false)
			{
				return null;
			}

			return factory();
		}
	}
}
