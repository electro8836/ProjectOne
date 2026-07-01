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

		// ─────────────────────────────────────────────────────────────
		// 레벨별 요약 (특성 슬롯 LV1~5 = 별도 스킬 5개) — 효과 타입별로 필요한 데이터만 뽑아 압축 라인 생성.
		// 한 줄에 모든 산문을 만드는 Build() 와 달리, 상황(효과 타입)별 소형 추출 함수를 조합한다.
		// ─────────────────────────────────────────────────────────────

		// 레벨 1~5 스킬을 받아 레벨별 압축 요약 5줄을 순서대로 반환한다.
		// None 레벨은 빈 문자열. 첫 유효 레벨(L1) 기준으로 범위/타겟/투사체 증가분을 계산한다.
		public static string[] BuildLevelSummaries(SkillInfo lv1, SkillInfo lv2, SkillInfo lv3, SkillInfo lv4, SkillInfo lv5)
		{
			SkillInfo[] ids = { lv1, lv2, lv3, lv4, lv5 };
			Table_SkillInfo.Row[] rows = new Table_SkillInfo.Row[ids.Length];
			Table_SkillInfo.Row baseRow = null;
			for (int i = 0; i < ids.Length; i++)
			{
				rows[i] = ids[i] != SkillInfo.None ? Table_SkillInfo.Get(ids[i]) : null;
				if (baseRow == null && rows[i] != null)
				{
					baseRow = rows[i];
				}
			}

			string[] result = new string[ids.Length];
			for (int i = 0; i < rows.Length; i++)
			{
				result[i] = rows[i] != null ? buildLevelLine(rows[i], baseRow) : string.Empty;
			}

			return result;
		}

		// 한 레벨 스킬의 단편들을 정해진 순서로 모아 ", " 로 join.
		// 순서: 데미지 → 발동확률 → 속성/버프 → 쿨타임 → 범위증가 → 타겟증가 → 투사체증가.
		private static string buildLevelLine(Table_SkillInfo.Row row, Table_SkillInfo.Row baseRow)
		{
			List<string> parts = new List<string>();

			addIfNotEmpty(parts, damagePhrase(row));
			addIfNotEmpty(parts, procChancePhrase(row));
			attributePhrases(row, parts);
			buffPhrases(row, parts);
			addIfNotEmpty(parts, cooltimePhrase(row));
			addIfNotEmpty(parts, durationPhrase(row));
			addIfNotEmpty(parts, scanRangeDeltaPhrase(row, baseRow));
			addIfNotEmpty(parts, targetCountDeltaPhrase(row, baseRow));
			addIfNotEmpty(parts, projectileCountDeltaPhrase(row, baseRow));

			return string.Join(", ", parts);
		}

		private static void addIfNotEmpty(List<string> list, string s)
		{
			if (string.IsNullOrEmpty(s) == false)
			{
				list.Add(s);
			}
		}

		// 스킬의 7개 효과 슬롯을 순서대로 순회한다 (StartEffect → Effect_0~4 → FinishEffect).
		private static void forEachEffect(Table_SkillInfo.Row row, List<SkillEffect> outList)
		{
			addEffectExpandingAura(row.StartEffect, outList);
			addEffectExpandingAura(row.Effect_0, outList);
			addEffectExpandingAura(row.Effect_1, outList);
			addEffectExpandingAura(row.Effect_2, outList);
			addEffectExpandingAura(row.Effect_3, outList);
			addEffectExpandingAura(row.Effect_4, outList);
			addEffectExpandingAura(row.FinishEffect, outList);
		}

		// 효과를 리스트에 추가하되, ActivateAura 면 대상 오라(Table_AuraInfo)의 효과 슬롯(Effect_1~4)으로
		// 펼쳐 넣는다 — 오라 스킬은 슬롯에 ActivateAura 만 있고 실제 수치(데미지·속성)는 오라에 있으므로,
		// 이를 펼쳐야 레벨별 요약/추출(firstEffectOfType·attributePhrases 등)에 노출된다.
		private static void addEffectExpandingAura(SkillEffect effect, List<SkillEffect> outList)
		{
			if (effect != SkillEffect.None)
			{
				Table_SkillEffect.Row e = Table_SkillEffect.Get(effect);
				if (e != null && e.EffectType == SkillEffectTypes.ActivateAura && SkillEffectParams.TryParseActivateAura(e, out AuraParams p) == true)
				{
					Table_AuraInfo.Row aura = Table_AuraInfo.Get(p.AuraId);
					if (aura != null)
					{
						outList.Add(aura.Effect_1);
						outList.Add(aura.Effect_2);
						outList.Add(aura.Effect_3);
						outList.Add(aura.Effect_4);
						return;
					}
				}
			}

			outList.Add(effect);
		}

		// 지정 타입의 첫 효과 행을 반환 (없으면 null).
		private static Table_SkillEffect.Row firstEffectOfType(Table_SkillInfo.Row row, SkillEffectTypes type)
		{
			List<SkillEffect> effects = new List<SkillEffect>(7);
			forEachEffect(row, effects);
			for (int i = 0; i < effects.Count; i++)
			{
				if (effects[i] == SkillEffect.None)
				{
					continue;
				}

				Table_SkillEffect.Row e = Table_SkillEffect.Get(effects[i]);
				if (e != null && e.EffectType == type)
				{
					return e;
				}
			}

			return null;
		}

		// 데미지: 계수 퍼센트(CoefValue*100). 타입 라벨 prefix, BaseDamage 는 "고정 데미지 N" 으로 덧붙임.
		private static string damagePhrase(Table_SkillInfo.Row row)
		{
			Table_SkillEffect.Row e = firstEffectOfType(row, SkillEffectTypes.Damage);
			if (e == null || SkillEffectParams.TryParseDamage(e, out DamageParams p) == false)
			{
				return string.Empty;
			}

			string typeLabel = SkillTextNames.DamageType(e.DamageType);
			StringBuilder sb = new StringBuilder();
			if (p.CoefStat != StatInfo.None && p.CoefValue != 0f)
			{
				if (string.IsNullOrEmpty(typeLabel) == false)
				{
					sb.Append(typeLabel).Append(" ");
				}

				sb.Append("데미지 ").Append(formatNum(p.CoefValue * 100f)).Append("%");
			}

			if (p.BaseDamage > 0f)
			{
				if (sb.Length > 0)
				{
					sb.Append(", ");
				}

				sb.Append("고정 데미지 ").Append(formatNum(p.BaseDamage));
			}

			return sb.ToString();
		}

		private static string cooltimePhrase(Table_SkillInfo.Row row)
		{
			if (row.CooltimeSec > 0f)
			{
				return "쿨타임 " + formatNum(row.CooltimeSec) + "초";
			}

			return string.Empty;
		}

		// 버프 지속시간 — 첫 ActivateBuff 효과의 Duration. 영구 지속(Duration<=0)은 표기 생략.
		private static string durationPhrase(Table_SkillInfo.Row row)
		{
			Table_SkillEffect.Row e = firstEffectOfType(row, SkillEffectTypes.ActivateBuff);
			if (e == null || SkillEffectParams.TryParseActivateBuff(e, out ActivateBuffParams p) == false)
			{
				return string.Empty;
			}

			if (p.Duration > 0f)
			{
				return "지속시간 " + formatNum(p.Duration) + "초";
			}

			return string.Empty;
		}

		// 발동확률 — OnHit 계열(OnHitCaster/OnHitTarget) 일 때. 둘 다 CastingParam 을 확률 %로 사용.
		private static string procChancePhrase(Table_SkillInfo.Row row)
		{
			if (row.CastingType == SkillCastingTypes.OnHitCaster || row.CastingType == SkillCastingTypes.OnHitTarget)
			{
				return "발동확률 " + row.CastingParam + "%";
			}

			return string.Empty;
		}

		// 스캔 범위 증가분 — 비-Target 스캔의 ScanParam1(반경/길이/외경) L1 대비 증가율.
		private static string scanRangeDeltaPhrase(Table_SkillInfo.Row row, Table_SkillInfo.Row baseRow)
		{
			if (baseRow == null || row == baseRow || row.ScanType == SkillScanType.None || row.ScanType == SkillScanType.Target)
			{
				return string.Empty;
			}

			if (baseRow.ScanType != row.ScanType || baseRow.ScanParam1 <= 0f)
			{
				return string.Empty;
			}

			float delta = (row.ScanParam1 / baseRow.ScanParam1 - 1f) * 100f;
			if (delta <= 0f)
			{
				return string.Empty;
			}

			// 공격(Damage) 효과가 없는 순수 버프 스킬은 "공격" 을 빼고 "범위" 로만 표기.
			bool isAttack = firstEffectOfType(row, SkillEffectTypes.Damage) != null;
			string prefix = isAttack ? "공격 범위 +" : "범위 +";
			return prefix + formatNum(delta) + "%";
		}

		// 타겟 수 증가분 — Target 스캔의 ScanParam2(대상 수) L1 대비 증가분.
		private static string targetCountDeltaPhrase(Table_SkillInfo.Row row, Table_SkillInfo.Row baseRow)
		{
			if (baseRow == null || row == baseRow || row.ScanType != SkillScanType.Target)
			{
				return string.Empty;
			}

			float baseCount = baseRow.ScanType == SkillScanType.Target ? baseRow.ScanParam2 : 0f;
			float delta = row.ScanParam2 - baseCount;
			if (delta <= 0f)
			{
				return string.Empty;
			}

			return "대상 +" + formatNum(delta);
		}

		// 투사체 수 증가분 — 첫 SpawnProjectile 효과의 Count L1 대비 증가분.
		private static string projectileCountDeltaPhrase(Table_SkillInfo.Row row, Table_SkillInfo.Row baseRow)
		{
			if (baseRow == null || row == baseRow)
			{
				return string.Empty;
			}

			int count = projectileCount(row);
			int baseCount = projectileCount(baseRow);
			int delta = count - baseCount;
			if (delta <= 0)
			{
				return string.Empty;
			}

			return "투사체 +" + delta + "개";
		}

		private static int projectileCount(Table_SkillInfo.Row row)
		{
			Table_SkillEffect.Row e = firstEffectOfType(row, SkillEffectTypes.SpawnProjectile);
			if (e == null || SkillEffectParams.TryParseSpawnProjectile(e, out SpawnProjectileParams p) == false)
			{
				return 0;
			}

			return p.Count;
		}

		// 속성 증감 — 모든 Increase/DecreaseAttribute 효과를 "스탯명 +값[%]" / "-값[%]" 로.
		private static void attributePhrases(Table_SkillInfo.Row row, List<string> outList)
		{
			List<SkillEffect> effects = new List<SkillEffect>(7);
			forEachEffect(row, effects);
			for (int i = 0; i < effects.Count; i++)
			{
				if (effects[i] == SkillEffect.None)
				{
					continue;
				}

				Table_SkillEffect.Row e = Table_SkillEffect.Get(effects[i]);
				if (e == null)
				{
					continue;
				}

				if (e.EffectType == SkillEffectTypes.IncreaseAttribute)
				{
					addIfNotEmpty(outList, attributePhrase(e, true));
				}
				else if (e.EffectType == SkillEffectTypes.DecreaseAttribute)
				{
					addIfNotEmpty(outList, attributePhrase(e, false));
				}
			}
		}

		private static string attributePhrase(Table_SkillEffect.Row e, bool isIncrease)
		{
			if (SkillEffectParams.TryParseAttribute(e, out AttributeParams p) == false)
			{
				return string.Empty;
			}

			string statName = SkillTextNames.Stat(p.AttrType);
			string sign = isIncrease ? "+" : "-";
			return statName + " " + sign + formatAttributeValue(p);
		}

		// 버프/디버프 — ActivateBuff 는 내부 효과가 속성 증감이면 그쪽을(레벨마다 수치 변화) 노출,
		// 그 외(스턴 등)는 "버프명(지속시간초)". DeactivateBuff 는 "버프명 해제".
		private static void buffPhrases(Table_SkillInfo.Row row, List<string> outList)
		{
			List<SkillEffect> effects = new List<SkillEffect>(7);
			forEachEffect(row, effects);
			for (int i = 0; i < effects.Count; i++)
			{
				if (effects[i] == SkillEffect.None)
				{
					continue;
				}

				Table_SkillEffect.Row e = Table_SkillEffect.Get(effects[i]);
				if (e == null)
				{
					continue;
				}

				if (e.EffectType == SkillEffectTypes.ActivateBuff)
				{
					activateBuffPhrases(e, outList);
				}
				else if (e.EffectType == SkillEffectTypes.DeactivateBuff)
				{
					addIfNotEmpty(outList, deactivateBuffPhrase(e));
				}
			}
		}

		private static void activateBuffPhrases(Table_SkillEffect.Row e, List<string> outList)
		{
			if (SkillEffectParams.TryParseActivateBuff(e, out ActivateBuffParams p) == false)
			{
				return;
			}

			Table_BuffInfo.Row buff = Table_BuffInfo.Get(p.BuffID);

			// 내부 효과가 속성 증감이면 레벨마다 수치가 바뀌므로 그 문구를 노출.
			if (buff != null && buff.Effect != SkillEffect.None)
			{
				Table_SkillEffect.Row inner = Table_SkillEffect.Get(buff.Effect);
				if (inner != null && inner.EffectType == SkillEffectTypes.IncreaseAttribute)
				{
					addIfNotEmpty(outList, attributePhrase(inner, true));
					return;
				}

				if (inner != null && inner.EffectType == SkillEffectTypes.DecreaseAttribute)
				{
					addIfNotEmpty(outList, attributePhrase(inner, false));
					return;
				}
			}

			string buffName = buff != null && string.IsNullOrEmpty(buff.Name) == false ? buff.Name : p.BuffID.ToString();
			string duration = p.Duration > 0f ? "(" + formatNum(p.Duration) + "초)" : "(지속)";
			outList.Add(buffName + duration);
		}

		private static string deactivateBuffPhrase(Table_SkillEffect.Row e)
		{
			if (SkillEffectParams.TryParseDeactivateBuff(e, out DeactivateBuffParams p) == false)
			{
				return string.Empty;
			}

			Table_BuffInfo.Row buff = Table_BuffInfo.Get(p.BuffID);
			string buffName = buff != null && string.IsNullOrEmpty(buff.Name) == false ? buff.Name : p.BuffID.ToString();
			return buffName + " 해제";
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
				case SkillEffectTypes.ActivateAura: return describeActivateAura(row, rangePhrase, depth);
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

		// 오라 발동 — 대상 오라(Table_AuraInfo)의 상시/주기 효과를 각각 풀어써 줄바꿈으로 잇는다.
		// 주기 효과(Interval>0)는 "N초마다 " 접두를 붙인다.
		private static string describeActivateAura(Table_SkillEffect.Row row, string rangePhrase, int depth)
		{
			if (SkillEffectParams.TryParseActivateAura(row, out AuraParams p) == false)
			{
				return fallbackName(row);
			}

			Table_AuraInfo.Row aura = Table_AuraInfo.Get(p.AuraId);
			if (aura == null)
			{
				return fallbackName(row);
			}

			SkillEffect[] effects = { aura.Effect_1, aura.Effect_2, aura.Effect_3, aura.Effect_4 };
			float[] intervals = { aura.Effect1_Interval, aura.Effect2_Interval, aura.Effect3_Interval, aura.Effect4_Interval };

			List<string> lines = new List<string>();
			for (int i = 0; i < effects.Length; i++)
			{
				if (effects[i] == SkillEffect.None)
				{
					continue;
				}

				string inner = describeEffect(effects[i], rangePhrase, depth + 1);
				if (string.IsNullOrEmpty(inner) == true)
				{
					continue;
				}

				if (intervals[i] > 0f)
				{
					inner = formatNum(intervals[i]) + "초마다 " + inner;
				}

				lines.Add(inner);
			}

			if (lines.Count == 0)
			{
				return fallbackName(row);
			}

			return string.Join("\n", lines);
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
			return string.Empty;
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
				case SkillCastingTypes.Aura: return "[오라] ";
				case SkillCastingTypes.Casting: return castingParam + "초 캐스팅 후 ";
				case SkillCastingTypes.OnHitCaster: return "공격 시 " + castingParam + "% 확률로 ";
				case SkillCastingTypes.OnHitTarget: return "공격 시 " + castingParam + "% 확률로 ";
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
