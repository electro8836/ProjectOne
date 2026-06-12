using System;
using System.Collections.Generic;
using EDT;
using ProjectOne.Unit;

namespace ProjectOne.Buff
{
	// 코드 버프(IBuffBehavior) 생성 registry — 버프 ID 와 동작 클래스를 명시적으로 매핑한다.
	// 리플렉션(Type.GetType by-name) 대체 — enum 이름과 클래스 이름이 달라도(DEBUFF_STUN ↔ BUFF_STUN) 정확히 연결되고,
	// 누락이 등록 시점에 드러나며 IL2CPP 스트리핑에 안전하다.
	// 등록 안 된 버프는 null → 데이터 버프(스탯/주기 효과)로만 동작한다.
	public static class BuffBehaviorRegistry
	{
		// registry 표는 "콜백 등록"이 아니라 "데이터 선언" → 인라인 델리게이트 사용.
		static readonly Dictionary<BuffInfo, Func<UnitBase, UnitBase, IBuffBehavior>> _factories = new Dictionary<BuffInfo, Func<UnitBase, UnitBase, IBuffBehavior>>
		{
			{ BuffInfo.DEBUFF_STUN,    (o, s) => new DEBUFF_STUN(o, s) },
			{ BuffInfo.DEBUFF_SILENCE, (o, s) => new DEBUFF_SILENCE(o, s) },
			{ BuffInfo.DEBUFF_BINDING, (o, s) => new DEBUFF_BINDING(o, s) },
		};

		public static IBuffBehavior Create(BuffInfo id, UnitBase owner, UnitBase source)
		{
			Func<UnitBase, UnitBase, IBuffBehavior> factory;
			if (_factories.TryGetValue(id, out factory) == false)
			{
				return null;
			}

			return factory(owner, source);
		}
	}
}
