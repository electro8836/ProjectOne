namespace ProjectOne.ServerData
{
	// 액션 계층 진입점 — 서버 권위 명령을 IServerCommand 구현체로 처리한다(로그인 시 Backnd 로 교체).
	public static class ServerCommandSystem
	{
		// 액션 식별자 — Backnd 함수명과 1:1 로 맞춘다.
		public const string ActionGetUserData = "getUserData";
		public const string ActionDungeonClear = "DungeonClear";   // 테스트: 서버가 exp/gold 가산 저장

		// 로그인 전 기본 스텁. 로그인 성공 시 BackndServerCommand 로 교체된다.
		static IServerCommand _command = new NullServerCommand();

		public static IServerCommand Command => _command;

		// 다른 구현체(서버 등)로 교체 (부트 시점 1회 호출 가정)
		public static void SetCommand(IServerCommand command)
		{
			if (command == null)
			{
				return;
			}

			_command = command;
		}
	}
}
