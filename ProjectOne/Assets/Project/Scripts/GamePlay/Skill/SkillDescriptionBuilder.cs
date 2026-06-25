using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EDT;

namespace ProjectOne.Skill
{
	// Table_SkillInfo / Table_SkillEffect / Table_BuffInfo 데이터를 조합해 스킬 설명 문장을 자동 생성한다.
	// 수치 해석은 전부 SkillEffectParams.TryParse* 를 재사용한다 (EffectParam_* 는 string 이므로 직접 파싱 금지).
	// 출력 = 개별 수치 라인(쿨타임·스태미너) + 효과 설명 문장 블록.
	public static class SkillDescriptionBuilder
	{
		// 버프 → 효과 → 버프 순환 / 발사체 적중효과 재귀 대비 깊이 제한.
		private const int MaxDepth = 4;

		public static string Build(SkillInfo skillId)
		{
			Table_SkillInfo.Row row = Table_SkillInfo.Get(skillId);
			if (row == null)
			{
				return string.Empty;
			}

			StringBuilder sb = new StringBuilder();

			string statLine = buildStatLine(row);
			if (string.IsNullOrEmpty(statLine) == false)
			{
				sb.AppendLine(statLine);
			}

			string castingPrefix = buildCastingPrefix(row.CastingType, row.CastingParam);
			string rangePhrase = buildRangePhrase(row.ScanType, row.ScanParam1, row.ScanParam2);

			List<string> sentences = new List<string>();
			collectEffectSentence(row.StartEffect, rangePhrase, sentences);
			collectEffectSentence(row.Effect_0, rangePhrase, sentences);
			collectEffectSentence(row.Effect_1, rangePhrase, sentences);
			collectEffectSentence(row.Effect_2, rangePhrase, sentences);
			collectEffectSentence(row.Effect_3, rangePhrase, sentences);
			collectEffectSentence(row.Effect_4, rangePhrase, sentences);
			collectEffectSentence(row.FinishEffect, rangePhrase, sentences);

			for (int i = 0; i < sentences.Count; i++)
			{
				string s = sentences[i];
				// 캐스팅/상시 접두는 첫 효과 문장에만 붙인다.
				if (i == 0 && string.IsNullOrEmpty(castingPrefix) == false)
				{
					s = castingPrefix + s;
				}

				sb.AppendLine(s);
			}

			return sb.ToString().TrimEnd();
		}

		private static void collectEffectSentence(SkillEffect effect, string rangePhrase, List<string> outList)
		{
			string s = describeEffect(effect, rangePhrase, 0);
			if (string.IsNullOrEmpty(s) == false)
			{
				outList.Add(s);
			}
		}

		// 하나의 SkillEffect 를 한 문장으로 설명한다. depth 는 버프/발사체 재귀 깊이.
		private static string describeEffect(SkillEffect effect, string rangePhrase, int depth)
		{
			if (effect == SkillEffect.None || depth > MaxDepth)
			{
				return string.Empty;
			}

			Table_SkillEffect.Row row = Table_SkillEffect.Get(effect);
			if (row == null)
			{
				return string.Empty;
			}

			switch (row.EffectType)
			{
				case SkillEffectTypes.Damage: return describeDamage(row, rangePhrase);
				case SkillEffectTypes.IncreaseAttribute: return describeAttribute(row, true);
				case SkillEffectTypes.DecreaseAttribute: return describeAttribute(row, false);
				case SkillEffectTypes.ActivateBuff: return describeActivateBuff(row, depth);
				case SkillEffectTypes.DeactivateBuff: return describeDeactivateBuff(row);
				case SkillEffectTypes.SpawnProjectile: return describeSpawnProjectile(row, depth);
				default: return fallbackName(row);
			}
		}

		private static string describeDamage(Table_SkillEffect.Row row, string rangePhrase)
		{
			if (SkillEffectParams.TryParseDamage(row, out DamageParams p) == false)
			{
				return fallbackName(row);
			}

			StringBuilder sb = new StringBuilder();
			if (string.IsNullOrEmpty(rangePhrase) == false)
			{
				sb.Append(rangePhrase);
			}

			string amount = buildDamageAmount(p);
			if (string.IsNullOrEmpty(amount) == false)
			{
				sb.Append(amount).Append("의 ");
			}

			string dmgType = SkillTextNames.DamageType(row.DamageType);
			if (string.IsNullOrEmpty(dmgType) == false)
			{
				sb.Append(dmgType).Append(" ");
			}

			if (p.KnockbackRatio > 0f)
			{
				sb.Append("데미지를 입히고 밀쳐낸다.");
			}
			else if (p.KnockbackRatio < 0f)
			{
				sb.Append("데미지를 입히고 끌어당긴다.");
			}
			else
			{
				sb.Append("데미지를 입힌다.");
			}

			if (p.BreakDamageRatio > 0f)
			{
				sb.Append(" 브레이크 게이지에 추가 데미지를 준다.");
			}

			return sb.ToString();
		}

		// 데미지 수치 표현: 기본데미지 + 계수(스탯*계수값) 조합.
		private static string buildDamageAmount(DamageParams p)
		{
			string coef = string.Empty;
			if (p.CoefStat != StatInfo.None)
			{
				coef = SkillTextNames.Stat(p.CoefStat) + "*" + formatNum(p.CoefValue);
			}

			if (p.BaseDamage > 0f && string.IsNullOrEmpty(coef) == false)
			{
				return formatNum(p.BaseDamage) + "+" + coef;
			}

			if (string.IsNullOrEmpty(coef) == false)
			{
				return coef;
			}

			if (p.BaseDamage > 0f)
			{
				return formatNum(p.BaseDamage);
			}

			return string.Empty;
		}

		private static string describeAttribute(Table_SkillEffect.Row row, bool isIncrease)
		{
			if (SkillEffectParams.TryParseAttribute(row, out AttributeParams p) == false)
			{
				return fallbackName(row);
			}

			string statName = SkillTextNames.Stat(p.AttrType);
			string value = formatAttributeValue(p);
			string target = targetPrefix(row.ApplyTarget);
			string verb = isIncrease ? "증가한다" : "감소한다";
			return $"{target}{statName}{subjectParticle(statName)} {value}만큼 {verb}.";
		}

		// 퍼센트 입력 스탯(_Ratio/_Amp)은 SkillEffectParams 에서 이미 /100 된 분수 → 표시 시 *100 + "%" 로 복원.
		private static string formatAttributeValue(AttributeParams p)
		{
			if (SkillTextNames.IsPercentStat(p.AttrType) == true)
			{
				return formatNum(p.Value * 100f) + "%";
			}

			return formatNum(p.Value);
		}

		private static string describeActivateBuff(Table_SkillEffect.Row row, int depth)
		{
			if (SkillEffectParams.TryParseActivateBuff(row, out ActivateBuffParams p) == false)
			{
				return fallbackName(row);
			}

			Table_BuffInfo.Row buff = Table_BuffInfo.Get(p.BuffID);
			string durationText = p.Duration > 0f ? formatNum(p.Duration) + "초 동안 " : "지속적으로 ";

			// 버프의 효과를 다시 풀어쓴다 — 즉시 효과 우선, 없으면 주기 효과.
			string inner = string.Empty;
			if (buff != null)
			{
				inner = describeEffect(buff.Effect, string.Empty, depth + 1);

				if (string.IsNullOrEmpty(inner) == true)
				{
					string interval = describeEffect(buff.IntervalEffect, string.Empty, depth + 1);
					if (string.IsNullOrEmpty(interval) == false)
					{
						string every = p.Interval > 0f ? formatNum(p.Interval) + "초마다 " : string.Empty;
						inner = every + interval;
					}
				}
			}

			if (string.IsNullOrEmpty(inner) == false)
			{
				return durationText + inner;
			}

			// 풀어쓸 효과가 없으면 버프 이름으로 표시 (예: 무적). 이름에 이미 "버프"가 들어가므로 조사만 붙인다.
			string buffName = buff != null && string.IsNullOrEmpty(buff.Name) == false ? buff.Name : p.BuffID.ToString();
			return durationText + buffName + subjectParticle(buffName) + " 적용된다.";
		}

		private static string describeDeactivateBuff(Table_SkillEffect.Row row)
		{
			if (SkillEffectParams.TryParseDeactivateBuff(row, out DeactivateBuffParams p) == false)
			{
				return fallbackName(row);
			}

			Table_BuffInfo.Row buff = Table_BuffInfo.Get(p.BuffID);
			string buffName = buff != null && string.IsNullOrEmpty(buff.Name) == false ? buff.Name : p.BuffID.ToString();
			return buffName + " 효과를 해제한다.";
		}

		private static string describeSpawnProjectile(Table_SkillEffect.Row row, int depth)
		{
			if (SkillEffectParams.TryParseSpawnProjectile(row, out SpawnProjectileParams p) == false)
			{
				return fallbackName(row);
			}

			StringBuilder sb = new StringBuilder();
			sb.Append(p.Count > 1 ? p.Count + "개의 발사체를 발사한다." : "발사체를 발사한다.");

			// 적중 시 효과를 풀어쓴다 (발사체 적중은 별도 스캔 범위가 없으므로 범위 구절 없이 설명).
			string hit = describeEffect(p.HitEffect, string.Empty, depth + 1);
			if (string.IsNullOrEmpty(hit) == false)
			{
				sb.Append(" 적중 시 ").Append(hit);
			}

			return sb.ToString();
		}

		private static string fallbackName(Table_SkillEffect.Row row)
		{
			return string.IsNullOrEmpty(row.Name) == false ? row.Name : string.Empty;
		}

		private static string buildStatLine(Table_SkillInfo.Row row)
		{
			List<string> parts = new List<string>();
			if (row.StaminaCost > 0)
			{
				parts.Add("스태미너 " + row.StaminaCost + " 소모");
			}

			if (row.CooltimeSec > 0f)
			{
				parts.Add("쿨타임 " + formatNum(row.CooltimeSec) + "초");
			}

			return string.Join(" · ", parts);
		}

		// 캐스팅/상시 접두. CastingParam 은 int — OnHit 확률은 정수 % 로 해석한다(소수 확률 입력 불가).
		private static string buildCastingPrefix(SkillCastingTypes castingType, int castingParam)
		{
			switch (castingType)
			{
				case SkillCastingTypes.Passive: return "[상시] ";
				case SkillCastingTypes.Casting: return castingParam + "초 캐스팅 후 ";
				case SkillCastingTypes.OnHitCaster: return "타격 시 " + castingParam + "% 확률로 ";
				case SkillCastingTypes.OnHitTarget: return "피격 시 " + castingParam + "% 확률로 ";
				default: return string.Empty;
			}
		}

		// 효과 문장에 끼워 쓸 범위 구절 (끝에 공백 포함). ScanParam 의미는 ScanType 별로 다르다.
		private static string buildRangePhrase(SkillScanType scanType, float p1, float p2)
		{
			switch (scanType)
			{
				case SkillScanType.Target: return "사정거리 " + formatNum(p1) + " 내 대상 " + formatNum(p2) + "명에게 ";
				case SkillScanType.Circle: return "반경 " + formatNum(p1) + " 원형 범위에 ";
				case SkillScanType.Sector: return "전방 " + formatNum(p2) + "도 반경 " + formatNum(p1) + " 부채꼴 범위에 ";
				case SkillScanType.Line: return "길이 " + formatNum(p1) + " 너비 " + formatNum(p2) + " 직선 범위에 ";
				case SkillScanType.Donut: return "외경 " + formatNum(p1) + " 내경 " + formatNum(p2) + " 도넛 범위에 ";
				default: return string.Empty;
			}
		}

		private static string targetPrefix(SkillApplyTarget target)
		{
			switch (target)
			{
				case SkillApplyTarget.Enemy: return "적의 ";
				case SkillApplyTarget.Friendly: return "아군의 ";
				case SkillApplyTarget.All: return "모든 대상의 ";
				default: return string.Empty;
			}
		}

		private static string formatNum(float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		// 주격 조사 선택 — 마지막 글자에 받침이 있으면 "이", 없으면(또는 비한글) "가".
		private static string subjectParticle(string word)
		{
			if (string.IsNullOrEmpty(word) == true)
			{
				return "가";
			}

			char c = word[word.Length - 1];
			if (c >= 0xAC00 && c <= 0xD7A3)
			{
				bool hasFinal = ((c - 0xAC00) % 28) != 0;
				return hasFinal ? "이" : "가";
			}

			return "가";
		}
	}
}
