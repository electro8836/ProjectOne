namespace ProjectOne.Pets
{
	// 강화가 막힌 이유. 선언 순서가 곧 안내 우선순위다.
	//
	// 판정을 두 벌 두면 규칙이 바뀔 때 버튼과 실제 동작이 조용히 갈라진다 —
	// 버튼 잠금도 실행도 PetBook.GetEnhanceBlock 하나만 본다 (MasteryProgress.InvestBlock 과 같은 규약).
	public enum PetEnhanceBlock
	{
		None = 0,
		NotOwned,	// 미보유 — 팝업 자체가 열리지 않지만 방어
		MaxLevel,	// 현재 등급의 최대 레벨 — 승급해야 더 올라간다
		NoTable,	// 이 레벨 구간의 PetEnhance 행이 없다(데이터 오류)
		NotEnough	// 재화 부족 — 이때만 비용 문구를 빨갛게 한다
	}

	// 승급이 막힌 이유.
	// 레벨 조건은 두지 않는다 — PetPromotion 테이블에 없는 규칙을 코드가 만들지 않는다.
	public enum PetPromoteBlock
	{
		None = 0,
		NotOwned,
		MaxGrade,	// 더 올라갈 등급이 없다
		NoTable,
		NotEnough
	}
}
