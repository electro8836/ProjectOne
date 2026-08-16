using EDT;

namespace ProjectOne.Unit.Stats
{
	// 버프/아이템이 들고 있다가 제거 시 그대로 RemoveModifier에 전달하는 핸들
	// Source: 출처 태그 (장비/버프/트리 등). 빈 문자열이면 소속 없음 — RemoveAllFromSource 대상에서 제외
	public sealed class StatModifier
	{
		public readonly Stat Group;
		public readonly StatDetailTypes Kind;
		public readonly float Value;
		public readonly string Source;

		public StatModifier(Stat group, StatDetailTypes kind, float value)
			: this(group, kind, value, string.Empty)
		{
		}

		public StatModifier(Stat group, StatDetailTypes kind, float value, string source)
		{
			Group = group;
			Kind = kind;
			Value = value;
			Source = source ?? string.Empty;
		}
	}
}
