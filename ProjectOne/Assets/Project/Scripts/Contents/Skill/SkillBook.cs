using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 스킬정보(인게임용) 도메인 모델 — 데이터 보관만. 로직은 추후.
	// 공유 DTO(SkillDto)를 받아 변환 보유하고, 저장/전송 시 ToDto() 로 역변환한다.
	public sealed class SkillBook
	{
		public SkillBook(SkillDto dto)
		{
			// 현재 SkillDto 는 빈 스키마 — 변환할 필드 없음. 추후 추가.
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public SkillDto ToDto()
		{
			return new SkillDto();
		}
	}
}
