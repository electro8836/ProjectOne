namespace ProjectOne.Dungeon
{
	// 보스 기믹 코어가 활성화됐음을 받는 쪽. 보스의 페이즈 전환 시퀀스가 구현한다.
	// 콜백을 델리게이트로 넘기지 않는 이유 — 람다 없이 메서드 그룹만으로는 대상 인스턴스를
	// 함께 넘길 수 없고, 코어가 풀로 돌아갈 때 구독 해제를 잊기 쉽다.
	public interface IBossGimmickListener
	{
		void OnGimmickActivated();
	}
}
