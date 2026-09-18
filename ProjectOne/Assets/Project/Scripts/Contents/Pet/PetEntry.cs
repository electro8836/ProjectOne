using EDT;
using ProjectOne.Shared;

namespace ProjectOne.Pets
{
	// 보유한 펫 1마리의 상태.
	//
	// **보유한 펫만 인스턴스가 생긴다** — 미보유는 항목 자체가 없다(MasteryProgress 의 노드 규약과 같다).
	// 저장하는 것은 레벨과 등급 둘뿐이고, 최대 레벨·옵션 수치·비용은 전부 테이블에서 계산한다.
	public sealed class PetEntry
	{
		public readonly Pet id;

		// 보유 = 최소 1레벨. 0 레벨은 존재하지 않는다.
		private int _level = 1;

		// 최초에는 Table_Pet.Grade, 이후 승급으로만 오른다.
		private ItemGradeType _grade = ItemGradeType.None;

		public PetEntry(Pet petId, ItemGradeType grade)
		{
			id = petId;
			_grade = grade;
		}

		public int Level
		{
			get { return _level; }
		}

		public ItemGradeType Grade
		{
			get { return _grade; }
		}

		// ── 파생값 (저장하지 않는다) ──────────────────────────────────

		// 현재 등급이 허용하는 최대 레벨. 비용 구간과 달리 이쪽은 등급이 정한다.
		public int MaxLevel
		{
			get { return PetCatalog.GetMaxLevel(_grade); }
		}

		public bool IsMaxLevel
		{
			get
			{
				int maxLevel = MaxLevel;
				return maxLevel > 0 && _level >= maxLevel;
			}
		}

		// 현재 레벨의 보유 효과 수치. 1레벨이 기준값이다 (MasteryProgress 의 노드 값 계산과 같은 규칙).
		public float OptionValue
		{
			get { return GetOptionValue(PetCatalog.Get(id), _level); }
		}

		public static float GetOptionValue(Table_Pet.Row row, int level)
		{
			if (row == null || level <= 0)
			{
				return 0f;
			}

			return row.Opt_Val + row.Opt_Step * (level - 1);
		}

		// ── 변경 (PetBook 만 부른다) ──────────────────────────────────

		internal void LevelUp()
		{
			_level++;
		}

		internal void Promote(ItemGradeType to)
		{
			_grade = to;
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public void LoadFrom(PetEntryDto dto)
		{
			if (dto == null)
			{
				return;
			}

			// 보유 = 최소 1레벨이라는 불변식을 여기서 지킨다 (서버 데이터 불일치 방어).
			_level = (dto.level < 1) ? 1 : dto.level;

			ItemGradeType grade = (ItemGradeType)dto.grade;
			if (grade == ItemGradeType.None)
			{
				// 등급이 비었으면 테이블의 기본 등급으로 되돌린다.
				Table_Pet.Row row = PetCatalog.Get(id);
				grade = (row != null) ? row.Grade : ItemGradeType.Normal;
			}

			_grade = grade;
		}

		public PetEntryDto ToDto()
		{
			PetEntryDto dto = new PetEntryDto();
			dto.petId = (int)id;
			dto.level = _level;
			dto.grade = (int)_grade;
			return dto;
		}
	}
}
