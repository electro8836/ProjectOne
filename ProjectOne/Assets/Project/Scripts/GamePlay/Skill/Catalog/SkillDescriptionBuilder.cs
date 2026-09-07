using System.Collections.Generic;
using System.Text;
using EDT;

namespace ProjectOne.Skill
{
	// 스킬 설명 문구 생성 — 테이블 값에서 사람이 읽는 문장을 만든다.
	//
	// 구버전은 효과 슬롯 7개를 순회했으나, 신규 구조는 EffectID_01/02 + ChainEffectIDs 그래프다.
	// 연쇄는 "적중 시 추가로" 일어나는 것이므로 들여쓰기 대신 접속 문구로 잇는다.
	public static class SkillDescriptionBuilder
	{
		// 테이블 원본 기준 설명 — 소유자가 없는 화면(도감 등)이 쓴다.
		public static string Build(EDT.Skill skillId)
		{
			return Build(ResolvedSkill.CreatePassthrough(skillId));
		}

		// 리졸브 결과 기준 설명 — 플레이어가 실제로 보는 수치와 일치한다.
		// 트리·장비 옵션이 붙은 뒤의 사거리·쿨타임·계수를 그대로 읽는다.
		public static string Build(ResolvedSkill resolved)
		{
			if (resolved == null || resolved.IsValid == false)
			{
				return string.Empty;
			}

			Table_Skill.Row row = resolved.Row;
			StringBuilder sb = new StringBuilder(256);

			string prefix = castingPrefix(row.CastingType);
			if (string.IsNullOrEmpty(prefix) == false)
			{
				sb.Append(prefix);
			}

			string range = rangePhrase(row);
			if (string.IsNullOrEmpty(range) == false)
			{
				sb.Append(range).Append(' ');
			}

			// Append 로 붙은 효과까지 포함하려면 테이블 슬롯이 아니라 리졸브 목록을 순회해야 한다.
			for (int i = 0; i < resolved.Effects.Count; i++)
			{
				appendEffectRow(sb, resolved.Effects[i], 0);
			}

			if (row.Cooldown > 0f)
			{
				sb.Append("\n재사용 대기시간 ").Append(row.Cooldown.ToString("0.##")).Append("초");
			}

			return sb.ToString();
		}

		// ── 효과 ──────────────────────────────────────────────────────

		static void appendEffect(StringBuilder sb, SkillEffect effectId, int depth)
		{
			if (effectId == SkillEffect.None || depth > SkillConstants.CHAIN_DEPTH_LIMIT)
			{
				return;
			}

			// 연쇄 효과는 리졸브 목록에 없으므로 테이블에서 읽는다.
			appendEffectRow(sb, Table_SkillEffect.Get(effectId), depth);
		}

		static void appendEffectRow(StringBuilder sb, Table_SkillEffect.Row row, int depth)
		{
			if (row == null || depth > SkillConstants.CHAIN_DEPTH_LIMIT)
			{
				return;
			}

			string text = describe(row);
			if (string.IsNullOrEmpty(text) == false)
			{
				if (sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n')
				{
					sb.Append('\n');
				}

				sb.Append(text);
			}

			// 연쇄는 부모가 적중했을 때만 일어난다 — 문구도 조건으로 잇는다.
			if (row.ChainEffectIDs == null)
			{
				return;
			}

			for (int i = 0; i < row.ChainEffectIDs.Length; i++)
			{
				appendEffect(sb, row.ChainEffectIDs[i], depth + 1);
			}
		}

		static string describe(Table_SkillEffect.Row row)
		{
			switch (row.EffectType)
			{
				case SkillEffectTypes.Damage:
				{
					DamageParams p;
					if (SkillEffectParams.TryParseDamage(row, out p) == false)
					{
						return string.Empty;
					}

					StringBuilder sb = new StringBuilder(64);
					if (p.ScaleStat != Stat.None && p.Ratio != 0f)
					{
						sb.Append(SkillTextNames.StatName(p.ScaleStat))
						  .Append("의 ").Append((p.Ratio * 100f).ToString("0.##")).Append("%");
					}

					if (p.FlatValue != 0f)
					{
						if (sb.Length > 0)
						{
							sb.Append(" + ");
						}

						sb.Append(p.FlatValue.ToString("0.##"));
					}

					sb.Append(" 피해");
					if (p.HitCount > 1)
					{
						sb.Append("를 ").Append(p.HitCount).Append("회 반복");
					}

					return sb.ToString();
				}

				case SkillEffectTypes.Heal:
				{
					HealParams p;
					if (SkillEffectParams.TryParseHeal(row, out p) == false)
					{
						return string.Empty;
					}

					StringBuilder sb = new StringBuilder(64);
					if (p.ScaleStat != Stat.None && p.Ratio != 0f)
					{
						sb.Append(SkillTextNames.StatName(p.ScaleStat))
						  .Append("의 ").Append((p.Ratio * 100f).ToString("0.##")).Append("%");
					}

					if (p.FlatValue != 0f)
					{
						if (sb.Length > 0)
						{
							sb.Append(" + ");
						}

						sb.Append(p.FlatValue.ToString("0.##"));
					}

					sb.Append(" 회복");
					if (p.TickCount > 1)
					{
						sb.Append(" (").Append(p.TickInterval.ToString("0.##")).Append("초마다 ")
						  .Append(p.TickCount).Append("회)");
					}

					return sb.ToString();
				}

				case SkillEffectTypes.Buff:
				{
					BuffParams p;
					if (SkillEffectParams.TryParseBuff(row, out p) == false)
					{
						return string.Empty;
					}

					Table_Buff.Row buff = Table_Buff.Get(p.RefID);
					string name = (buff != null && string.IsNullOrEmpty(buff.Name) == false) ? buff.Name : p.RefID.ToString();

					StringBuilder sb = new StringBuilder(64);
					sb.Append(name);
					if (p.Duration > 0f)
					{
						sb.Append(' ').Append(p.Duration.ToString("0.##")).Append("초");
					}

					sb.Append(" 부여");
					if (p.Chance < 1f)
					{
						sb.Append(" (").Append((p.Chance * 100f).ToString("0.##")).Append("% 확률)");
					}

					return sb.ToString();
				}

				case SkillEffectTypes.StatChange:
				{
					StatChangeParams p;
					if (SkillEffectParams.TryParseStatChange(row, out p) == false)
					{
						return string.Empty;
					}

					return SkillTextNames.FormatStatDetail(p.StatDetailID, p.Value);
				}

				case SkillEffectTypes.Projectile:
				{
					ProjectileParams p;
					if (SkillEffectParams.TryParseProjectile(row, out p) == false)
					{
						return string.Empty;
					}

					return (p.Count > 1) ? ("투사체 " + p.Count + "발 발사") : "투사체 발사";
				}

				case SkillEffectTypes.Summon:
				{
					SummonParams p;
					if (SkillEffectParams.TryParseSummon(row, out p) == false)
					{
						return string.Empty;
					}

					return (p.Count > 1) ? ("소환물 " + p.Count + "체 소환") : "소환물 소환";
				}

				case SkillEffectTypes.Force:
				{
					ForceParams p;
					if (SkillEffectParams.TryParseForce(row, out p) == false)
					{
						return string.Empty;
					}

					return (p.ForceType == ForceType.Pull) ? "대상을 끌어당김" : "대상을 밀어냄";
				}

				case SkillEffectTypes.CooldownReduce:
				{
					CooldownReduceParams p;
					if (SkillEffectParams.TryParseCooldownReduce(row, out p) == false)
					{
						return string.Empty;
					}

					if (p.Ratio >= 1f)
					{
						return "재사용 대기시간 초기화";
					}

					return "재사용 대기시간 감소";
				}

				case SkillEffectTypes.BuffConsume:
				{
					BuffConsumeParams p;
					if (SkillEffectParams.TryParseBuffConsume(row, out p) == false)
					{
						return string.Empty;
					}

					Table_Buff.Row buff = Table_Buff.Get(p.RefID);
					string name = (buff != null && string.IsNullOrEmpty(buff.Name) == false) ? buff.Name : p.RefID.ToString();
					return name + " " + p.Count + "중첩 소모 시";
				}

				default:
					return string.Empty;
			}
		}

		// ── 머리말 ────────────────────────────────────────────────────

		static string castingPrefix(SkillCastingTypes type)
		{
			switch (type)
			{
				case SkillCastingTypes.Passive:   return "[패시브] ";
				case SkillCastingTypes.Aura:      return "[오라] ";
				case SkillCastingTypes.OnHit:     return "[적중 시] ";
				case SkillCastingTypes.OnCrit:    return "[치명타 시] ";
				case SkillCastingTypes.OnCombo:   return "[연타 시] ";
				case SkillCastingTypes.OnKill:    return "[처치 시] ";
				case SkillCastingTypes.OnDamaged: return "[피격 시] ";
				case SkillCastingTypes.OnLowHP:   return "[체력 부족 시] ";
				default:                          return string.Empty;
			}
		}

		static string rangePhrase(Table_Skill.Row row)
		{
			switch (row.ScanType)
			{
				case SkillScanTypes.Circle:
					return "반경 " + row.ScanRange.ToString("0.##") + " 내 대상에게";
				case SkillScanTypes.Sector:
					return "전방 " + row.ScanParam.ToString("0.##") + "도 범위의 대상에게";
				case SkillScanTypes.Line:
					return "전방 " + row.ScanRange.ToString("0.##") + " 직선상의 대상에게";
				case SkillScanTypes.Target:
					return "사거리 " + row.ScanRange.ToString("0.##") + " 내 대상에게";
				default:
					return string.Empty;
			}
		}

		// 스킬 트리의 레벨별 요약 — 레벨마다 별도 스킬 행을 쓰는 구조에서 사용한다.
		public static string[] BuildLevelSummaries(IReadOnlyList<EDT.Skill> levels)
		{
			if (levels == null)
			{
				return new string[0];
			}

			string[] result = new string[levels.Count];
			for (int i = 0; i < levels.Count; i++)
			{
				result[i] = Build(levels[i]);
			}

			return result;
		}
	}
}
