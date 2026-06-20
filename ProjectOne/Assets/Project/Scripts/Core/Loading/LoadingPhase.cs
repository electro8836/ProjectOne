namespace ProjectOne.Loading
{
	// 로딩 진행 단계. 각 State 는 자기 단계의 로컬 비율(0~1)만 보고하고,
	// 절대 진행값으로의 매핑(밴드)은 LoadingManager 가 흐름별로 결정한다.
	public enum LoadingPhase
	{
		Patch,       // 번들 다운로드
		ServerData,  // 서버 계정/우편/출석/랭킹 등 fetch
		SceneLoad,   // 씬 비동기 로드
		SceneReady,  // 씬 준비 완료 게이트
	}

	// 로딩 세션의 종류(목적지 기준). 흐름마다 단계 구성·밴드·라벨이 다르다.
	public enum LoadingFlow
	{
		ToLobby,        // 타이틀→로비 (패치+서버데이터+씬+준비)
		ToBattle,       // 로비→배틀 (씬+준비)
		ReturnToLobby,  // 배틀→로비 복귀 (씬+준비)
	}
}
