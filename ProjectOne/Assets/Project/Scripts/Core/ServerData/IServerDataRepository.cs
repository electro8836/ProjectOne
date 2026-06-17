namespace ProjectOne.ServerData
{
	// 도메인 키 단위로 유저 데이터(DTO)를 읽고 쓰는 저장소 추상화.
	// - 현재는 로컬 JSON, 추후 서버 구현으로 교체 → 매니저는 이 인터페이스로만 접근한다.
	public interface IServerDataRepository
	{
		// 저장된 데이터가 없으면 false 반환 (data = null)
		bool TryLoad<T>(string key, out T data) where T : class;

		void Save<T>(string key, T data) where T : class;
	}
}
