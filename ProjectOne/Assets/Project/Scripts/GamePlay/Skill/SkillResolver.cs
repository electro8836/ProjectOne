using System.Collections.Generic;
using System.Globalization;
using EDT;
using UnityEngine;
using ProjectOne.Items;
using ProjectOne.Mastery;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Skill
{
	// 스킬 리졸브 파이프라인 (설계 11.1) — 히어로 1명이 소유하는 캐시.
	//
	// 모디파이어의 출처는 스킬 트리와 장비뿐이고 둘 다 계정에 있으므로, 유닛별로 하나만 있으면 된다.
	// 몬스터·소환물은 모디파이어 출처가 없어 리졸브하지 않는다 — UnitBase 가 테이블 패스스루를 돌려준다.
	//
	// 캐시 무효화 조건은 설계 11.4 를 따른다. 스탯 캐시(StatContainer 의 dirty)와는 별개다 —
	// 캐릭터 레벨업이나 순수 스탯 버프는 스킬 구조를 바꾸지 않으므로 여기를 건드리지 않는다.
	public sealed class SkillResolver
	{
		// 수집된 모디파이어 1건 — 정의(어떻게)와 값(얼마나)의 결합 (설계 9.5).
		private struct Entry
		{
			public BakedModifier mod;
			public float value;
		}

		private readonly List<Entry> _collected = new List<Entry>(16);
		private readonly Dictionary<EDT.Skill, ResolvedSkill> _cache = new Dictionary<EDT.Skill, ResolvedSkill>();
		private readonly List<EquipmentOptionCalculator.Resolved> _optionBuffer = new List<EquipmentOptionCalculator.Resolved>(8);

		private bool _collectedDirty = true;

		// 캐시 무효화 — 무기 교체 · 장비 착탈 · 노드 투자 · 트리 초기화 · 구조 변경 버프.
		public void Invalidate()
		{
			_cache.Clear();
			_collectedDirty = true;
		}

		// 리졸브 결과를 얻는다. 데이터가 없으면 null.
		public ResolvedSkill Resolve(EDT.Skill id)
		{
			if (id == EDT.Skill.None)
			{
				return null;
			}

			ResolvedSkill cached;
			if (_cache.TryGetValue(id, out cached) == true)
			{
				return cached;
			}

			ResolvedSkill resolved = build(id);
			_cache[id] = resolved;
			return resolved;
		}

		// ── 파이프라인 ────────────────────────────────────────────────

		private ResolvedSkill build(EDT.Skill requestedId)
		{
			ensureCollected();

			// [1] Replace — 반드시 최우선. 교체된 스킬에 붙는 모디파이어가 유실되지 않도록.
			EDT.Skill finalId = applyReplace(requestedId);

			Table_Skill.Row source = Table_Skill.Get(finalId);
			if (source == null)
			{
				Debug.LogError($"[SkillResolver] Table_Skill.Get({finalId}) == null");
				return null;
			}

			// 건드릴 모디파이어가 하나도 없으면 사본을 만들지 않는다 — 대부분의 스킬이 이 경로다.
			if (hasAnyModifier(finalId, source) == false)
			{
				ResolvedSkill passthrough = ResolvedSkill.CreatePassthrough(finalId);
				return passthrough;
			}

			// [2] 사본 생성 — 효과는 고정 배열이 아니라 가변 리스트로 보관한다.
			ResolvedSkill resolved = new ResolvedSkill();
			resolved.Id = finalId;
			resolved.Row = ResolvedSkill.CopySkill(source);
			addEffectCopy(resolved, source.EffectID_01);
			addEffectCopy(resolved, source.EffectID_02);

			// [3][4] 는 ensureCollected() 가 이미 마쳤다 — 트리 옵션과 장비 모디파이어가 _collected 에 있다.

			// [5] Append — 수치 연산보다 먼저. 새로 붙은 효과에도 Effect 모디파이어가 닿아야 한다.
			applyAppend(resolved);

			// [6][7] Set → Add → Ratio
			applyOperator(resolved, ModifierOperator.Set);
			applyAccumulated(resolved);

			return resolved;
		}

		// [1] Scope=Skill + Operator=Replace 를 순회해 스킬 ID 자체를 교체한다.
		private EDT.Skill applyReplace(EDT.Skill id)
		{
			for (int guard = 0; guard < SkillConstants.CHAIN_DEPTH_LIMIT; guard++)
			{
				EDT.Skill next = findReplacement(id);
				if (next == EDT.Skill.None || next == id)
				{
					return id;
				}

				id = next;
			}

			Debug.LogError($"[SkillResolver] 스킬 교체 순환 참조가 감지되었습니다 — {id}");
			return id;
		}

		private EDT.Skill findReplacement(EDT.Skill id)
		{
			for (int i = 0; i < _collected.Count; i++)
			{
				BakedModifier mod = _collected[i].mod;
				if (mod.op != ModifierOperator.Replace || mod.scopeSkill != id)
				{
					continue;
				}

				EDT.Skill parsed;
				if (System.Enum.TryParse<EDT.Skill>(mod.refValue, false, out parsed) == false)
				{
					Debug.LogError($"[SkillResolver] Replace 대상 스킬을 해석하지 못했습니다: {mod.id} → '{mod.refValue}'");
					continue;
				}

				return parsed;
			}

			return EDT.Skill.None;
		}

		// [5] ParamKey=EffectID / Operator=Append — 효과 리스트에 추가한다.
		// 같은 효과가 두 번 Append 되면 누적된다(수치 옵션과 같은 규칙, 설계 9.3).
		private void applyAppend(ResolvedSkill resolved)
		{
			for (int i = 0; i < _collected.Count; i++)
			{
				BakedModifier mod = _collected[i].mod;
				if (mod.op != ModifierOperator.Append || mod.scopeSkill != resolved.Id)
				{
					continue;
				}

				EDT.SkillEffect parsed;
				if (System.Enum.TryParse<EDT.SkillEffect>(mod.refValue, false, out parsed) == false)
				{
					Debug.LogError($"[SkillResolver] Append 대상 효과를 해석하지 못했습니다: {mod.id} → '{mod.refValue}'");
					continue;
				}

				addEffectCopy(resolved, parsed);
			}
		}

		// [6] Set — 마지막 값이 이긴다. 여러 개가 충돌하면 결과가 비결정적이므로 데이터로 피한다.
		private void applyOperator(ResolvedSkill resolved, ModifierOperator op)
		{
			for (int i = 0; i < _collected.Count; i++)
			{
				Entry entry = _collected[i];
				if (entry.mod.op != op)
				{
					continue;
				}

				applySingle(resolved, entry, op);
			}
		}

		// [6][7] Add 전부 합산 → Ratio 전부 합산 → (Base + ΣAdd) × (1 + ΣRatio)
		//
		// 대상 자리마다 따로 누적해야 하므로 자리별로 합을 모은 뒤 한 번에 쓴다.
		private void applyAccumulated(ResolvedSkill resolved)
		{
			// 자리 식별자 → (Σ Add, Σ Ratio)
			Dictionary<string, Vector2> sums = null;

			for (int i = 0; i < _collected.Count; i++)
			{
				Entry entry = _collected[i];
				if (entry.mod.op != ModifierOperator.Add && entry.mod.op != ModifierOperator.Ratio)
				{
					continue;
				}

				if (matchesScope(resolved, entry.mod) == false)
				{
					continue;
				}

				if (sums == null)
				{
					sums = new Dictionary<string, Vector2>(4);
				}

				string slot = slotKey(entry.mod);
				Vector2 acc;
				sums.TryGetValue(slot, out acc);

				if (entry.mod.op == ModifierOperator.Add)
				{
					acc.x += entry.value;
				}
				else
				{
					acc.y += entry.value;
				}

				sums[slot] = acc;
			}

			if (sums == null)
			{
				return;
			}

			// 같은 자리를 가리키는 모디파이어 중 아무거나 하나로 읽기/쓰기 대상을 특정한다.
			for (int i = 0; i < _collected.Count; i++)
			{
				Entry entry = _collected[i];
				if (entry.mod.op != ModifierOperator.Add && entry.mod.op != ModifierOperator.Ratio)
				{
					continue;
				}

				if (matchesScope(resolved, entry.mod) == false)
				{
					continue;
				}

				string slot = slotKey(entry.mod);
				Vector2 acc;
				if (sums.TryGetValue(slot, out acc) == false)
				{
					continue;	// 이미 처리된 자리
				}

				sums.Remove(slot);

				float baseValue = readValue(resolved, entry.mod);
				writeValue(resolved, entry.mod, (baseValue + acc.x) * (1f + acc.y));
			}
		}

		private void applySingle(ResolvedSkill resolved, Entry entry, ModifierOperator op)
		{
			if (matchesScope(resolved, entry.mod) == false)
			{
				return;
			}

			// Set 은 숫자면 Value, 참조·enum 이면 RefValue 를 쓴다 (설계 9.3).
			if (string.IsNullOrEmpty(entry.mod.refValue) == false)
			{
				writeRaw(resolved, entry.mod, entry.mod.refValue);
				return;
			}

			writeValue(resolved, entry.mod, entry.value);
		}

		// ── 대상 판정 / 값 접근 ───────────────────────────────────────

		private bool matchesScope(ResolvedSkill resolved, BakedModifier mod)
		{
			if (mod.scope == SkillModifierScope.Skill)
			{
				return mod.scopeSkill == resolved.Id;
			}

			if (mod.scope == SkillModifierScope.Effect)
			{
				return resolved.FindEffect(mod.scopeEffect) != null;
			}

			// Projectile / Summon / Buff 는 리졸브 사본에 들어 있지 않다 — 사본은 스킬과 효과만 뜬다.
			// 이 세 시트의 컬럼을 직접 바꾸는 모디파이어는 원본 테이블을 건드려야 해서 지원하지 않는다.
			// 대신 Scope=Effect + ParamKey=RefID 로 "어떤 투사체/소환물을 쓰는가"를 바꾸는 경로가 열려 있다 (설계 9.4).
			warnUnsupportedScope(mod);
			return false;
		}

		// 같은 모디파이어로 매 리졸브마다 로그가 쌓이지 않게 1회만 알린다.
		private readonly HashSet<int> _warnedScopes = new HashSet<int>();

		private void warnUnsupportedScope(BakedModifier mod)
		{
			if (_warnedScopes.Add((int)mod.id) == false)
			{
				return;
			}

			Debug.LogWarning($"[SkillResolver] Scope={mod.scope} 모디파이어는 적용되지 않습니다 — SkillModifier:{mod.id}. "
				+ "Scope=Effect + ParamKey=RefID 로 참조를 바꾸세요 (설계 9.4).");
		}

		private string slotKey(BakedModifier mod)
		{
			return (int)mod.scope + "|" + (int)mod.scopeSkill + "|" + (int)mod.scopeEffect + "|" + mod.paramKey;
		}

		private float readValue(ResolvedSkill resolved, BakedModifier mod)
		{
			switch (mod.target)
			{
				case ModifierBakedTarget.Named:
					return readNamed(resolved, mod);

				case ModifierBakedTarget.CastingParam:
					return resolved.Row.CastingParam;

				case ModifierBakedTarget.ScanParam:
					return resolved.Row.ScanParam;

				case ModifierBakedTarget.EffectParam:
				{
					Table_SkillEffect.Row effect = resolved.FindEffect(mod.scopeEffect);
					string raw = SkillParamCatalog.GetRawParam(effect, mod.bakedIndex);
					float parsed;
					float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
					return parsed;
				}
			}

			return 0f;
		}

		private void writeValue(ResolvedSkill resolved, BakedModifier mod, float value)
		{
			switch (mod.target)
			{
				case ModifierBakedTarget.Named:
					writeNamed(resolved, mod, value);
					return;

				case ModifierBakedTarget.CastingParam:
					resolved.Row.CastingParam = value;
					return;

				case ModifierBakedTarget.ScanParam:
					resolved.Row.ScanParam = value;
					return;

				case ModifierBakedTarget.EffectParam:
					writeRaw(resolved, mod, value.ToString(CultureInfo.InvariantCulture));
					return;
			}
		}

		// 참조·enum 교체 — 파라미터 칸에 문자열을 그대로 써 넣는다 (설계 9.4).
		private void writeRaw(ResolvedSkill resolved, BakedModifier mod, string raw)
		{
			if (mod.target != ModifierBakedTarget.EffectParam)
			{
				float parsed;
				if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) == true)
				{
					writeValue(resolved, mod, parsed);
				}

				return;
			}

			Table_SkillEffect.Row effect = resolved.FindEffect(mod.scopeEffect);
			if (effect == null)
			{
				return;
			}

			switch (mod.bakedIndex)
			{
				case 1: effect.EffectParam_1 = raw; return;
				case 2: effect.EffectParam_2 = raw; return;
				case 3: effect.EffectParam_3 = raw; return;
				case 4: effect.EffectParam_4 = raw; return;
				case 5: effect.EffectParam_5 = raw; return;
			}
		}

		private float readNamed(ResolvedSkill resolved, BakedModifier mod)
		{
			if (mod.scope == SkillModifierScope.Effect)
			{
				Table_SkillEffect.Row effect = resolved.FindEffect(mod.scopeEffect);
				return effect != null ? effect.EffectTime : 0f;
			}

			switch (mod.paramKey)
			{
				case "ScanRange":	return resolved.Row.ScanRange;
				case "Cooldown":	return resolved.Row.Cooldown;
				case "AnimLength":	return resolved.Row.AnimLength;
			}

			return 0f;
		}

		private void writeNamed(ResolvedSkill resolved, BakedModifier mod, float value)
		{
			if (mod.scope == SkillModifierScope.Effect)
			{
				Table_SkillEffect.Row effect = resolved.FindEffect(mod.scopeEffect);
				if (effect != null)
				{
					effect.EffectTime = value;
				}

				return;
			}

			switch (mod.paramKey)
			{
				case "ScanRange":	resolved.Row.ScanRange = value; return;
				case "Cooldown":	resolved.Row.Cooldown = value; return;
				case "AnimLength":	resolved.Row.AnimLength = value; return;
			}
		}

		// ── 모디파이어 수집 ([3][4]) ──────────────────────────────────

		private void ensureCollected()
		{
			if (_collectedDirty == false)
			{
				return;
			}

			_collected.Clear();
			collectFromSkillTree();
			collectFromEquipment();
			_collectedDirty = false;
		}

		// [3] 현재 마스터리의 투자 노드. 다른 마스터리의 트리는 참여하지 않는다.
		private void collectFromSkillTree()
		{
			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row mastery = book.CurrentMastery;
			if (mastery == null)
			{
				return;		// 무기 미착용 — 트리가 없다 (설계 4.3)
			}

			MasteryProgress progress = book.Find(mastery.ID);
			if (progress == null)
			{
				return;
			}

			IReadOnlyList<Table_SkillTreeNode.Row> nodes = MasteryCatalog.GetNodes(mastery.SkillTreeNodeGroupID);
			for (int i = 0; i < nodes.Count; i++)
			{
				Table_SkillTreeNode.Row node = nodes[i];
				int level = progress.GetNodeLevel(node.ID);
				if (level <= 0)
				{
					continue;
				}

				add(node.Option, MasteryProgress.GetNodeValue(node, level));
			}
		}

		// [4] 장비. 부착처의 Value 를 그대로 쓴다.
		// WeaponMastery.LevelBonusOption 은 Stat 전용이라 여기 들어오지 않는다 (설계 11.1 주석).
		private void collectFromEquipment()
		{
			Loadout loadout = Account.Instance.Loadout;
			for (int slot = 1; slot < LoadoutDto.SlotCount; slot++)
			{
				EquipmentInstance instance = loadout.GetEquipped((EquipSlotTypes)slot);
				if (instance == null)
				{
					continue;
				}

				EquipmentOptionCalculator.Collect(instance, _optionBuffer);
				for (int i = 0; i < _optionBuffer.Count; i++)
				{
					add(_optionBuffer[i].option, _optionBuffer[i].value);
				}
			}
		}

		private void add(Option option, float value)
		{
			if (option == Option.None)
			{
				return;
			}

			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(option, out entry) == false || entry.type != OptionTypes.Modifier)
			{
				return;		// Stat / SkillGrant 는 MasteryAspect·EquipmentAspect 가 처리한다
			}

			BakedModifier mod = SkillModifierCatalog.Get(entry.modifier);
			if (mod == null)
			{
				Debug.LogWarning($"[SkillResolver] 해석되지 않은 모디파이어를 참조합니다: {option} → {entry.modifier}");
				return;
			}

			Entry collected;
			collected.mod = mod;
			// 부착처에 값 컬럼이 없으면 SkillModifier.Value 를 기본값으로 쓴다 (설계 9.5).
			collected.value = (value != 0f) ? value : mod.defaultValue;
			_collected.Add(collected);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void addEffectCopy(ResolvedSkill resolved, EDT.SkillEffect effectId)
		{
			if (effectId == EDT.SkillEffect.None)
			{
				return;
			}

			Table_SkillEffect.Row src = Table_SkillEffect.Get(effectId);
			if (src == null)
			{
				Debug.LogError($"[SkillResolver] SkillEffect 행 없음 — {effectId}");
				return;
			}

			resolved.Effects.Add(ResolvedSkill.CopyEffect(src));
		}

		// 이 스킬을 건드리는 모디파이어가 하나라도 있는가 — 없으면 사본을 만들지 않는다.
		private bool hasAnyModifier(EDT.Skill id, Table_Skill.Row row)
		{
			for (int i = 0; i < _collected.Count; i++)
			{
				BakedModifier mod = _collected[i].mod;
				if (mod.scope == SkillModifierScope.Skill && mod.scopeSkill == id)
				{
					return true;
				}

				if (mod.scope == SkillModifierScope.Effect &&
					(mod.scopeEffect == row.EffectID_01 || mod.scopeEffect == row.EffectID_02))
				{
					return true;
				}
			}

			return false;
		}
	}
}
