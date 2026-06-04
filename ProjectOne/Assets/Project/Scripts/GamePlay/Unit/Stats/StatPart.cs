using EDT;

namespace ProjectOne.Unit.Stats
{
	// StatInfo 키가 어느 그룹의 어느 파트인지 가리키는 참조
	// (예: ATK_Base → Group=ATK, Kind=Base / ATK → Group=ATK, Kind=Final)
	public readonly struct StatPart
	{
		public readonly StatInfo Group;
		public readonly ModifierTypes Kind;

		public StatPart(StatInfo group, ModifierTypes kind)
		{
			Group = group;
			Kind = kind;
		}
	}
}
