using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 히어로 근처에 주기적으로 생기는 전투룬.
	// 제자리에 머물며 직접 밟아야 획득되고, 시간 안에 먹지 않으면 스스로 사라진다(수명은 베이스가 관리).
	public class BuffRune : DropObject
	{
		// 버프 지속시간(초) / 최대 중첩. 수치(공/방 +10%)는 SkillEffect 테이블이 소유한다.
		private const float RuneBuffDuration = 10f;
		private const int RuneBuffStackMax = 5;

		protected override void OnPickup(UnitBase hero)
		{
			// 버프 1개가 EffectID_01/02 로 공/방을 함께 올린다 — Apply 는 한 번이면 된다.
			hero.BuffContainer.Apply(EDT.Buff.BUFF_CombatRune, RuneBuffDuration, RuneBuffStackMax, hero, EDT.Skill.None);
		}
	}
}
