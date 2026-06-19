namespace ProjectOne.Network
{
	// 로그인 방식. NetworkManager.Login 이 타입별 핸들러로 위임한다.
	public enum LoginType
	{
		Guest,
		Google,
		Apple,      // 추후 핸들러 추가
		Facebook    // 추후 핸들러 추가
	}
}
