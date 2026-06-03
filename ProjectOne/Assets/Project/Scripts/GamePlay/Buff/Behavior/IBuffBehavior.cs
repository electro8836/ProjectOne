namespace ProjectOne.Buff
{
	// 코드로 정의되는 버프의 동작 인터페이스
	// - 버프 ID(enum) 와 동일한 이름의 클래스가 이 인터페이스를 구현하면
	//   BuffRuntime 이 리플렉션으로 찾아 활성/비활성 시점에 호출한다.
	// - 구현 클래스는 (UnitBase owner, UnitBase caster) 생성자를 가져야 한다.
	public interface IBuffBehavior
	{
		// 버프 적용 순간 1회 호출
		void OnActivate();

		// 버프 만료/해제 순간 1회 호출
		void OnDeactivate();
	}
}
