using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Items
{
	// 장비 테이블 정적 조회 캐시 — 런타임 조회 키가 테이블 기본 키와 다른 것들을 인덱싱한다.
	//
	// EquipOption 의 실제 조회 키는 (GroupID, ItemGrade) 다 (아이템 설계 3.4).
	// 강화·승급·전이 비용 인덱스는 서버와 같이 쓰도록 공유 EquipmentGrowthRules 로 옮겼다.
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

		// (EquipOptionGroupID, ItemGrade) → 옵션 행
		private static readonly Dictionary<GradeKey, Table_EquipOption.Row> _options =
			new Dictionary<GradeKey, Table_EquipOption.Row>(new GradeKeyComparer());

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_options.Clear();

			buildOptions();

			_built = true;
			Debug.Log($"[EquipmentCatalog] 구축 완료 — 옵션:{_options.Count}");
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
	}
}
