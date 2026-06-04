using UnityEngine;
using EDT;

namespace ProjectOne.Unit.Stats
{
	// 실시간 변동 게이지(HP, BreakGage 등) 보관 POCO
	// - Max는 StatContainer에서 조회, 현재값만 여기서 관리
	// - 게이지 종류가 늘어나면 이 파일에 필드+헬퍼 한 쌍 추가
	public sealed class Vitals
	{
		readonly StatContainer _stats;

		public float Hp;
		public float BreakGage;

		public Vitals(StatContainer stats)
		{
			_stats = stats;
		}

		public void InitHp()
		{
			Hp = _stats.GetStat(StatInfo.MaxHP);
		}

		public void InitBreakGage()
		{
			BreakGage = _stats.GetStat(StatInfo.BreakGage);
		}

		public void ModifyHp(float delta)
		{
			Hp = Mathf.Clamp(Hp + delta, 0f, _stats.GetStat(StatInfo.MaxHP));
		}

		public void ModifyBreakGage(float delta)
		{
			BreakGage = Mathf.Clamp(BreakGage + delta, 0f, _stats.GetStat(StatInfo.BreakGage));
		}

		public bool IsHpZero        => Hp <= 0f;
		public bool IsBreakGageZero => BreakGage <= 0f;
	}
}
