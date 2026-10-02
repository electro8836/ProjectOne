using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;

namespace ProjectOne.Pets
{
	// 펫 테이블 조회를 모아 둔 런타임 인덱스 — 표시용 정렬 목록을 들고 있다.
	//
	// 강화·승급 규칙(최대 레벨·비용·승급 행)은 서버와 같이 쓰도록 공유 PetRules 로 옮겼다. 여기서는 위임만 한다.
	public static class PetCatalog
	{
		// ID 오름차순 목록 — Dictionary 는 순서를 보장하지 않으므로 표시 순서는 여기서 고정한다.
		private static readonly List<Table_Pet.Row> _sorted = new List<Table_Pet.Row>();

		private static bool _isBuilt;

		public static bool IsBuilt
		{
			get { return _isBuilt; }
		}

		// 표시 대상 펫 전체 수 — 펫 창 타이틀의 분모.
		public static int Count
		{
			get { return _sorted.Count; }
		}

		public static IReadOnlyList<Table_Pet.Row> AllSorted()
		{
			return _sorted;
		}

		// 테이블 로드 이후 부트에서 1회 호출한다.
		public static void Build()
		{
			_sorted.Clear();

			Dictionary<Pet, Table_Pet.Row> pets = Table_Pet.All();
			Dictionary<Pet, Table_Pet.Row>.Enumerator pe = pets.GetEnumerator();
			while (pe.MoveNext() == true)
			{
				Table_Pet.Row row = pe.Current.Value;
				if (row.ID == Pet.None)
				{
					continue;
				}

				_sorted.Add(row);
			}

			_sorted.Sort(comparePetId);
			_isBuilt = true;
		}

		public static Table_Pet.Row Get(Pet id)
		{
			return Table_Pet.Get(id);
		}

		// 등급이 허용하는 최대 강화 레벨. 행이 없으면 0 — 호출부가 강화를 막는다.
		public static int GetMaxLevel(ItemGradeType grade)
		{
			return PetRules.GetMaxLevel(grade);
		}

		// 다음 등급으로 가는 승급 행. 최고 등급이면 null.
		public static Table_PetPromotion.Row GetPromotion(ItemGradeType from)
		{
			return PetRules.GetPromotion(from);
		}

		// level 레벨에서 다음 레벨로 올리는 비용 (구간 규칙은 PetRules 참고).
		public static bool TryGetEnhanceCost(int level, out EDT.Currency currency, out int amount)
		{
			return PetRules.TryGetEnhanceCost(level, out currency, out amount);
		}

		private static int comparePetId(Table_Pet.Row a, Table_Pet.Row b)
		{
			return ((int)a.ID).CompareTo((int)b.ID);
		}
	}
}
