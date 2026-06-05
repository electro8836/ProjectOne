namespace ProjectOne.Buff
{
	// 매 프레임 갱신이 필요한 코드 버프용 옵트인 인터페이스.
	// - IBuffBehavior 를 구현한 버프 중 이 인터페이스도 구현하면 BuffRuntime 이 매 Tick 마다 호출한다.
	// - Tick 이 true 를 반환하면 해당 버프는 이번 프레임에 종료된다(거리 도달·막힘 등 자체 종료 조건).
	public interface ITickableBuff : IBuffBehavior
	{
		// 호스트 런타임 주입 — OnActivate 호출 직전에 1회. SourceSkill/RemainingDuration/Effect 등에 접근.
		void SetHost(BuffRuntime host);

		bool Tick(float dt);
	}
}
