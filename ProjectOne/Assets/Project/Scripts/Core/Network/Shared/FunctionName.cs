namespace ProjectOne.Shared
{
	// 뒤끝 함수명 상수 — 클라 호출명과 서버 등록명을 단일 소스로 일치시킨다.
	// 서버(Functions.sln)도 이 파일을 링크 컴파일해 같은 상수를 사용한다.
	public static class FunctionName
	{
		public const string GetUserData = "GetUserData";
		public const string DungeonClear = "DungeonClear";
	}
}
