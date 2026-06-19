namespace ProjectOne.Shared
{
	// 뒤끝 함수명 상수 — 클라 호출명과 서버 등록명을 단일 소스로 일치시킨다.
	// 서버(Functions.sln)도 이 파일을 링크 컴파일해 같은 상수를 사용한다.
	public static class FunctionName
	{
		// 뒤끝 콘솔에 등록된 단일 진입점 펑션 이름 — InvokeFunction 의 funcName(= config.json functionName).
		public const string Deployed = "ProjectOneFunction";

		// 진입점 내부 분기용 action 식별자. 클라가 body 의 "action" 에 담아 보내면 서버가 분기한다.
		public const string GetUserData = "GetUserData";
		public const string DungeonClear = "DungeonClear";
	}
}
