namespace ProjectOne.Loading
{
	// 로딩 진행 단계. 각 State 는 자기 단계의 로컬 비율(0~1)만 보고하고,
	// 절대 진행값으로의 매핑(밴드)은 LoadingManager 가 흐름별로 결정한다.
	public enum LoadingPhase
	{
		Patch,       // 번들 다운로드
		ServerData,  // 서버 계정/우편/출석/랭킹 등 fetch
		SceneLoad,   // 씬 비동기 로드 (또는 씬 전환 없는 그리드맵 로드)
		SceneReady,  // 씬 준비 완료 게이트
	}

	// 로딩 세션의 종류(목적지 기준). 흐름마다 단계 구성·밴드·라벨이 다르다.
	//
	// 씬 전환은 마을↔필드 · 마을↔던전 · 필드↔던전 세 경계에서만 일어난다.
	// 액트 전환과 던전 다음 단계는 씬을 그대로 두고 그리드맵만 교체하지만,
	// 로딩창은 똑같이 띄우므로 같은 흐름 enum 을 재사용한다.
	public enum LoadingFlow
	{
		ToTown,      // 타이틀→마을 (패치+서버데이터+씬+준비)
		ReturnTown,  // 필드/던전→마을 복귀 (씬+준비)
		ToField,     // 마을/던전→필드, 액트 전환 (씬 또는 맵 로드+준비)
		ToDungeon,   // 마을/필드→던전, 다음 단계 (씬 또는 맵 로드+준비)
	}
}
