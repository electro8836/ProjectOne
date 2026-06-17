namespace ProjectOne.ServerData
{
	// 유저 데이터 저장소 진입점 — 서버 연동 시 Repository 구현체만 이 곳에서 교체한다.
	public static class ServerDataSystem
	{
		// 데이터 도메인 키 (로컬 파일명 / 서버 키로 사용)
		public const string KeyCharacter = "character";
		public const string KeyInventory = "inventory";
		public const string KeySkill = "skill";

		static IServerDataRepository _repository = new LocalJsonRepository();

		public static IServerDataRepository Repository => _repository;

		// 다른 구현체(서버 등)로 교체 (부트 시점 1회 호출 가정)
		public static void SetRepository(IServerDataRepository repository)
		{
			if (repository == null)
			{
				return;
			}

			_repository = repository;
		}
	}
}
