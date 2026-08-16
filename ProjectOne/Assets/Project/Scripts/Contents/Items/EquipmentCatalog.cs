using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Items
{
	// 장비 테이블 정적 조회 캐시 — 런타임 조회 키가 테이블 기본 키와 다른 것들을 인덱싱한다.
	//
	// EquipOption 의 실제 조회 키는 (GroupID, ItemGrade) 이고 (아이템 설계 3.4),
	// EquipEnhance 는 (Category, EnhanceTier), EquipPromotion 은 (Category, FromGrade) 다.
	//
	// EquipEnhanceTier 는 **두 방향으로 쓴다** (설계 3.5) — 혼동하면 조용히 틀린다.
	//   - 레벨 상한 : 현재 '등급' 으로 조회 → MaxLevel
	//   - 비용 티어 : 현재 '레벨' 이 속한 구간으로 조회 → EnhanceTier
	//
	// BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class EquipmentCatalog
	{
		private struct GradeKey
		{
			public int group;
			public ItemGradeType grade;
		}

		private sealed class GradeKeyComparer : IEqualityComparer<GradeKey>
		{
			public bool Equals(GradeKey a, GradeKey b)
			{
				return a.group == b.group && a.grade == b.grade;
			}

			public int GetHashCode(GradeKey k)
			{
				return (k.group * 397) ^ (int)k.grade;
			}
		}

		private struct SlotKey
		{
			public EquipSlotTypes slot;
			public int sub;
		}

		private sealed class SlotKeyComparer : IEqualityComparer<SlotKey>
		{
			public bool Equals(SlotKey a, SlotKey b)
			{
				return a.slot == b.slot && a.sub == b.sub;
			}

			public int GetHashCode(SlotKey k)
			{
				return ((int)k.slot * 397) ^ k.sub;
			}
		}

		// (EquipOptionGroupID, ItemGrade) → 옵션 행
		private static readonly Dictionary<GradeKey, Table_EquipOption.Row> _options =
			new Dictionary<GradeKey, Table_EquipOption.Row>(new GradeKeyComparer());

		// (Category, EnhanceTier) → 강화 비용 행
		private static readonly Dictionary<SlotKey, Table_EquipEnhance.Row> _enhances =
			new Dictionary<SlotKey, Table_EquipEnhance.Row>(new SlotKeyComparer());

		// (Category, FromGrade) → 승급 비용 행
		private static readonly Dictionary<SlotKey, Table_EquipPromotion.Row> _promotions =
			new Dictionary<SlotKey, Table_EquipPromotion.Row>(new SlotKeyComparer());

		// 등급 → 티어 행 (레벨 상한 조회용)
		private static readonly Dictionary<ItemGradeType, Table_EquipEnhanceTier.Row> _tierByGrade =
			new Dictionary<ItemGradeType, Table_EquipEnhanceTier.Row>();

		// 레벨 구간 순회용 (비용 티어 조회용) — MinLevel 오름차순
		private static readonly List<Table_EquipEnhanceTier.Row> _tiersByLevel = new List<Table_EquipEnhanceTier.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_options.Clear();
			_enhances.Clear();
			_promotions.Clear();
			_tierByGrade.Clear();
			_tiersByLevel.Clear();

			buildOptions();
			buildTiers();
			buildEnhances();
			buildPromotions();

			_built = true;
			Debug.Log($"[EquipmentCatalog] 구축 완료 — 옵션:{_options.Count} 티어:{_tiersByLevel.Count} 강화:{_enhances.Count} 승급:{_promotions.Count}");
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// (옵션 그룹, 등급) 의 옵션 행. 없으면 null.
		public static Table_EquipOption.Row GetOption(int optionGroupId, ItemGradeType grade)
		{
			GradeKey key;
			key.group = optionGroupId;
			key.grade = grade;

			Table_EquipOption.Row row;
			_options.TryGetValue(key, out row);
			return row;
		}

		// 현재 '등급' 기준 강화 레벨 상한. 티어 행이 없으면 0(강화 불가).
		public static int GetMaxLevel(ItemGradeType grade)
		{
			Table_EquipEnhanceTier.Row row;
			if (_tierByGrade.TryGetValue(grade, out row) == false)
			{
				return 0;
			}

			return row.MaxLevel;
		}

		// 현재 '레벨' 이 속한 비용 티어. 어느 구간에도 속하지 않으면 None.
		public static Table_EquipEnhanceTier.Row GetTierByLevel(int level)
		{
			for (int i = 0; i < _tiersByLevel.Count; i++)
			{
				Table_EquipEnhanceTier.Row row = _tiersByLevel[i];
				if (level >= row.MinLevel && level <= row.MaxLevel)
				{
					return row;
				}
			}

			return null;
		}

		public static Table_EquipEnhance.Row GetEnhance(EquipSlotTypes slot, EquipEnhanceTier tier)
		{
			SlotKey key;
			key.slot = slot;
			key.sub = (int)tier;

			Table_EquipEnhance.Row row;
			_enhances.TryGetValue(key, out row);
			return row;
		}

		public static Table_EquipPromotion.Row GetPromotion(EquipSlotTypes slot, ItemGradeType fromGrade)
		{
			SlotKey key;
			key.slot = slot;
			key.sub = (int)fromGrade;

			Table_EquipPromotion.Row row;
			_promotions.TryGetValue(key, out row);
			return row;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void buildOptions()
		{
			Dictionary<int, Table_EquipOption.Row> all = Table_EquipOption.All();
			Dictionary<int, Table_EquipOption.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipOption.Row row = e.Current.Value;
				if (row.GroupID <= 0 || row.ItemGrade == ItemGradeType.None)
				{
					Debug.LogWarning($"[EquipmentCatalog] EquipOption {row.ID} 의 GroupID/ItemGrade 가 비었습니다.");
					continue;
				}

				GradeKey key;
				key.group = row.GroupID;
				key.grade = row.ItemGrade;
				_options[key] = row;
			}
		}

		private static void buildTiers()
		{
			Dictionary<EquipEnhanceTier, Table_EquipEnhanceTier.Row> all = Table_EquipEnhanceTier.All();
			Dictionary<EquipEnhanceTier, Table_EquipEnhanceTier.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipEnhanceTier.Row row = e.Current.Value;
				if (row.ID == EquipEnhanceTier.None)
				{
					continue;
				}

				if (row.Grade != ItemGradeType.None)
				{
					_tierByGrade[row.Grade] = row;
				}

				_tiersByLevel.Add(row);
			}

			_tiersByLevel.Sort(compareTierMinLevel);
		}

		private static int compareTierMinLevel(Table_EquipEnhanceTier.Row a, Table_EquipEnhanceTier.Row b)
		{
			return a.MinLevel.CompareTo(b.MinLevel);
		}

		private static void buildEnhances()
		{
			Dictionary<int, Table_EquipEnhance.Row> all = Table_EquipEnhance.All();
			Dictionary<int, Table_EquipEnhance.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipEnhance.Row row = e.Current.Value;
				if (row.Category == EquipSlotTypes.None || row.EnhanceTier == EquipEnhanceTier.None)
				{
					continue;
				}

				SlotKey key;
				key.slot = row.Category;
				key.sub = (int)row.EnhanceTier;
				_enhances[key] = row;
			}
		}

		private static void buildPromotions()
		{
			Dictionary<int, Table_EquipPromotion.Row> all = Table_EquipPromotion.All();
			Dictionary<int, Table_EquipPromotion.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipPromotion.Row row = e.Current.Value;
				if (row.Category == EquipSlotTypes.None || row.FromGrade == ItemGradeType.None)
				{
					continue;
				}

				SlotKey key;
				key.slot = row.Category;
				key.sub = (int)row.FromGrade;
				_promotions[key] = row;
			}
		}
	}
}
