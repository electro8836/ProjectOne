using System;
using System.Globalization;
using UnityEngine;
using EDT;

namespace ProjectOne.Skill
{
	// SkillEffect.Row 의 EffectParam_1~7 (string) 을 EffectType별 강타입 struct로 파싱
	// 컨벤션:
	//   Damage             : P1=기본데미지(float), P2=계수 속성(StatTypes 이름), P3=계수값(float)
	//   ActivateBuff       : P1=BuffInfo ID(enum 이름), P2=지속시간 sec(0=무한), P3=발동 간격 sec
	//   DeactivateBuff     : P1=BuffInfo ID
	//   IncreaseAttribute  : P1=StatTypes 이름(_Add/_Ratio/_Amp), P2=증가 수치 (_Ratio/_Amp 는 퍼센트 입력 100=100%, _Add 는 절대값)
	//   DecreaseAttribute  : P1=StatTypes 이름(_Add/_Ratio/_Amp), P2=감소 수치 (_Ratio/_Amp 는 퍼센트 입력 100=100%, _Add 는 절대값)
	//   ActivateAura       : P1=Aura ID, P2=지속시간 (현재 stub)

	public struct DamageParams
	{
		public float BaseDamage;
		public StatTypes CoefStat;   // None 이면 계수 없음
		public float CoefValue;
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
		public StatTypes AttrType;
		public float Value;
	}

	public static class SkillEffectParams
	{
		public static bool TryParseDamage(Table_SkillEffect.Row row, out DamageParams p)
		{
			p = new DamageParams();
			if (TryParseFloat(row.EffectParam_1, out float baseDam) == false)
			{
				LogParseError(row, 1, "Damage.BaseDamage");
				return false;
			}

			p.BaseDamage = baseDam;

			// P2/P3 는 선택 — 계수 없으면 None
			if (string.IsNullOrEmpty(row.EffectParam_2) == true || row.EffectParam_2 == "None")
			{
				p.CoefStat = StatTypes.None;
				p.CoefValue = 0f;
				return true;
			}

			if (Enum.TryParse(row.EffectParam_2, out StatTypes stat) == false)
			{
				LogParseError(row, 2, "Damage.CoefStat (StatTypes)");
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
			if (Enum.TryParse(row.EffectParam_1, out StatTypes stat) == false)
			{
				LogParseError(row, 1, "Attribute.StatTypes");
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

		// _Ratio/_Amp 파트는 퍼센트 입력 → 분수 변환 대상 (BuildPartMap 의 suffix 분류와 동일 규칙)
		static bool IsPercentInput(StatTypes stat)
		{
			string n = stat.ToString();
			return n.EndsWith("_Ratio") == true || n.EndsWith("_Amp") == true;
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
