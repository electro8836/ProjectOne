using System;
using System.Globalization;
using UnityEngine;
using EDT;

namespace ProjectOne.Skill
{
	// SkillEffect.Row 의 EffectParam_1~7 (string) 을 EffectType별 강타입 struct로 파싱
	// 컨벤션:
	//   Damage             : P1=기본데미지(float), P2=계수 속성(StatInfo 이름), P3=계수값(float), P4=브레이크 게이지 데미지 비율(공격자 BreakDamage 배수, 1=100%, 0=없음 / HP 데미지와 무관), P5=넉백 힘 방향·비율(-/+, 0=없음)
	//   ActivateBuff       : P1=BuffInfo ID(enum 이름), P2=지속시간 sec(0=무한), P3=발동 간격 sec
	//   DeactivateBuff     : P1=BuffInfo ID
	//   IncreaseAttribute  : P1=StatInfo 이름(_Add/_Ratio/_Amp), P2=증가 수치 (_Ratio/_Amp 는 퍼센트 입력 100=100%, _Add 는 절대값)
	//   DecreaseAttribute  : P1=StatInfo 이름(_Add/_Ratio/_Amp), P2=감소 수치 (_Ratio/_Amp 는 퍼센트 입력 100=100%, _Add 는 절대값)
	//   ActivateAura       : P1=Aura ID, P2=지속시간 (현재 stub)

	public struct DamageParams
	{
		public float BaseDamage;
		public StatInfo CoefStat;   // None 이면 계수 없음
		public float CoefValue;
		public float BreakDamageRatio; // 0=없음, 공격자 BreakDamage 스탯에 곱할 비율 (브레이크 게이지 데미지, 1=100%, HP 데미지와 무관)
		public float KnockbackRatio; // 0=없음, +는 밀어내기, -는 당기기 (시전자 넉백 파워에 곱할 비율)
	}

	public struct ActivateBuffParams
	{
		public BuffInfo BuffID;
		public float Duration;
		public float Interval;
	}

	public struct DeactivateBuffParams
	{
		public BuffInfo BuffID;
	}

	public struct AttributeParams
	{
		public StatInfo AttrType;
		public float Value;
	}

	public struct AuraParams
	{
		public AuraInfo AuraId;
		public float Duration; // 0 = 무한
	}

	public struct SpawnProjectileParams
	{
		public int Count;            // 발사체 개수 (>=1)
		public float AngleStep;      // 이웃 발 사이 간격(도) — 타겟 방향 중심 좌우 대칭 분산
		public string Prefab;        // 발사체 프리팹 주소
		public SkillEffect HitEffect; // 적중 시 적용할 효과
		public float HitRadius;      // 임팩트 AoE 반경 (0=단일 타겟, >0=적중/마지막 위치 원형 범위)
	}

	public static class SkillEffectParams
	{
		public static bool TryParseDamage(Table_SkillEffect.Row row, out DamageParams p)
		{
			p = new DamageParams();
			if (string.IsNullOrEmpty(row.EffectParam_1) == true || row.EffectParam_1 == "None")
			{
				p.BaseDamage = 0f;
			}
			else if (TryParseFloat(row.EffectParam_1, out float baseDam) == false)
			{
				LogParseError(row, 1, "Damage.BaseDamage");
				return false;
			}
			else
			{
				p.BaseDamage = baseDam;
			}

			// P4 브레이크 게이지 데미지 비율 — 선택 (비어있거나 "None" 이면 0). 게이지 감소 = 공격자 BreakDamage × 비율 (HP 무관)
			p.BreakDamageRatio = 0f;
			if (string.IsNullOrEmpty(row.EffectParam_4) == false && row.EffectParam_4 != "None")
			{
				if (TryParseFloat(row.EffectParam_4, out float breakRatio) == false)
				{
					LogParseError(row, 4, "Damage.BreakDamageRatio");
					return false;
				}

				p.BreakDamageRatio = breakRatio;
			}

			// P5 넉백 힘 방향·비율 — 선택 (비어있거나 "None" 이면 0). 음수 허용(당기기)
			p.KnockbackRatio = 0f;
			if (string.IsNullOrEmpty(row.EffectParam_5) == false && row.EffectParam_5 != "None")
			{
				if (TryParseFloat(row.EffectParam_5, out float kbRatio) == false)
				{
					LogParseError(row, 5, "Damage.KnockbackRatio");
					return false;
				}

				p.KnockbackRatio = kbRatio;
			}

			// P2/P3 는 선택 — 계수 없으면 None
			if (string.IsNullOrEmpty(row.EffectParam_2) == true || row.EffectParam_2 == "None")
			{
				p.CoefStat = StatInfo.None;
				p.CoefValue = 0f;
				return true;
			}

			if (Enum.TryParse(row.EffectParam_2, out StatInfo stat) == false)
			{
				LogParseError(row, 2, "Damage.CoefStat (StatInfo)");
				return false;
			}

			p.CoefStat = stat;

			if (TryParseFloat(row.EffectParam_3, out float coef) == false)
			{
				LogParseError(row, 3, "Damage.CoefValue");
				return false;
			}

			p.CoefValue = coef;
			return true;
		}

		public static bool TryParseActivateBuff(Table_SkillEffect.Row row, out ActivateBuffParams p)
		{
			p = new ActivateBuffParams();
			if (Enum.TryParse(row.EffectParam_1, out BuffInfo buffId) == false)
			{
				LogParseError(row, 1, "ActivateBuff.BuffID");
				return false;
			}

			p.BuffID = buffId;

			// Duration 은 선택 — 비어있거나 "None" 이면 0 (무한 지속)
			if (string.IsNullOrEmpty(row.EffectParam_2) == true || row.EffectParam_2 == "None")
			{
				p.Duration = 0f;
			}
			else
			{
				if (TryParseFloat(row.EffectParam_2, out float duration) == false)
				{
					LogParseError(row, 2, "ActivateBuff.Duration");
					return false;
				}

				p.Duration = duration;
			}

			// Interval 은 선택 — 비어있으면 0
			if (string.IsNullOrEmpty(row.EffectParam_3) == true)
			{
				p.Interval = 0f;
			}
			else
			{
				if (TryParseFloat(row.EffectParam_3, out float interval) == false)
				{
					LogParseError(row, 3, "ActivateBuff.Interval");
					return false;
				}

				p.Interval = interval;
			}

			return true;
		}

		public static bool TryParseDeactivateBuff(Table_SkillEffect.Row row, out DeactivateBuffParams p)
		{
			p = new DeactivateBuffParams();
			if (Enum.TryParse(row.EffectParam_1, out BuffInfo buffId) == false)
			{
				LogParseError(row, 1, "DeactivateBuff.BuffID");
				return false;
			}

			p.BuffID = buffId;
			return true;
		}

		public static bool TryParseAttribute(Table_SkillEffect.Row row, out AttributeParams p)
		{
			p = new AttributeParams();
			if (Enum.TryParse(row.EffectParam_1, out StatInfo stat) == false)
			{
				LogParseError(row, 1, "Attribute.StatInfo");
				return false;
			}

			p.AttrType = stat;

			if (TryParseFloat(row.EffectParam_2, out float v) == false)
			{
				LogParseError(row, 2, "Attribute.Value");
				return false;
			}

			// 테이블은 퍼센트(100=100%) 입력 — _Ratio/_Amp 는 분수로 변환, _Add 는 절대값 유지
			if (IsPercentInput(stat) == true)
			{
				v = v / 100f;
			}

			p.Value = v;
			return true;
		}

		// ActivateAura : P1=AuraInfo ID(enum 이름, 필수), P2=지속시간 sec(빈값/"None"=0=무한)
		public static bool TryParseActivateAura(Table_SkillEffect.Row row, out AuraParams p)
		{
			p = new AuraParams();
			if (Enum.TryParse(row.EffectParam_1, out AuraInfo auraId) == false)
			{
				LogParseError(row, 1, "ActivateAura.AuraId");
				return false;
			}

			p.AuraId = auraId;

			// Duration 은 선택 — 비어있거나 "None" 이면 0 (무한 지속)
			if (string.IsNullOrEmpty(row.EffectParam_2) == true || row.EffectParam_2 == "None")
			{
				p.Duration = 0f;
			}
			else
			{
				if (TryParseFloat(row.EffectParam_2, out float duration) == false)
				{
					LogParseError(row, 2, "ActivateAura.Duration");
					return false;
				}

				p.Duration = duration;
			}

			return true;
		}

		// SpawnProjectile : P1=개수(int, 기본 1), P2=각도 간격(float, 기본 0), P3=발사체 프리팹 주소(필수), P4=적중 효과 SkillEffect(필수), P5=임팩트 AoE 반경(float, 기본 0=단일)
		public static bool TryParseSpawnProjectile(Table_SkillEffect.Row row, out SpawnProjectileParams p)
		{
			p = new SpawnProjectileParams();

			// P1 개수 — 선택(기본 1), 1 미만 불가
			int count = 1;
			if (string.IsNullOrEmpty(row.EffectParam_1) == false && row.EffectParam_1 != "None")
			{
				if (int.TryParse(row.EffectParam_1, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) == false || count < 1)
				{
					LogParseError(row, 1, "SpawnProjectile.Count");
					return false;
				}
			}

			p.Count = count;

			// P2 각도 간격 — 선택(기본 0 = 전부 같은 방향)
			p.AngleStep = 0f;
			if (string.IsNullOrEmpty(row.EffectParam_2) == false && row.EffectParam_2 != "None")
			{
				if (TryParseFloat(row.EffectParam_2, out float step) == false)
				{
					LogParseError(row, 2, "SpawnProjectile.AngleStep");
					return false;
				}

				p.AngleStep = step;
			}

			// P3 발사체 프리팹 주소 — 필수
			if (string.IsNullOrEmpty(row.EffectParam_3) == true || row.EffectParam_3 == "None")
			{
				LogParseError(row, 3, "SpawnProjectile.Prefab");
				return false;
			}

			p.Prefab = row.EffectParam_3;

			// P4 적중 효과 — 필수
			if (Enum.TryParse(row.EffectParam_4, out SkillEffect hitEffect) == false)
			{
				LogParseError(row, 4, "SpawnProjectile.HitEffect (SkillEffect)");
				return false;
			}

			p.HitEffect = hitEffect;

			// P5 임팩트 AoE 반경 — 선택(기본 0 = 단일 타겟). >0 이면 적중/마지막 위치 원형 범위 적용
			p.HitRadius = 0f;
			if (string.IsNullOrEmpty(row.EffectParam_5) == false && row.EffectParam_5 != "None")
			{
				if (TryParseFloat(row.EffectParam_5, out float radius) == false)
				{
					LogParseError(row, 5, "SpawnProjectile.HitRadius");
					return false;
				}

				p.HitRadius = radius;
			}

			return true;
		}

		// 퍼센트 입력(100=100%) → 분수 변환 대상 여부 — 테이블 IsRatio 사용
		static bool IsPercentInput(StatInfo stat)
		{
			Table_StatInfo.Row row = Table_StatInfo.Get(stat);
			return row != null && row.IsRatio == true;
		}

		static bool TryParseFloat(string s, out float value)
		{
			value = 0f;
			if (string.IsNullOrEmpty(s) == true)
			{
				return false;
			}

			return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		static void LogParseError(Table_SkillEffect.Row row, int paramIndex, string field)
		{
			Debug.LogError($"[SkillEffectParams] 파싱 실패 — Effect:{row.ID}, Param_{paramIndex}({field})");
		}
	}
}
