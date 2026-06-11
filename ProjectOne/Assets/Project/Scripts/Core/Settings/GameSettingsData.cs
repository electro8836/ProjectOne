namespace ProjectOne.Settings
{
	// 게임 옵션의 직렬화 데이터 — JSON 과 1:1 매핑된다. 옵션이 늘면 여기에 public 필드만 추가.
	// 새 필드는 기본값(초기자)을 가지므로, 구버전 JSON 에 없던 필드도 기본값으로 로드된다.
	[System.Serializable]
	public class GameSettingsData
	{
		public bool showPlayerSkillIndicator = true;
	}
}
