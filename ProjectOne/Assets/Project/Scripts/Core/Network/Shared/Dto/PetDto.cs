using System.Collections.Generic;

namespace ProjectOne.Shared
{
	// 펫 1마리 저장 DTO.
	//
	// 펫은 개별 인스턴스가 없다 — 같은 펫을 두 마리 가질 수 없으므로 uid 를 채번하지 않고
	// 테이블 ID 를 키로 쓴다. 상태는 강화 레벨과 등급 둘이면 전부 표현된다.
	[System.Serializable]
	public class PetEntryDto
	{
		public int petId;		// EDT.Pet

		// 보유 = 최소 1레벨
		public int level = 1;

		// EDT.ItemGradeType — enum 은 int 로 싣는다(기존 DTO 규약)
		public int grade;
	}

	// 펫 보유·장착 저장 DTO.
	// JsonUtility 가 Dictionary 를 다루지 못해 리스트로 편다.
	[System.Serializable]
	public class PetDto
	{
		public List<PetEntryDto> pets = new List<PetEntryDto>();

		// 0 = 미장착. 펫은 동시에 한 마리만 장착한다.
		public int equippedPetId;
	}
}
