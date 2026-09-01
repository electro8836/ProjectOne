namespace ProjectOne.UI
{
	// 버텨서 하는 상호작용의 종류. InteractionGauge 가운데 아이콘을 고르는 데 쓴다.
	//
	// 게이지는 종류를 몰라도 되게 만든다 — 무엇을 하는 중인지는 아이콘이 알리고,
	// 진행도 계산은 상호작용을 소유한 쪽(기믹 코어·상자·채집물)이 한다.
	public enum InteractionKind
	{
		None = 0,
		BossGimmick,	// 보스 전멸기 파훼
		Chest,			// 상자 개봉
		Gather,			// 자원 수집
	}
}
