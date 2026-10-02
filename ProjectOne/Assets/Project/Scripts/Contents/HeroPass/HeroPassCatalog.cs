using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Shared;

namespace ProjectOne.HeroPasses
{
	// 히어로패스 조회 + 데이터 정합성 검증.
	//
	// 레벨·누적 경험치·획득 규칙 인덱스는 서버와 같이 쓰도록 공유 HeroPassRules 가 소유한다. 여기서는 위임만 한다.
	//
	// DailyBonusCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class HeroPassCatalog
	{
		public static int MaxLevel
		{
			get { return HeroPassRules.MaxLevel; }
		}

		public static void Build()
		{
			Debug.Log($"[HeroPassCatalog] 구축 완료 — 레벨:{HeroPassRules.MaxLevel} 획득규칙:{Table_HeroPassExpInfo.All().Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static IReadOnlyList<Table_HeroPass.Row> GetLevels()
		{
			return HeroPassRules.GetLevels();
		}

		// level 에 도달하는 누적 경험치. 0레벨은 0, 범위 밖은 끝값으로 자른다.
		public static int GetTotalExp(int level)
		{
			return HeroPassRules.GetTotalExp(level);
		}

		// 최대 레벨 도달에 필요한 누적 경험치 — 이 이상은 버린다.
		public static int GetMaxExp()
		{
			return HeroPassRules.GetMaxExp();
		}

		// 누적 경험치로 도달한 레벨. 아무것도 못 넘었으면 0.
		public static int GetLevelByExp(int exp)
		{
			return HeroPassRules.GetLevelByExp(exp);
		}

		public static IReadOnlyList<Table_HeroPassExpInfo.Row> GetExpInfos(HeroPassExpType type)
		{
			return HeroPassRules.GetExpInfos(type);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 레벨이 1부터 이어지는지, 누적 경험치가 늘어나는지, 보상 그룹이 비어 있지 않은지,
		// 획득 규칙이 유효한지 본다. 보상 누락은 한 줄로 묶는다(DailyBonusCatalog 와 같은 이유).
		private static void validate()
		{
			Dictionary<int, Table_HeroPassExpInfo.Row>.Enumerator e = Table_HeroPassExpInfo.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_HeroPassExpInfo.Row row = e.Current.Value;
				if (row.ExpType == HeroPassExpType.None)
				{
					Debug.LogWarning($"[HeroPassCatalog] HeroPassExpInfo ID {row.ID} 의 ExpType 이 비어 있다 — 건너뛴다.");
				}
				else if (row.ReqCount <= 0)
				{
					Debug.LogWarning($"[HeroPassCatalog] HeroPassExpInfo ID {row.ID} 의 ReqCount 가 {row.ReqCount} 다 — 건너뛴다.");
				}
			}

			System.Text.StringBuilder missing = new System.Text.StringBuilder();

			IReadOnlyList<Table_HeroPass.Row> levels = HeroPassRules.GetLevels();
			for (int i = 0; i < levels.Count; i++)
			{
				Table_HeroPass.Row row = levels[i];
				int level = i + 1;

				if (row.ID != level)
				{
					Debug.LogWarning($"[HeroPassCatalog] 레벨이 이어지지 않는다 — {level} 이어야 할 자리에 {row.ID} 가 있다.");
				}

				int total = HeroPassRules.GetTotalExp(level);
				int prev = HeroPassRules.GetTotalExp(level - 1);
				if (total <= prev)
				{
					Debug.LogWarning($"[HeroPassCatalog] {level}레벨 TotalExp({total}) 가 이전 레벨보다 크지 않다 — HeroPassLevelExp 를 확인한다.");
				}

				if (row.RewardGroupID_Normal <= 0 || row.RewardGroupID_Advanced <= 0)
				{
					if (missing.Length > 0)
					{
						missing.Append(", ");
					}

					missing.Append(level);
				}
			}

			if (missing.Length > 0)
			{
				Debug.LogWarning($"[HeroPassCatalog] RewardGroupID 가 비어 있다 — {missing}레벨. HeroPass 엑셀을 채워야 한다.");
			}
		}
	}
}
