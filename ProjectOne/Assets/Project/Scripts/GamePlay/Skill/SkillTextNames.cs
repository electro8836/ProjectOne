using EDT;

namespace ProjectOne.Skill
{
	// 스킬 설명 자동 생성용 enum → 한국어 표시명 변환.
	// 프로젝트에 enum 표시명 매핑이 없어 신규로 둔다. 매핑에 없으면 enum 이름을 그대로 반환(폴백).
	public static class SkillTextNames
	{
		// StatInfo 를 한국어 스탯명으로. _Base/_Add/_Ratio/_Amp 접미사는 떼고 베이스 스탯명으로 매핑한다.
		public static string Stat(StatInfo stat)
		{
			string name = stat.ToString();
			string baseName = StripSuffix(name);

			switch (baseName)
			{
				case "ATK": return "공격력";
				case "MATK": return "마법 공격력";
				case "DEF": return "방어력";
				case "MDEF": return "마법 방어력";
				case "MaxHP": return "최대 체력";
				case "HpRecovery": return "체력 회복";
				case "AtkSpeed": return "공격 속도";
				case "MoveSpeed": return "이동 속도";
				case "CritRate": return "치명타 확률";
				case "CritDam": return "치명타 피해";
				case "SuperCritRate": return "초월 치명타 확률";
				case "SuperCritDam": return "초월 치명타 피해";
				case "MaxStamina": return "최대 스태미너";
				case "StaminaRegen": return "스태미너 회복";
				case "StaminaSteal": return "스태미너 흡수";
				case "Accuracy": return "명중";
				case "Evasion": return "회피";
				case "Block": return "막기";
				case "DamageReduction": return "피해 감소";
				case "DamageTakenAmp": return "받는 피해 증폭";
				case "BreakDamage": return "브레이크 데미지";
				case "BreakGage": return "브레이크 게이지";
				case "BreakRecovery": return "브레이크 회복";
				case "Pen_Physical": return "물리 관통";
				case "Pen_Magic": return "마법 관통";
				case "LifeSteal_Ratio": return "흡혈";
				case "SpellVamp_Ratio": return "주문 흡혈";
				case "KnockBack": return "넉백";
				case "KnockBackResist": return "넉백 저항";
				case "Cooldown_Reduce": return "쿨다운 감소";
				case "DamBoss_Ratio": return "보스 피해";
				case "DamNormal_Ratio": return "일반 몬스터 피해";
				case "SkillDam_Ratio": return "스킬 피해";
				case "NormalDam_Ratio": return "일반 공격 피해";
				default: return name;
			}
		}

		// SkillDamageType 를 한국어로.
		public static string DamageType(SkillDamageType type)
		{
			switch (type)
			{
				case SkillDamageType.Physical: return "물리";
				case SkillDamageType.Magical: return "마법";
				case SkillDamageType.Pure: return "고정";
				default: return string.Empty;
			}
		}

		// _Ratio/_Amp 로 끝나면 퍼센트 입력 스탯 (SkillEffectParams 의 IsPercentInput 과 동일 규칙).
		public static bool IsPercentStat(StatInfo stat)
		{
			string n = stat.ToString();
			return n.EndsWith("_Ratio") == true || n.EndsWith("_Amp") == true;
		}

		// 베이스 스탯명을 얻기 위해 알려진 접미사를 제거한다. (Pen_/LifeSteal_/SpellVamp_/Cooldown_/DamBoss_ 등 언더스코어 포함 베이스명은 보존)
		private static string StripSuffix(string name)
		{
			if (name.EndsWith("_Base") == true)
			{
				return name.Substring(0, name.Length - "_Base".Length);
			}

			if (name.EndsWith("_Add") == true)
			{
				return name.Substring(0, name.Length - "_Add".Length);
			}

			if (name.EndsWith("_Ratio") == true)
			{
				// _Ratio 자체가 베이스명인 스탯(LifeSteal_Ratio 등)은 그대로 두고, 그 외에만 제거
				switch (name)
				{
					case "LifeSteal_Ratio":
					case "SpellVamp_Ratio":
					case "DamBoss_Ratio":
					case "DamNormal_Ratio":
					case "SkillDam_Ratio":
					case "NormalDam_Ratio":
						return name;
					default:
						return name.Substring(0, name.Length - "_Ratio".Length);
				}
			}

			if (name.EndsWith("_Amp") == true)
			{
				return name.Substring(0, name.Length - "_Amp".Length);
			}

			return name;
		}
	}
}
