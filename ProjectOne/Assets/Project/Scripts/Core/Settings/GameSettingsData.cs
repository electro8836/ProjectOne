namespace ProjectOne.Settings
{
	// 게임 옵션의 직렬화 데이터 — JSON 과 1:1 매핑된다. 옵션이 늘면 여기에 public 필드만 추가.
	// 새 필드는 기본값(초기자)을 가지므로, 구버전 JSON 에 없던 필드도 기본값으로 로드된다.
	[System.Serializable]
	public class GameSettingsData
	{
		public bool showPlayerSkillIndicator = true;
		// 내 캐릭터의 기본공격 사거리 원 표시 여부
		public bool showAttackRangeCircle = true;
		// 조이스틱 유동성(floating) 사용 여부 — true 면 누른 위치로 배경이 이동, false 면 고정
		public bool useFloatingJoystick = true;
	}
}
