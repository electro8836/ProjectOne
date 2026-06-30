namespace ProjectOne.UI
{
	// 로비 하단 메뉴 탭 종류 (TabGroup 내 인덱스와 1:1 — 순서 = 인스펙터 탭 자식 순서).
	// 탭 식별은 인덱스 기반이므로 이 enum은 로비 코드에서만 쓰이고, 범용 TabGroup은 모른다.
	public enum LobbyMenuTab
	{
		Character = 0,
		Equipment = 1,
		CardSkill = 2,
		Shop = 3,
		Craft = 4
	}
}
