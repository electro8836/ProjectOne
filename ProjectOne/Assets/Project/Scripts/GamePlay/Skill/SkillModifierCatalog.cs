using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Skill
{
	// 모디파이어가 건드리는 자리. ParamKey 문자열 해석 결과를 Build 시점에 굽는다 (설계 9.2).
	public enum ModifierBakedTarget
	{
		None = 0,
		Named,			// 대상 시트의 명명 컬럼 직접 접근 (ScanRange · Cooldown · AnimLength · EffectTime …)
		CastingParam,	// Skill.CastingParam — CastingType 이 의미를 결정
		ScanParam,		// Skill.ScanParam — ScanType 이 의미를 결정
		EffectParam,	// SkillEffect.EffectParam_N — BakedIndex 가 슬롯 번호(1~5)
		EffectList		// 효과 리스트 자체 (Append 전용)
	}

	// 해석이 끝난 모디파이어 1건.
	public sealed class BakedModifier
	{
		public EDT.SkillModifier id;
		public SkillModifierScope scope;
		public ModifierOperator op;

		// 대상 — scope 에 해당하는 것만 유효하다.
		public EDT.Skill scopeSkill;
		public EDT.SkillEffect scopeEffect;

		public string paramKey;
		public ModifierBakedTarget target;
		public int bakedIndex;		// target == EffectParam 일 때 슬롯 번호(1~5)

		public string refValue;		// Replace / Append / Set(참조) 의 교체·추가 대상
		public float defaultValue;	// 값 칸이 없는 부착처용 기본값 (설계 9.5)
	}

	// SkillModifier 정적 조회 캐시.
	//
	// 설계상 ParamKey 해석(BakedTarget / BakedIndex)은 임포터가 굽는 몫이지만 컨버터 작업은 STEP 15 다.
	// Build() 에서 한 번 구우면 결과가 같고, 런타임 조회 경로에는 분기만 남는다.
	//
	// SkillParamCatalog 이후에 Build() 되어야 한다 (EffectParam 슬롯 번호 조회에 의존).
	public static class SkillModifierCatalog
	{
		// Skill 명명 컬럼 — 설계 9.2 ②
		private const string KEY_SCAN_RANGE = "ScanRange";
		private const string KEY_COOLDOWN = "Cooldown";
		private const string KEY_ANIM_LENGTH = "AnimLength";

		// SkillEffect 명명 컬럼 — 설계 9.2 Scope=Effect ①
		private const string KEY_EFFECT_TIME = "EffectTime";

		// 효과 목록 자체를 지칭하는 예약어 (Append 전용)
		public const string KEY_EFFECT_ID = "EffectID";

		private static readonly Dictionary<EDT.SkillModifier, BakedModifier> _byId =
			new Dictionary<EDT.SkillModifier, BakedModifier>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byId.Clear();

			Dictionary<EDT.SkillModifier, Table_SkillModifier.Row> all = Table_SkillModifier.All();
			Dictionary<EDT.SkillModifier, Table_SkillModifier.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillModifier.Row row = e.Current.Value;
				if (row.ID == EDT.SkillModifier.None)
				{
					continue;
				}

				BakedModifier baked = bake(row);
				if (baked != null)
				{
					_byId[row.ID] = baked;
				}
			}

			_built = true;
			Debug.Log($"[SkillModifierCatalog] 구축 완료 — 모디파이어 {_byId.Count} / 전체 {all.Count}");
		}

		public static BakedModifier Get(EDT.SkillModifier id)
		{
			if (_built == false)
			{
				Debug.LogError("[SkillModifierCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				return null;
			}

			BakedModifier baked;
			_byId.TryGetValue(id, out baked);
			return baked;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static BakedModifier bake(Table_SkillModifier.Row row)
		{
			BakedModifier baked = new BakedModifier();
			baked.id = row.ID;
			baked.scope = row.Scope;
			baked.op = row.Operator;
			baked.scopeSkill = row.Scope_Skill;
			baked.scopeEffect = row.Scope_Effect;
			baked.paramKey = row.ParamKey;
			baked.refValue = row.RefValue;
			baked.defaultValue = row.Value;

			// Replace 는 스킬 자체를 교체하므로 ParamKey 가 비어 있다 (설계 9.3).
			if (row.Operator == ModifierOperator.Replace)
			{
				if (row.Scope != SkillModifierScope.Skill)
				{
					Debug.LogError($"[SkillModifierCatalog] Replace 는 Scope=Skill 에서만 가능합니다: {row.ID}");
					return null;
				}

				baked.target = ModifierBakedTarget.None;
				return baked;
			}

			// Append 는 EffectID 전용이다 (설계 9.3 표).
			if (row.Operator == ModifierOperator.Append)
			{
				if (row.ParamKey != KEY_EFFECT_ID)
				{
					Debug.LogError($"[SkillModifierCatalog] Append 는 ParamKey=EffectID 전용입니다: {row.ID} (ParamKey={row.ParamKey})");
					return null;
				}

				baked.target = ModifierBakedTarget.EffectList;
				return baked;
			}

			if (row.ParamKey == KEY_EFFECT_ID)
			{
				Debug.LogError($"[SkillModifierCatalog] EffectID 는 Append 만 가능합니다: {row.ID} (Operator={row.Operator})");
				return null;
			}

			switch (row.Scope)
			{
				case SkillModifierScope.Skill:
					return bakeSkillScope(row, baked);

				case SkillModifierScope.Effect:
					return bakeEffectScope(row, baked);

				case SkillModifierScope.Projectile:
				case SkillModifierScope.Summon:
				case SkillModifierScope.Buff:
					// 해당 시트의 명명 컬럼 직접 접근. 실제 적용은 각 시스템이 붙을 때(STEP 13) 연결한다.
					baked.target = ModifierBakedTarget.Named;
					return baked;
			}

			Debug.LogError($"[SkillModifierCatalog] Scope 가 지정되지 않은 모디파이어: {row.ID}");
			return null;
		}

		// 설계 9.2 Scope=Skill 의 ②③④ 순서를 그대로 따른다.
		private static BakedModifier bakeSkillScope(Table_SkillModifier.Row row, BakedModifier baked)
		{
			// ② Skill 명명 컬럼
			if (row.ParamKey == KEY_SCAN_RANGE || row.ParamKey == KEY_COOLDOWN || row.ParamKey == KEY_ANIM_LENGTH)
			{
				baked.target = ModifierBakedTarget.Named;
				return baked;
			}

			Table_Skill.Row skill = Table_Skill.Get(row.Scope_Skill);
			if (skill == null)
			{
				Debug.LogError($"[SkillModifierCatalog] Scope_Skill 이 실존하지 않습니다: {row.ID} → {row.Scope_Skill}");
				return null;
			}

			// ③ CastingType 이 소유한 파라미터 이름인가
			if (matchesCastingParam(skill.CastingType, row.ParamKey) == true)
			{
				baked.target = ModifierBakedTarget.CastingParam;
				return baked;
			}

			// ④ ScanType 이 소유한 파라미터 이름인가
			if (matchesScanParam(skill.ScanType, row.ParamKey) == true)
			{
				baked.target = ModifierBakedTarget.ScanParam;
				return baked;
			}

			Debug.LogError($"[SkillModifierCatalog] ParamKey '{row.ParamKey}' 를 해석하지 못했습니다 — {row.ID} (Skill={row.Scope_Skill} Casting={skill.CastingType} Scan={skill.ScanType})");
			return null;
		}

		private static BakedModifier bakeEffectScope(Table_SkillModifier.Row row, BakedModifier baked)
		{
			// ① SkillEffect 명명 컬럼
			if (row.ParamKey == KEY_EFFECT_TIME)
			{
				baked.target = ModifierBakedTarget.Named;
				return baked;
			}

			Table_SkillEffect.Row effect = Table_SkillEffect.Get(row.Scope_Effect);
			if (effect == null)
			{
				Debug.LogError($"[SkillModifierCatalog] Scope_Effect 가 실존하지 않습니다: {row.ID} → {row.Scope_Effect}");
				return null;
			}

			// ② SkillParamDef[EffectType] 의 슬롯 이름
			int index = SkillParamCatalog.GetIndex(effect.EffectType, row.ParamKey);
			if (index < 0)
			{
				Debug.LogError($"[SkillModifierCatalog] {effect.EffectType} 타입에 '{row.ParamKey}' 파라미터가 없습니다: {row.ID}");
				return null;
			}

			baked.target = ModifierBakedTarget.EffectParam;
			baked.bakedIndex = index;
			return baked;
		}

		// 설계 3.3 매핑표. Instant / Passive 는 CastingParam 을 쓰지 않는다.
		private static bool matchesCastingParam(SkillCastingTypes castingType, string paramKey)
		{
			switch (castingType)
			{
				case SkillCastingTypes.Casting:		return paramKey == "CastTime";
				case SkillCastingTypes.OnCombo:		return paramKey == "ComboCount";
				case SkillCastingTypes.OnLowHP:		return paramKey == "HPThreshold";
				case SkillCastingTypes.Aura:		return paramKey == "TickInterval";

				case SkillCastingTypes.OnHit:
				case SkillCastingTypes.OnCrit:
				case SkillCastingTypes.OnDamaged:
				case SkillCastingTypes.OnKill:
					return paramKey == "TriggerChance";
			}

			return false;
		}

		// 설계 3.4 매핑표. Circle 은 ScanParam 을 쓰지 않는다.
		private static bool matchesScanParam(SkillScanTypes scanType, string paramKey)
		{
			switch (scanType)
			{
				case SkillScanTypes.Sector:	return paramKey == "Angle";
				case SkillScanTypes.Line:	return paramKey == "Width";
				case SkillScanTypes.Target:	return paramKey == "MaxTarget";
			}

			return false;
		}
	}
}
