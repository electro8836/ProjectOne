using ProjectOne.ServerData;

namespace ProjectOne.UserData
{
	// 스킬정보(인게임용) 도메인 모델 — 데이터 보관만. 로직은 추후.
	public sealed class SkillBook
	{
		private readonly SkillData _data;

		public SkillBook(SkillData data)
		{
			_data = (data != null) ? data : new SkillData();
		}
	}
}
