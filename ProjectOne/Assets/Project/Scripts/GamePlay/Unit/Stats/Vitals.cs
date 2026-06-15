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
		public float Stamina;

		// 브레이크 상태(게이지 0) 여부 — true이면 내부 회복 풀로만 회복
		public bool IsBreak { get; private set; }

		// 브레이크 상태 회복 내부 누적 변수 — 최대치 도달 시 브레이크 해제
		private float _breakRecoverPool;

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
			IsBreak = false;
			_breakRecoverPool = 0f;
		}

		public void ModifyHp(float delta)
		{
			Hp = Mathf.Clamp(Hp + delta, 0f, _stats.GetStat(StatInfo.MaxHP));
		}

		public void InitStamina()
		{
			Stamina = _stats.GetStat(StatInfo.MaxStamina);
		}

		public void ModifyStamina(float delta)
		{
			Stamina = Mathf.Clamp(Stamina + delta, 0f, _stats.GetStat(StatInfo.MaxStamina));
		}

		public void ModifyBreakGage(float delta)
		{
			BreakGage = Mathf.Clamp(BreakGage + delta, 0f, _stats.GetStat(StatInfo.BreakGage));
		}

		// 외부(아이템/능력 등) 단일 제어 진입점. ratio 0~1 (0 = 브레이크 상태 진입, 1 = 만충)
		// - 0 이하: 브레이크 상태 진입, 내부 회복 풀 초기화
		// - 0 초과: 브레이크 상태 해제, 일반 자동 회복으로 전환
		public void SetBreakGage(float ratio)
		{
			float max = _stats.GetStat(StatInfo.BreakGage);
			float value = max * Mathf.Clamp01(ratio);
			if (value <= 0f)
			{
				IsBreak = true;
				_breakRecoverPool = 0f;
				BreakGage = 0f;
			}
			else
			{
				IsBreak = false;
				_breakRecoverPool = 0f;
				BreakGage = value;
			}
		}

		// UnitBase LateUpdate에서 호출 — 브레이크 상태 회복 누적, 최대치 도달 시 자동 해제
		public void TickBreakRecover(float amount)
		{
			float max = _stats.GetStat(StatInfo.BreakGage);
			_breakRecoverPool += amount;
			if (_breakRecoverPool >= max)
			{
				IsBreak = false;
				_breakRecoverPool = 0f;
				BreakGage = max;
			}
		}

		public bool IsHpZero        => Hp <= 0f;
		public bool IsBreakGageZero => BreakGage <= 0f;
	}
}
