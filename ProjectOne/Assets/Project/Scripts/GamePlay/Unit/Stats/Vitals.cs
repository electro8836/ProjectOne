using UnityEngine;
using EDT;

namespace ProjectOne.Unit.Stats
{
	// 실시간 변동 상태값 보관 POCO.
	// 신규 설계에서 유닛의 상태값은 CurrentHp 하나뿐이다 (기반테이블 5.1).
	// Stat_MaxHp 는 계산되는 값이고 Hp 는 소모·회복되는 값이라 층위를 분리해 둔다.
	public sealed class Vitals
	{
		readonly StatContainer _stats;

		public float Hp;

		public Vitals(StatContainer stats)
		{
			_stats = stats;
		}

		public void InitHp()
		{
			Hp = _stats.GetStat(Stat.Stat_MaxHp);
		}

		public void ModifyHp(float delta)
		{
			Hp = Mathf.Clamp(Hp + delta, 0f, _stats.GetStat(Stat.Stat_MaxHp));
		}

		// 최대 체력이 변할 때 현재 비율을 유지한다 (기반테이블 5.2).
		// 차분 가산(Hp += ΔMaxHp)은 최대체력 장비를 반복 착·탈하면 체력이 회복되는 악용이 가능하다.
		// 호출자가 변경 "직전"의 최대치를 넘겨야 한다.
		public void RescaleToNewMaxHp(float previousMaxHp)
		{
			float newMax = _stats.GetStat(Stat.Stat_MaxHp);
			if (previousMaxHp <= 0f || newMax <= 0f)
			{
				Hp = newMax;
				return;
			}

			float ratio = Hp / previousMaxHp;
			float value = Mathf.Round(newMax * ratio);

			// 최대 체력이 줄면 현재 체력도 줄지만, 이 때문에 사망하지는 않는다(하한 1).
			Hp = Mathf.Clamp(value, 1f, newMax);
		}

		// 전체 회복 — 던전 입장 / 마을 귀환 / 부활 / 재접속.
		public void FullHeal()
		{
			Hp = _stats.GetStat(Stat.Stat_MaxHp);
		}

		public bool IsHpZero => Hp <= 0f;
	}
}
