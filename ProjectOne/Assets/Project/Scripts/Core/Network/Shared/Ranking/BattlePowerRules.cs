using System;
using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 전투력 계산 — 저장 데이터(DTO)와 테이블만으로 계산한다. 서버가 랭킹 점수를 매기는 데 쓴다.
	//
	// 전투력 = Σ(영구 출처 스탯 × Weight) + Σ(보유 스킬 옵션 Point)
	// 스탯 합성: (Base + ΣAdd) × (1 + ΣRatio) × (1 + ΣAmp) → Table_Stat Min/Max 클램프 1회.
	// Percent 스탯은 Lv1 기본값 초과분만 점수화한다.
	//
	// 화면에 보이는 전투력(BattlePowerCalculator — 살아 있는 히어로의 StatContainer)과 **같은 값이 나와야 한다.**
	// 영구 출처는 네 갈래다. 새 출처(IHeroAspect)를 추가하면 여기에도 넣는다.
	//   1. 기본 스탯   Table_CharacterStat × 캐릭터 레벨          (StatContainerFactory.ApplyCharacterBase)
	//   2. 장비        장착 장비의 기본·해금 옵션                 (EquipmentAspect / EquipmentOptionCalculator)
	//   3. 마스터리    레벨 보너스 + 총합 보너스 + 현재 무기 트리 (MasteryAspect)
	//   4. 펫          보유 펫의 옵션                             (PetAspect)
	// 버프·패시브가 붙이는 임시 스탯과 던전 카드스킬은 들어가지 않는다.
	public static class BattlePowerRules
	{
		// 스탯 하나의 레이어 누적.
		private sealed class Bucket
		{
			public float baseValue;
			public float add;
			public float ratio;
			public float amp;
		}

		private struct Part
		{
			public Stat stat;
			public StatDetailTypes kind;
		}

		private struct OptionEntry
		{
			public OptionTypes type;
			public StatDetail statDetail;
			public EDT.Skill skill;
		}

		// ── 테이블 인덱스(처음 쓸 때 1회 구축) ─────────────────────────

		private static readonly Dictionary<StatDetail, Part> _parts = new Dictionary<StatDetail, Part>();
		private static readonly Dictionary<Stat, float> _baselines = new Dictionary<Stat, float>();
		private static readonly Dictionary<Option, OptionEntry> _options = new Dictionary<Option, OptionEntry>();
		private static readonly Dictionary<long, Table_EquipOption.Row> _equipOptions = new Dictionary<long, Table_EquipOption.Row>();
		private static readonly Dictionary<WeaponType, Table_WeaponMastery.Row> _masteryByWeapon = new Dictionary<WeaponType, Table_WeaponMastery.Row>();
		private static readonly Dictionary<int, List<Table_SkillTreeNode.Row>> _nodesByGroup = new Dictionary<int, List<Table_SkillTreeNode.Row>>();

		private static bool _built;

		public static int Calculate(LoadoutDto loadout, InventoryDto inventory, MasteryDto mastery, PetDto pet)
		{
			ensureBuilt();

			Dictionary<Stat, Bucket> buckets = new Dictionary<Stat, Bucket>();
			HashSet<EDT.Skill> skills = new HashSet<EDT.Skill>();

			applyCharacterBase(buckets, (loadout != null) ? loadout.level : 1);

			EquipmentInstanceDto weapon = applyEquipment(buckets, skills, loadout, inventory);
			applyMastery(buckets, skills, mastery, weapon);
			applyPets(buckets, pet);

			float total = sumStats(buckets) + sumOptions(skills);
			if (total < 0f)
			{
				total = 0f;
			}

			// Mathf.RoundToInt 와 같은 규칙(짝수 반올림).
			return (int)Math.Round(total);
		}

		// 전 마스터리의 레벨 합 — 기록이 없는 마스터리는 1레벨이다(클라 MasteryBook.TotalLevel 과 같은 규칙).
		public static int TotalMasteryLevel(MasteryDto mastery)
		{
			int sum = 0;
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = Table_WeaponMastery.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				sum += getMasteryLevel(mastery, e.Current.Key);
			}

			return sum;
		}

		// ── 출처별 적용 ───────────────────────────────────────────────

		// 값 = BaseValue + PerLevel × (Level - 1)
		private static void applyCharacterBase(Dictionary<Stat, Bucket> buckets, int level)
		{
			int lv = level > 1 ? level : 1;

			Dictionary<int, Table_CharacterStat.Row>.Enumerator e = Table_CharacterStat.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_CharacterStat.Row row = e.Current.Value;

				Part part;
				if (_parts.TryGetValue(row.StatDetailID, out part) == false || part.kind != StatDetailTypes.Base)
				{
					continue;
				}

				// SetBase 는 절대 대입이다 — 같은 StatDetail 행이 둘이면 마지막 값이 남는다.
				getBucket(buckets, part.stat).baseValue = row.BaseValue + row.PerLevel * (lv - 1);
			}
		}

		// 장착 장비의 옵션. 손에 든 무기(없으면 null)를 돌려준다 — 현재 마스터리를 정한다.
		private static EquipmentInstanceDto applyEquipment(Dictionary<Stat, Bucket> buckets, HashSet<EDT.Skill> skills, LoadoutDto loadout, InventoryDto inventory)
		{
			EquipmentInstanceDto weapon = null;
			if (loadout == null || loadout.slots == null || inventory == null)
			{
				return null;
			}

			for (int slot = 1; slot < loadout.slots.Length; slot++)
			{
				EquipmentInstanceDto instance = findEquipment(inventory, loadout.slots[slot]);
				if (instance == null)
				{
					continue;
				}

				if (slot == (int)EquipSlotTypes.Weapon)
				{
					weapon = instance;
				}

				Table_Equipment.Row equipment = Table_Equipment.Get(instance.itemId);
				if (equipment == null)
				{
					continue;
				}

				int level = instance.level > 0 ? instance.level : 1;

				// 기본 옵션 — 현재 등급 행 하나. Val 은 0레벨 기준값이다.
				Table_EquipOption.Row current = getEquipOption(equipment.EquipOptionGroupID, instance.grade);
				if (current != null)
				{
					applyOption(buckets, skills, current.Opt1_ID, current.Opt1_Val + current.Opt1_Step * level);
					applyOption(buckets, skills, current.Opt2_ID, current.Opt2_Val + current.Opt2_Step * level);
					applyOption(buckets, skills, current.Opt3_ID, current.Opt3_Val + current.Opt3_Step * level);
					applyOption(buckets, skills, current.Opt4_ID, current.Opt4_Val + current.Opt4_Step * level);
				}

				// 해금 옵션 — 현재 등급 이하 전부 누적.
				float t = EquipmentQuality.ToRate(instance.quality);
				for (int g = (int)ItemGradeType.Normal; g <= instance.grade; g++)
				{
					Table_EquipOption.Row row = getEquipOption(equipment.EquipOptionGroupID, g);
					if (row == null || row.UnlockOpt_ID == Option.None)
					{
						continue;
					}

					applyOption(buckets, skills, row.UnlockOpt_ID, row.UnlockOpt_MinVal + (row.UnlockOpt_MaxVal - row.UnlockOpt_MinVal) * t);
				}
			}

			return weapon;
		}

		private static void applyMastery(Dictionary<Stat, Bucket> buckets, HashSet<EDT.Skill> skills, MasteryDto mastery, EquipmentInstanceDto weapon)
		{
			// 1. 무기별 레벨 보너스 — 전 마스터리 합산, 들고 있는 무기와 무관하다.
			int totalLevel = 0;
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = Table_WeaponMastery.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_WeaponMastery.Row row = e.Current.Value;
				int level = getMasteryLevel(mastery, e.Current.Key);
				totalLevel += level;

				if (row.ID == WeaponMastery.None || row.LevelBonusOption == Option.None)
				{
					continue;
				}

				applyStatOnly(buckets, row.LevelBonusOption, row.LevelBonusPerLevel * level);
			}

			// 2. 총합 레벨 보너스.
			Dictionary<int, Table_MasteryLevelBonus.Row>.Enumerator be = Table_MasteryLevelBonus.All().GetEnumerator();
			while (be.MoveNext() == true)
			{
				Table_MasteryLevelBonus.Row row = be.Current.Value;
				if (row.LevelBonusOption == Option.None)
				{
					continue;
				}

				applyStatOnly(buckets, row.LevelBonusOption, row.LevelBonusPerLevel * totalLevel);
			}

			// 3. 현재 무기의 마스터리 — 무기가 없으면 기본공격도 트리도 없다.
			Table_WeaponMastery.Row current = getCurrentMastery(weapon);
			if (current == null)
			{
				return;
			}

			addSkill(skills, current.NormalAttackSkill);
			if (current.Dev_SkillIDs != null)
			{
				for (int i = 0; i < current.Dev_SkillIDs.Length; i++)
				{
					addSkill(skills, current.Dev_SkillIDs[i]);
				}
			}

			MasteryProgressDto progress = findProgress(mastery, (int)current.ID);
			List<Table_SkillTreeNode.Row> nodes;
			if (progress == null || _nodesByGroup.TryGetValue(current.SkillTreeNodeGroupID, out nodes) == false)
			{
				return;
			}

			for (int i = 0; i < nodes.Count; i++)
			{
				Table_SkillTreeNode.Row node = nodes[i];
				int level = getNodeLevel(progress, node.ID);
				if (level <= 0)
				{
					continue;
				}

				applyOption(buckets, skills, node.Option, node.BaseValue + node.PerLevelValue * (level - 1));
				applyOption(buckets, skills, node.Option_02, node.BaseValue_02 + node.PerLevelValue_02 * (level - 1));
			}
		}

		// 보유한 펫 전부의 옵션 — 장착 여부와 무관하다.
		private static void applyPets(Dictionary<Stat, Bucket> buckets, PetDto pet)
		{
			if (pet == null || pet.pets == null)
			{
				return;
			}

			for (int i = 0; i < pet.pets.Count; i++)
			{
				PetEntryDto entry = pet.pets[i];
				if (entry == null || entry.level <= 0)
				{
					continue;
				}

				Table_Pet.Row row = Table_Pet.Get(entry.petId);
				if (row == null || row.ID == 0 || row.Opt_ID == Option.None)
				{
					continue;
				}

				applyStatOnly(buckets, row.Opt_ID, row.Opt_Val + row.Opt_Step * (entry.level - 1));
			}
		}

		// ── 합산 ──────────────────────────────────────────────────────

		private static float sumStats(Dictionary<Stat, Bucket> buckets)
		{
			float total = 0f;

			Dictionary<int, Table_BattlePower_Stat.Row>.Enumerator e = Table_BattlePower_Stat.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_BattlePower_Stat.Row row = e.Current.Value;
				if (row.SourceStat == Stat.None || row.Weight == 0f)
				{
					continue;
				}

				float value = 0f;
				Bucket b;
				if (buckets.TryGetValue(row.SourceStat, out b) == true)
				{
					value = (b.baseValue + b.add) * (1f + b.ratio) * (1f + b.amp);
				}

				value = applyClamp(row.SourceStat, value);

				float baseline;
				if (_baselines.TryGetValue(row.SourceStat, out baseline) == true)
				{
					value -= baseline;
					if (value < 0f)
					{
						value = 0f;
					}
				}

				total += value * row.Weight;
			}

			return total;
		}

		// SkillGrant 옵션만 점수가 된다 — Stat 옵션은 이미 스탯에 녹아 있다.
		private static float sumOptions(HashSet<EDT.Skill> skills)
		{
			float total = 0f;

			Dictionary<int, Table_BattlePower_Option.Row>.Enumerator e = Table_BattlePower_Option.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_BattlePower_Option.Row row = e.Current.Value;
				if (row.SourceOption == Option.None || row.Point == 0f)
				{
					continue;
				}

				OptionEntry entry;
				if (_options.TryGetValue(row.SourceOption, out entry) == false || entry.type != OptionTypes.SkillGrant)
				{
					continue;
				}

				if (entry.skill != EDT.Skill.None && skills.Contains(entry.skill) == true)
				{
					total += row.Point;
				}
			}

			return total;
		}

		// ── 옵션 적용 ─────────────────────────────────────────────────

		// 장비·트리 노드 — Stat 은 스탯에, SkillGrant 는 보유 스킬에. SkillLevel·Modifier 는 스탯 식에 들어가지 않는다.
		private static void applyOption(Dictionary<Stat, Bucket> buckets, HashSet<EDT.Skill> skills, Option option, float value)
		{
			OptionEntry entry;
			if (option == Option.None || _options.TryGetValue(option, out entry) == false)
			{
				return;
			}

			if (entry.type == OptionTypes.Stat)
			{
				addModifier(buckets, entry.statDetail, value);
			}
			else if (entry.type == OptionTypes.SkillGrant)
			{
				addSkill(skills, entry.skill);
			}
		}

		// 레벨 보너스·펫 — Stat 옵션만 허용하고 0 은 붙이지 않는다.
		private static void applyStatOnly(Dictionary<Stat, Bucket> buckets, Option option, float value)
		{
			OptionEntry entry;
			if (value == 0f || _options.TryGetValue(option, out entry) == false || entry.type != OptionTypes.Stat)
			{
				return;
			}

			addModifier(buckets, entry.statDetail, value);
		}

		// Add / Ratio / Amp 레이어만 받는다(Base 는 캐릭터 기본값 전용).
		private static void addModifier(Dictionary<Stat, Bucket> buckets, StatDetail detail, float value)
		{
			Part part;
			if (_parts.TryGetValue(detail, out part) == false)
			{
				return;
			}

			Bucket b = getBucket(buckets, part.stat);
			switch (part.kind)
			{
				case StatDetailTypes.Add:
					b.add += value;
					break;

				case StatDetailTypes.Ratio:
					b.ratio += value;
					break;

				case StatDetailTypes.Amp:
					b.amp += value;
					break;
			}
		}

		private static void addSkill(HashSet<EDT.Skill> skills, EDT.Skill skill)
		{
			if (skill != EDT.Skill.None)
			{
				skills.Add(skill);
			}
		}

		// ── 조회 ──────────────────────────────────────────────────────

		private static Bucket getBucket(Dictionary<Stat, Bucket> buckets, Stat stat)
		{
			Bucket b;
			if (buckets.TryGetValue(stat, out b) == false)
			{
				b = new Bucket();
				buckets[stat] = b;
			}

			return b;
		}

		// Min/Max 가 0 이면 그 방향의 제한이 없다.
		private static float applyClamp(Stat stat, float value)
		{
			Table_Stat.Row row = Table_Stat.Get(stat);
			if (row == null)
			{
				return value;
			}

			if (row.MinValue != 0f && value < row.MinValue)
			{
				value = row.MinValue;
			}

			if (row.MaxValue != 0f && value > row.MaxValue)
			{
				value = row.MaxValue;
			}

			return value;
		}

		private static EquipmentInstanceDto findEquipment(InventoryDto inventory, long uid)
		{
			if (uid <= 0 || inventory.equipments == null)
			{
				return null;
			}

			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto equipment = inventory.equipments[i];
				if (equipment != null && equipment.uid == uid)
				{
					return equipment;
				}
			}

			return null;
		}

		private static Table_EquipOption.Row getEquipOption(int groupId, int grade)
		{
			Table_EquipOption.Row row;
			_equipOptions.TryGetValue(equipOptionKey(groupId, grade), out row);
			return row;
		}

		private static long equipOptionKey(int groupId, int grade)
		{
			return ((long)groupId << 32) | (uint)grade;
		}

		private static Table_WeaponMastery.Row getCurrentMastery(EquipmentInstanceDto weapon)
		{
			if (weapon == null)
			{
				return null;
			}

			Table_Equipment.Row equipment = Table_Equipment.Get(weapon.itemId);
			if (equipment == null || equipment.WeaponType == WeaponType.None)
			{
				return null;
			}

			Table_WeaponMastery.Row row;
			_masteryByWeapon.TryGetValue(equipment.WeaponType, out row);
			return row;
		}

		private static MasteryProgressDto findProgress(MasteryDto mastery, int masteryId)
		{
			if (mastery == null || mastery.masteries == null)
			{
				return null;
			}

			for (int i = 0; i < mastery.masteries.Count; i++)
			{
				MasteryProgressDto progress = mastery.masteries[i];
				if (progress != null && progress.masteryId == masteryId)
				{
					return progress;
				}
			}

			return null;
		}

		// 한 번도 들지 않은 무기도 1레벨로 센다.
		private static int getMasteryLevel(MasteryDto mastery, WeaponMastery id)
		{
			MasteryProgressDto progress = findProgress(mastery, (int)id);
			return (progress != null) ? MasteryRules.LevelFromExp(progress.totalExp) : 1;
		}

		private static int getNodeLevel(MasteryProgressDto progress, int nodeId)
		{
			if (progress.nodeIds == null || progress.nodeLevels == null)
			{
				return 0;
			}

			for (int i = 0; i < progress.nodeIds.Count && i < progress.nodeLevels.Count; i++)
			{
				if (progress.nodeIds[i] == nodeId)
				{
					return progress.nodeLevels[i];
				}
			}

			return 0;
		}

		// ── 인덱스 구축 ───────────────────────────────────────────────

		private static void ensureBuilt()
		{
			if (_built == true)
			{
				return;
			}

			buildParts();
			buildBaselines();
			buildOptions();
			buildEquipOptions();
			buildMasteries();

			_built = true;
		}

		private static void buildParts()
		{
			Dictionary<StatDetail, Table_StatDetail.Row>.Enumerator e = Table_StatDetail.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_StatDetail.Row row = e.Current.Value;
				if (row.ID == StatDetail.None || row.StatID == Stat.None || row.StatDetailType == StatDetailTypes.None)
				{
					continue;
				}

				Part part;
				part.stat = row.StatID;
				part.kind = row.StatDetailType;
				_parts[row.ID] = part;
			}
		}

		// Percent 스탯의 Lv1 기본값. 레벨 성장분은 일부러 뺀다 — 기준선이 레벨과 같이 오르면 레벨업이 전투력에 안 잡힌다.
		private static void buildBaselines()
		{
			Dictionary<int, Table_CharacterStat.Row>.Enumerator e = Table_CharacterStat.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_CharacterStat.Row row = e.Current.Value;

				Part part;
				if (_parts.TryGetValue(row.StatDetailID, out part) == false || part.kind != StatDetailTypes.Base)
				{
					continue;
				}

				Table_Stat.Row stat = Table_Stat.Get(part.stat);
				if (stat == null || stat.ValueType != StatValueTypes.Percent)
				{
					continue;
				}

				_baselines[part.stat] = row.BaseValue;
			}
		}

		// OptionTarget 문자열을 OptionType 에 맞는 enum 으로 해석한다(클라 OptionCatalog 와 같은 규칙).
		private static void buildOptions()
		{
			Dictionary<Option, Table_Option.Row>.Enumerator e = Table_Option.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Option.Row row = e.Current.Value;
				if (row.ID == Option.None || string.IsNullOrEmpty(row.OptionTarget) == true)
				{
					continue;
				}

				OptionEntry entry = default(OptionEntry);
				entry.type = row.OptionType;

				if (row.OptionType == OptionTypes.Stat)
				{
					if (Enum.TryParse<StatDetail>(row.OptionTarget, false, out entry.statDetail) == false)
					{
						continue;
					}
				}
				else if (row.OptionType == OptionTypes.SkillGrant)
				{
					if (Enum.TryParse<EDT.Skill>(row.OptionTarget, false, out entry.skill) == false)
					{
						continue;
					}
				}
				else
				{
					// SkillLevel·Modifier 는 전투력에 쓰이지 않는다.
					continue;
				}

				_options[row.ID] = entry;
			}
		}

		private static void buildEquipOptions()
		{
			Dictionary<int, Table_EquipOption.Row>.Enumerator e = Table_EquipOption.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipOption.Row row = e.Current.Value;
				if (row.GroupID <= 0 || row.ItemGrade == ItemGradeType.None)
				{
					continue;
				}

				_equipOptions[equipOptionKey(row.GroupID, (int)row.ItemGrade)] = row;
			}
		}

		private static void buildMasteries()
		{
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = Table_WeaponMastery.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_WeaponMastery.Row row = e.Current.Value;
				if (row.ID == WeaponMastery.None || row.WeaponType == WeaponType.None)
				{
					continue;
				}

				// WeaponType 하나당 마스터리 하나 — 중복이면 먼저 나온 행을 쓴다.
				if (_masteryByWeapon.ContainsKey(row.WeaponType) == false)
				{
					_masteryByWeapon.Add(row.WeaponType, row);
				}
			}

			Dictionary<int, Table_SkillTreeNode.Row>.Enumerator ne = Table_SkillTreeNode.All().GetEnumerator();
			while (ne.MoveNext() == true)
			{
				Table_SkillTreeNode.Row row = ne.Current.Value;
				if (row.GroupID <= 0)
				{
					continue;
				}

				List<Table_SkillTreeNode.Row> list;
				if (_nodesByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_SkillTreeNode.Row>();
					_nodesByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
			}
		}
	}
}
