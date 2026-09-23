using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Items
{
	// 장비 테이블 정적 조회 캐시 — 런타임 조회 키가 테이블 기본 키와 다른 것들을 인덱싱한다.
	//
	// EquipOption 의 실제 조회 키는 (GroupID, ItemGrade) 이고 (아이템 설계 3.4),
	// ItemEnhance 는 (EquipmentType, EnhanceTier), ItemPromotion 은 (EquipmentType, FromGrade),
	// ItemTransfer 는 (EquipmentType, Grade) 다.
	//
	// ItemEnhanceTier 는 **두 방향으로 쓴다** (설계 3.5) — 혼동하면 조용히 틀린다.
	//   - 레벨 상한 : 현재 '등급' 으로 조회 → MaxLevel
	//   - 비용 티어 : 강화 '목표 레벨' 이 속한 구간으로 조회 → EnhanceTier
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

		// (EquipmentType, EnhanceTier) → 강화 비용 행
		private static readonly Dictionary<SlotKey, Table_ItemEnhance.Row> _enhances =
			new Dictionary<SlotKey, Table_ItemEnhance.Row>(new SlotKeyComparer());

		// (EquipmentType, FromGrade) → 승급 비용 행
		private static readonly Dictionary<SlotKey, Table_ItemPromotion.Row> _promotions =
			new Dictionary<SlotKey, Table_ItemPromotion.Row>(new SlotKeyComparer());

		// (EquipmentType, Grade) → 전이 비용 행
		private static readonly Dictionary<SlotKey, Table_ItemTransfer.Row> _transfers =
			new Dictionary<SlotKey, Table_ItemTransfer.Row>(new SlotKeyComparer());

		// 등급 → 티어 행 (레벨 상한 조회용)
		private static readonly Dictionary<ItemGradeType, Table_ItemEnhanceTier.Row> _tierByGrade =
			new Dictionary<ItemGradeType, Table_ItemEnhanceTier.Row>();

		// 레벨 구간 순회용 (비용 티어 조회용) — MinLevel 오름차순
		private static readonly List<Table_ItemEnhanceTier.Row> _tiersByLevel = new List<Table_ItemEnhanceTier.Row>();

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
			_transfers.Clear();
			_tierByGrade.Clear();
			_tiersByLevel.Clear();

			buildOptions();
			buildTiers();
			buildEnhances();
			buildPromotions();
			buildTransfers();

			_built = true;
			Debug.Log($"[EquipmentCatalog] 구축 완료 — 옵션:{_options.Count} 티어:{_tiersByLevel.Count} 강화:{_enhances.Count} 승급:{_promotions.Count} 전이:{_transfers.Count}");
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
			Table_ItemEnhanceTier.Row row;
			if (_tierByGrade.TryGetValue(grade, out row) == false)
			{
				return 0;
			}

			return row.MaxLevel;
		}

		// 해당 레벨이 속한 비용 티어. 어느 구간에도 속하지 않으면 null.
		public static Table_ItemEnhanceTier.Row GetTierByLevel(int level)
		{
			for (int i = 0; i < _tiersByLevel.Count; i++)
			{
				Table_ItemEnhanceTier.Row row = _tiersByLevel[i];
				if (level >= row.MinLevel && level <= row.MaxLevel)
				{
					return row;
				}
			}

			return null;
		}

		public static Table_ItemEnhance.Row GetEnhance(EquipSlotTypes slot, ItemEnhanceTier tier)
		{
			SlotKey key;
			key.slot = slot;
			key.sub = (int)tier;

			Table_ItemEnhance.Row row;
			_enhances.TryGetValue(key, out row);
			return row;
		}

		public static Table_ItemPromotion.Row GetPromotion(EquipSlotTypes slot, ItemGradeType fromGrade)
		{
			SlotKey key;
			key.slot = slot;
			key.sub = (int)fromGrade;

			Table_ItemPromotion.Row row;
			_promotions.TryGetValue(key, out row);
			return row;
		}

		public static Table_ItemTransfer.Row GetTransfer(EquipSlotTypes slot, ItemGradeType grade)
		{
			SlotKey key;
			key.slot = slot;
			key.sub = (int)grade;

			Table_ItemTransfer.Row row;
			_transfers.TryGetValue(key, out row);
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
			Dictionary<ItemEnhanceTier, Table_ItemEnhanceTier.Row> all = Table_ItemEnhanceTier.All();
			Dictionary<ItemEnhanceTier, Table_ItemEnhanceTier.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemEnhanceTier.Row row = e.Current.Value;
				if (row.ID == ItemEnhanceTier.None)
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

		private static int compareTierMinLevel(Table_ItemEnhanceTier.Row a, Table_ItemEnhanceTier.Row b)
		{
			return a.MinLevel.CompareTo(b.MinLevel);
		}

		private static void buildEnhances()
		{
			Dictionary<int, Table_ItemEnhance.Row> all = Table_ItemEnhance.All();
			Dictionary<int, Table_ItemEnhance.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemEnhance.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.EnhanceTier == ItemEnhanceTier.None)
				{
					continue;
				}

				SlotKey key;
				key.slot = row.EquipmentType;
				key.sub = (int)row.EnhanceTier;
				_enhances[key] = row;
			}
		}

		private static void buildPromotions()
		{
			Dictionary<int, Table_ItemPromotion.Row> all = Table_ItemPromotion.All();
			Dictionary<int, Table_ItemPromotion.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemPromotion.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.FromGrade == ItemGradeType.None)
				{
					continue;
				}

				SlotKey key;
				key.slot = row.EquipmentType;
				key.sub = (int)row.FromGrade;
				_promotions[key] = row;
			}
		}

		private static void buildTransfers()
		{
			Dictionary<int, Table_ItemTransfer.Row> all = Table_ItemTransfer.All();
			Dictionary<int, Table_ItemTransfer.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemTransfer.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.Grade == ItemGradeType.None)
				{
					continue;
				}

				SlotKey key;
				key.slot = row.EquipmentType;
				key.sub = (int)row.Grade;
				_transfers[key] = row;
			}
		}
	}
}
