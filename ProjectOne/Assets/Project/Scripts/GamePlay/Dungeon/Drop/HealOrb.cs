using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Event;

namespace ProjectOne.Dungeon
{
	// 몬스터가 쓰러진 자리에 확률로 뜨는 회복 오브.
	// 제자리에 머물며 히어로가 직접 닿아야 획득된다 — 자석 흡입 대상이 아니다.
	public class HealOrb : DropObject
	{
		// 체력 회복 비율 (최대치 대비)
		private const float RestoreRatio = 0.25f;

		protected override void OnPickup(UnitBase hero)
		{
			float before = hero.Vitals.Hp;
			hero.Vitals.ModifyHp(hero.Stats.GetStat(Stat.Stat_MaxHp) * RestoreRatio);

			// 풀피 클램프로 실제 회복이 0이면 알리지 않는다 — 0 이 뜨는 팝업을 막는다.
			int healed = Mathf.RoundToInt(hero.Vitals.Hp - before);
			if (healed > 0)
			{
				EventManager.Instance.Publish(new HealAppliedEvent(hero, healed));
			}
		}
	}
}
