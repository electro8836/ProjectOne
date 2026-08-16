using System;
using System.Globalization;
using EDT;
using UnityEngine;

namespace ProjectOne.Skill
{
	// SkillEffect 의 EffectParam_1~5(전부 문자열)를 강타입으로 파싱한다.
	//
	// 슬롯 번호가 아니라 ParamKey(이름)로 값을 꺼낸다. 슬롯 번호는 Table_SkillParamDef 가 알고 있고,
	// 모디파이어도 이름으로 슬롯을 지목하므로 코드가 번호를 직접 아는 순간 두 곳이 어긋난다.
	//
	// 파싱 실패는 에러 로그 후 false — 해당 효과는 적용하지 않는다.
	// (경고로 두면 오타 하나로 옵션이 조용히 안 먹는다 — 설계 15.1)

	// Damage — 비례 계수 + 고정값 + 다단히트
	public struct DamageParams
	{
		public float Ratio;
		public float FlatValue;
		public int HitCount;
		public float HitInterval;
		public Stat ScaleStat;
	}

	// Heal — 지속 회복(TickCount/TickInterval) 포함
	public struct HealParams
	{
		public float Ratio;
		public float FlatValue;
		public int TickCount;
		public float TickInterval;
		public Stat ScaleStat;
	}

	// Buff — 지속시간·중첩은 버프가 아니라 부여하는 쪽이 정한다 (설계 8.3)
	public struct BuffParams
	{
		public EDT.Buff RefID;
		public float Duration;
		public int StackMax;
		public float Ratio;
		public float Chance;
	}

	// StatChange — 패시브·오라의 스탯 변경. Duration 0 = 영구
	public struct StatChangeParams
	{
		public StatDetail StatDetailID;
		public float Value;
		public float Duration;
	}

	public struct ProjectileParams
	{
		public EDT.Projectile RefID;
		public int Count;
		public float Angle;
		public float Interval;
		public float SpeedRate;
	}

	public struct SummonParams
	{
		public EDT.Summon RefID;
		public int Count;
		public float Duration;
		public float Radius;
	}

	// Force — 밀기/당기기. 반드시 ChainEffectIDs 로 연결한다 (설계 5.6)
	public struct ForceParams
	{
		public float Power;
		public float Duration;
		public ForceType ForceType;
	}

	public struct CooldownReduceParams
	{
		public EDT.Skill TargetSkillID;
		public float Ratio;
		public float FlatValue;
	}

	public struct BuffConsumeParams
	{
		public EDT.Buff RefID;
		public int Count;
	}

	public static class SkillEffectParams
	{
		// ── 타입별 파싱 ───────────────────────────────────────────────

		public static bool TryParseDamage(Table_SkillEffect.Row row, out DamageParams p)
		{
			p = default(DamageParams);
			if (row == null)
			{
				return false;
			}

			p.Ratio = readFloat(row, "Ratio", 0f);
			p.FlatValue = readFloat(row, "FlatValue", 0f);
			p.HitCount = readInt(row, "HitCount", 1);
			p.HitInterval = readFloat(row, "HitInterval", 0f);
			p.ScaleStat = readEnum(row, "ScaleStat", Stat.None);

			if (p.HitCount < 1)
			{
				p.HitCount = 1;
			}

			return true;
		}

		public static bool TryParseHeal(Table_SkillEffect.Row row, out HealParams p)
		{
			p = default(HealParams);
			if (row == null)
			{
				return false;
			}

			p.Ratio = readFloat(row, "Ratio", 0f);
			p.FlatValue = readFloat(row, "FlatValue", 0f);
			p.TickCount = readInt(row, "TickCount", 1);
			p.TickInterval = readFloat(row, "TickInterval", 0f);
			p.ScaleStat = readEnum(row, "ScaleStat", Stat.None);

			if (p.TickCount < 1)
			{
				p.TickCount = 1;
			}

			return true;
		}

		public static bool TryParseBuff(Table_SkillEffect.Row row, out BuffParams p)
		{
			p = default(BuffParams);
			if (row == null)
			{
				return false;
			}

			p.RefID = readEnum(row, "RefID", EDT.Buff.None);
			if (p.RefID == EDT.Buff.None)
			{
				logMissing(row, "RefID");
				return false;
			}

			p.Duration = readFloat(row, "Duration", 0f);
			p.StackMax = readInt(row, "StackMax", 1);
			p.Ratio = readFloat(row, "Ratio", 0f);

			// Chance 빈 칸은 0(=절대 안 걸림)이 아니라 확정 부여로 해석한다.
			// Reward.Chance 와 달리 "확률 0으로 봉인"을 표현할 필요가 없는 슬롯이다.
			string raw = SkillParamCatalog.GetRawParamByKey(row, "Chance");
			p.Chance = string.IsNullOrEmpty(raw) ? 1f : readFloat(row, "Chance", 1f);

			return true;
		}

		public static bool TryParseStatChange(Table_SkillEffect.Row row, out StatChangeParams p)
		{
			p = default(StatChangeParams);
			if (row == null)
			{
				return false;
			}

			p.StatDetailID = readEnum(row, "StatDetailID", StatDetail.None);
			if (p.StatDetailID == StatDetail.None)
			{
				logMissing(row, "StatDetailID");
				return false;
			}

			p.Value = readFloat(row, "Value", 0f);
			p.Duration = readFloat(row, "Duration", 0f);
			return true;
		}

		public static bool TryParseProjectile(Table_SkillEffect.Row row, out ProjectileParams p)
		{
			p = default(ProjectileParams);
			if (row == null)
			{
				return false;
			}

			p.RefID = readEnum(row, "RefID", EDT.Projectile.None);
			if (p.RefID == EDT.Projectile.None)
			{
				logMissing(row, "RefID");
				return false;
			}

			p.Count = readInt(row, "Count", 1);
			p.Angle = readFloat(row, "Angle", 0f);
			p.Interval = readFloat(row, "Interval", 0f);
			p.SpeedRate = readFloat(row, "SpeedRate", 1f);

			if (p.Count < 1)
			{
				p.Count = 1;
			}

			if (p.SpeedRate <= 0f)
			{
				p.SpeedRate = 1f;
			}

			return true;
		}

		public static bool TryParseSummon(Table_SkillEffect.Row row, out SummonParams p)
		{
			p = default(SummonParams);
			if (row == null)
			{
				return false;
			}

			p.RefID = readEnum(row, "RefID", EDT.Summon.None);
			if (p.RefID == EDT.Summon.None)
			{
				logMissing(row, "RefID");
				return false;
			}

			p.Count = readInt(row, "Count", 1);
			p.Duration = readFloat(row, "Duration", 0f);
			p.Radius = readFloat(row, "Radius", 0f);

			if (p.Count < 1)
			{
				p.Count = 1;
			}

			return true;
		}

		public static bool TryParseForce(Table_SkillEffect.Row row, out ForceParams p)
		{
			p = default(ForceParams);
			if (row == null)
			{
				return false;
			}

			p.Power = readFloat(row, "Power", 0f);
			p.Duration = readFloat(row, "Duration", 0f);
			p.ForceType = readEnum(row, "ForceType", ForceType.None);

			if (p.ForceType == ForceType.None)
			{
				logMissing(row, "ForceType");
				return false;
			}

			return true;
		}

		public static bool TryParseCooldownReduce(Table_SkillEffect.Row row, out CooldownReduceParams p)
		{
			p = default(CooldownReduceParams);
			if (row == null)
			{
				return false;
			}

			p.TargetSkillID = readEnum(row, "TargetSkillID", EDT.Skill.None);
			if (p.TargetSkillID == EDT.Skill.None)
			{
				logMissing(row, "TargetSkillID");
				return false;
			}

			p.Ratio = readFloat(row, "Ratio", 0f);
			p.FlatValue = readFloat(row, "FlatValue", 0f);
			return true;
		}

		public static bool TryParseBuffConsume(Table_SkillEffect.Row row, out BuffConsumeParams p)
		{
			p = default(BuffConsumeParams);
			if (row == null)
			{
				return false;
			}

			p.RefID = readEnum(row, "RefID", EDT.Buff.None);
			if (p.RefID == EDT.Buff.None)
			{
				logMissing(row, "RefID");
				return false;
			}

			p.Count = readInt(row, "Count", 1);
			if (p.Count < 1)
			{
				p.Count = 1;
			}

			return true;
		}

		// ── 슬롯 읽기 ─────────────────────────────────────────────────

		private static float readFloat(Table_SkillEffect.Row row, string paramKey, float fallback)
		{
			string raw = SkillParamCatalog.GetRawParamByKey(row, paramKey);
			if (isEmpty(raw) == true)
			{
				return fallback;
			}

			float value;
			if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false)
			{
				logParseError(row, paramKey, raw, "float");
				return fallback;
			}

			return value;
		}

		private static int readInt(Table_SkillEffect.Row row, string paramKey, int fallback)
		{
			string raw = SkillParamCatalog.GetRawParamByKey(row, paramKey);
			if (isEmpty(raw) == true)
			{
				return fallback;
			}

			int value;
			if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == false)
			{
				logParseError(row, paramKey, raw, "int");
				return fallback;
			}

			return value;
		}

		private static T readEnum<T>(Table_SkillEffect.Row row, string paramKey, T fallback) where T : struct
		{
			string raw = SkillParamCatalog.GetRawParamByKey(row, paramKey);
			if (isEmpty(raw) == true)
			{
				return fallback;
			}

			T value;
			if (Enum.TryParse<T>(raw, out value) == false)
			{
				logParseError(row, paramKey, raw, typeof(T).Name);
				return fallback;
			}

			return value;
		}

		// 빈 칸은 언제나 None 이다. 엑셀에서 "None" 을 직접 적은 경우도 같게 취급한다.
		private static bool isEmpty(string raw)
		{
			return string.IsNullOrEmpty(raw) == true || raw == "None";
		}

		private static void logParseError(Table_SkillEffect.Row row, string paramKey, string raw, string typeName)
		{
			Debug.LogError($"[SkillEffectParams] 파싱 실패 — Effect:{row.ID} Type:{row.EffectType} {paramKey}=\"{raw}\" (기대 타입 {typeName})");
		}

		private static void logMissing(Table_SkillEffect.Row row, string paramKey)
		{
			Debug.LogError($"[SkillEffectParams] 필수 파라미터 누락 — Effect:{row.ID} Type:{row.EffectType} {paramKey}");
		}
	}
}
