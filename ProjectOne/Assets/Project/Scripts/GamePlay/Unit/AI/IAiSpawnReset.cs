namespace ProjectOne.Unit.AI
{
	// 스폰마다 자체 상태를 비워야 하는 behavior 가 선택적으로 구현한다.
	//
	// behavior 인스턴스는 풀 생성 시 1회만 만들어지고 리스폰마다 재생성되지 않는다
	// (MonsterPool 이 AiBrainFactory 를 풀 생성 시점에 부른다). 히스테리시스처럼
	// 이어져도 무해한 상태는 그냥 두면 되고, 보스 페이즈처럼 반드시 되돌려야 하는
	// 상태만 이 인터페이스를 구현한다 — IAiBehavior 전체에 빈 메서드를 강요하지 않는다.
	public interface IAiSpawnReset
	{
		void OnSpawnReset(UnitBase self);
	}
}
