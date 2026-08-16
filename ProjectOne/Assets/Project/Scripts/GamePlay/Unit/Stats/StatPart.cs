using EDT;

namespace ProjectOne.Unit.Stats
{
	// StatDetail 이 어느 스탯의 어느 레이어인지 가리키는 참조.
	// (예: StatDetail_Atk_Ratio → Group=Stat_Atk, Kind=Ratio)
	// 구버전은 enum 이름의 접미사를 문자열로 파싱했으나, 이제 Table_StatDetail 이 두 컬럼으로 직접 알려준다.
	public readonly struct StatPart
	{
		public readonly Stat Group;
		public readonly StatDetailTypes Kind;

		public StatPart(Stat group, StatDetailTypes kind)
		{
			Group = group;
			Kind = kind;
		}
	}
}
