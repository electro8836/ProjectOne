using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Costumes
{
	// 코스튬 정적 조회 캐시.
	//
	// 런타임 판정에 필요한 것 두 가지를 인덱싱한다.
	//   기본 바디      착용 ID 가 0일 때 대신 쓸 행 (IsDefault)
	//   타입/직업 제한  무기 코스튬이 지금 든 무기에 맞는가
	//
	// MasteryCatalog / MonsterCatalog 와 같은 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class CostumeCatalog
	{
		// 착용 ID 0 일 때 쓰는 기본 바디 코스튬. IsDefault 로 지정한다.
		private static Table_Costume.Row _defaultBody;

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		// 기본 바디 코스튬. 데이터에 없으면 null — 호출자는 프리팹 원본으로 폴백한다.
		public static Table_Costume.Row DefaultBody
		{
			get { return _defaultBody; }
		}

		public static void Build()
		{
			_defaultBody = null;

			Dictionary<int, Table_Costume.Row> all = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Costume.Row row = e.Current.Value;
				if (row.IsDefault == true && row.CostumeType == CostumeType.Body && _defaultBody == null)
				{
					_defaultBody = row;
				}
			}

			_built = true;
			Debug.Log($"[CostumeCatalog] 구축 완료 — 코스튬:{all.Count} 기본바디:{(_defaultBody != null ? _defaultBody.ID.ToString() : "없음")}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static Table_Costume.Row Get(int costumeId)
		{
			if (costumeId <= 0)
			{
				return null;
			}

			return Table_Costume.Get(costumeId);
		}

		public static bool IsBody(int costumeId)
		{
			Table_Costume.Row row = Get(costumeId);
			return row != null && row.CostumeType == CostumeType.Body;
		}

		public static bool IsWeapon(int costumeId)
		{
			Table_Costume.Row row = Get(costumeId);
			return row != null && row.CostumeType == CostumeType.Weapon;
		}

		// 무기 코스튬을 지금 든 무기에 씌울 수 있는가 — 직업(WeaponType) 제한.
		//
		// 착용 자체를 막지는 않는다. 안 맞는 무기를 들고 있는 동안만 장비 무기가 보이고,
		// 맞는 무기로 갈아끼우면 코스튬이 다시 나타난다. 그래서 표시 시점마다 이 판정을 한다.
		public static bool CanShowWeapon(int costumeId, WeaponType equipped)
		{
			Table_Costume.Row row = Get(costumeId);
			if (row == null || row.CostumeType != CostumeType.Weapon)
			{
				return false;
			}

			if (equipped == WeaponType.None)
			{
				// 무기를 안 들었으면 무기 스킨도 보이지 않는다.
				return false;
			}

			return row.WeaponType == equipped;
		}

		// ── 검증 ──────────────────────────────────────────────────────

		private static void validate()
		{
			int issues = 0;
			int defaultBodyCount = 0;

			Dictionary<int, Table_Costume.Row> all = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Costume.Row row = e.Current.Value;

				if (row.CostumeType == CostumeType.None)
				{
					Debug.LogWarning($"[CostumeCatalog] 코스튬 {row.ID}({row.Name}) 의 CostumeType 이 없습니다.");
					issues++;
				}

				if (string.IsNullOrEmpty(row.SetAddress) == true)
				{
					Debug.LogWarning($"[CostumeCatalog] 코스튬 {row.ID}({row.Name}) 에 SetAddress 가 없습니다 — 외형이 바뀌지 않습니다.");
					issues++;
				}

				if (row.CostumeType == CostumeType.Weapon && row.WeaponType == WeaponType.None)
				{
					Debug.LogWarning($"[CostumeCatalog] 무기 코스튬 {row.ID}({row.Name}) 에 WeaponType 이 없습니다 — 어떤 무기에도 표시되지 않습니다.");
					issues++;
				}

				if (row.CostumeType == CostumeType.Body && row.WeaponType != WeaponType.None)
				{
					Debug.LogWarning($"[CostumeCatalog] 바디 코스튬 {row.ID}({row.Name}) 에 WeaponType 이 있습니다 — 쓰이지 않는 값입니다.");
					issues++;
				}

				if (row.IsDefault == true)
				{
					if (row.CostumeType != CostumeType.Body)
					{
						Debug.LogWarning($"[CostumeCatalog] IsDefault 인 코스튬 {row.ID}({row.Name}) 이 Body 가 아닙니다.");
						issues++;
					}
					else
					{
						defaultBodyCount++;
					}
				}
			}

			if (defaultBodyCount != 1)
			{
				// 0이면 코스튬을 벗었을 때 돌아갈 외형이 없고, 2개 이상이면 어느 쪽이 쓰일지 데이터로 알 수 없다.
				Debug.LogWarning($"[CostumeCatalog] IsDefault 인 바디 코스튬이 {defaultBodyCount}개입니다 — 정확히 1개여야 합니다.");
				issues++;
			}

			if (issues > 0)
			{
				Debug.LogWarning($"[CostumeCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}
	}
}
