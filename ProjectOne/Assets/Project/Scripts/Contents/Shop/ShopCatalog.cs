using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shop
{
	// 상점 정적 조회 캐시.
	//
	// 테이블은 카테고리(탭) → 그룹(제목) → 상품 의 3단이다.
	// ShopCategory 는 enum 키, ShopGoodsGroup 은 Category 로 카테고리에 매달리고,
	// ShopGoods 는 GroupID 로 그룹에 매달린다.
	//
	// RewardCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class ShopCatalog
	{
		private static readonly List<Table_ShopCategory.Row> _categories = new List<Table_ShopCategory.Row>();
		private static readonly Dictionary<ShopCategory, List<Table_ShopGoodsGroup.Row>> _groupsByCategory = new Dictionary<ShopCategory, List<Table_ShopGoodsGroup.Row>>();
		private static readonly Dictionary<int, List<Table_ShopGoods.Row>> _goodsByGroup = new Dictionary<int, List<Table_ShopGoods.Row>>();

		private static readonly List<Table_ShopGoodsGroup.Row> _emptyGroups = new List<Table_ShopGoodsGroup.Row>();
		private static readonly List<Table_ShopGoods.Row> _emptyGoods = new List<Table_ShopGoods.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_categories.Clear();
			_groupsByCategory.Clear();
			_goodsByGroup.Clear();

			buildCategories();
			buildGroups();
			buildGoods();

			_built = true;
		}

		// 노출할 탭 목록 — IsEnable 필터 + Order 오름차순.
		public static IReadOnlyList<Table_ShopCategory.Row> GetCategories()
		{
			return _categories;
		}

		// 카테고리에 속한 그룹 목록 — ID 오름차순(그룹 테이블에 Order 가 없어 ID 가 곧 표시 순서다).
		public static IReadOnlyList<Table_ShopGoodsGroup.Row> GetGroups(ShopCategory category)
		{
			List<Table_ShopGoodsGroup.Row> list;
			if (_groupsByCategory.TryGetValue(category, out list) == false)
			{
				return _emptyGroups;
			}

			return list;
		}

		// 그룹에 속한 상품 목록 — ID 오름차순.
		public static IReadOnlyList<Table_ShopGoods.Row> GetGoods(int groupId)
		{
			List<Table_ShopGoods.Row> list;
			if (_goodsByGroup.TryGetValue(groupId, out list) == false)
			{
				return _emptyGoods;
			}

			return list;
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private static void buildCategories()
		{
			Dictionary<ShopCategory, Table_ShopCategory.Row> all = Table_ShopCategory.All();
			Dictionary<ShopCategory, Table_ShopCategory.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ShopCategory.Row row = e.Current.Value;
				if (row.ID == ShopCategory.None || row.IsEnable == false)
				{
					continue;
				}

				_categories.Add(row);
			}

			sortByOrder(_categories);
		}

		private static void buildGroups()
		{
			Dictionary<int, Table_ShopGoodsGroup.Row> all = Table_ShopGoodsGroup.All();
			Dictionary<int, Table_ShopGoodsGroup.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ShopGoodsGroup.Row row = e.Current.Value;
				if (row.Category == ShopCategory.None)
				{
					continue;
				}

				List<Table_ShopGoodsGroup.Row> list;
				if (_groupsByCategory.TryGetValue(row.Category, out list) == false)
				{
					list = new List<Table_ShopGoodsGroup.Row>();
					_groupsByCategory.Add(row.Category, list);
				}

				list.Add(row);
			}

			Dictionary<ShopCategory, List<Table_ShopGoodsGroup.Row>>.Enumerator groupEnum = _groupsByCategory.GetEnumerator();
			while (groupEnum.MoveNext() == true)
			{
				sortGroupsById(groupEnum.Current.Value);
			}
		}

		private static void buildGoods()
		{
			Dictionary<int, Table_ShopGoods.Row> all = Table_ShopGoods.All();
			Dictionary<int, Table_ShopGoods.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ShopGoods.Row row = e.Current.Value;
				if (row.GroupID <= 0)
				{
					continue;
				}

				List<Table_ShopGoods.Row> list;
				if (_goodsByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_ShopGoods.Row>();
					_goodsByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
			}

			Dictionary<int, List<Table_ShopGoods.Row>>.Enumerator goodsEnum = _goodsByGroup.GetEnumerator();
			while (goodsEnum.MoveNext() == true)
			{
				sortGoodsById(goodsEnum.Current.Value);
			}
		}

		// Dictionary 순회 순서는 보장되지 않는다 — 표시 순서는 정렬로 확정한다.
		// 목록이 수십 건이라 삽입정렬로 충분하고, Comparison 델리게이트(람다)를 만들지 않아도 된다.
		private static void sortByOrder(List<Table_ShopCategory.Row> list)
		{
			for (int i = 1; i < list.Count; i++)
			{
				Table_ShopCategory.Row key = list[i];
				int j = i - 1;
				while (j >= 0 && list[j].Order > key.Order)
				{
					list[j + 1] = list[j];
					j--;
				}

				list[j + 1] = key;
			}
		}

		private static void sortGroupsById(List<Table_ShopGoodsGroup.Row> list)
		{
			for (int i = 1; i < list.Count; i++)
			{
				Table_ShopGoodsGroup.Row key = list[i];
				int j = i - 1;
				while (j >= 0 && list[j].ID > key.ID)
				{
					list[j + 1] = list[j];
					j--;
				}

				list[j + 1] = key;
			}
		}

		private static void sortGoodsById(List<Table_ShopGoods.Row> list)
		{
			for (int i = 1; i < list.Count; i++)
			{
				Table_ShopGoods.Row key = list[i];
				int j = i - 1;
				while (j >= 0 && list[j].ID > key.ID)
				{
					list[j + 1] = list[j];
					j--;
				}

				list[j + 1] = key;
			}
		}
	}
}
