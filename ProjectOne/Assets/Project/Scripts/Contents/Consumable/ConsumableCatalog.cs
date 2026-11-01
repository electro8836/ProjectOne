using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Consumables
{
	// 소모품 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// 소모품은 단발성만 둔다 — 지속 버프·회복·상자형은 넣지 않는다(밸런스·공수 대비 단점).
	// 현재 정의된 효과가 없다 — 첫 소모품 효과를 추가할 때 bake / validateParams / ConsumableUser.apply 에 분기를 넣는다.
	//
	// EffectParam_1/2 는 자유 형식 문자열이라 사용할 때마다 파싱하지 않고 Build 시점에 굽는다.
	// RewardCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class ConsumableCatalog
	{
		// 파싱이 끝난 소모품 1개. 타입별로 쓰는 필드가 다르다 — 슬롯의 뜻은 설계 4.2 표가 전부다.
		public sealed class BakedConsumable
		{
			public int itemId;
			public ConsumeEffect effect;

			// 파싱에 실패한 행은 사용 대상에서 제외한다. 경고는 Build 가 이미 냈다.
			public bool isValid;
		}

		private static readonly Dictionary<int, BakedConsumable> _byItem = new Dictionary<int, BakedConsumable>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_byItem.Clear();

			Dictionary<int, Table_Consumable.Row> all = Table_Consumable.All();
			Dictionary<int, Table_Consumable.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedConsumable baked = bake(e.Current.Value);
				if (baked != null)
				{
					_byItem[baked.itemId] = baked;
				}
			}

			_built = true;
			Debug.Log($"[ConsumableCatalog] 구축 완료 — 소모품:{_byItem.Count} / 전체 행 {all.Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static BakedConsumable Get(int itemId)
		{
			if (_built == false)
			{
				Debug.LogError("[ConsumableCatalog] Build() 이전에 조회했습니다. 부트 순서를 확인하세요.");
				return null;
			}

			BakedConsumable baked;
			_byItem.TryGetValue(itemId, out baked);
			return baked;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static BakedConsumable bake(Table_Consumable.Row row)
		{
			if (row == null || row.ID <= 0)
			{
				return null;
			}

			BakedConsumable baked = new BakedConsumable();
			baked.itemId = row.ID;
			baked.effect = row.ConsumeEffect;
			baked.isValid = true;

			// 아직 처리할 효과 종류가 없다 — 모든 행이 사용 대상에서 빠진다.
			switch (row.ConsumeEffect)
			{
				default:
					baked.isValid = false;
					break;
			}

			return baked;
		}

		// ── 검증 ──────────────────────────────────────────────────────
		//
		// 경고 목록이 곧 채워야 할 엑셀 작업 지시서다 (STEP 8·10과 같은 방식).
		// 컨버터로의 승격은 STEP 15.

		private static void validate()
		{
			int issues = 0;
			issues += validateRows();
			issues += validateItemSide();

			if (issues > 0)
			{
				Debug.LogWarning($"[ConsumableCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		private static int validateRows()
		{
			int issues = 0;

			Dictionary<int, BakedConsumable>.Enumerator e = _byItem.GetEnumerator();
			while (e.MoveNext() == true)
			{
				BakedConsumable baked = e.Current.Value;

				Table_Item.Row item = Table_Item.Get(baked.itemId);
				if (item == null)
				{
					Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 에 대응하는 Item 행이 없습니다.");
					issues++;
				}
				else if (item.MainCategory != ItemMainCategory.Consumable)
				{
					Debug.LogWarning($"[ConsumableCatalog] Item {baked.itemId} 의 MainCategory 가 {item.MainCategory} 입니다 — Consumable 이어야 합니다.");
					issues++;
				}
				else if (item.MaxStack == 1)
				{
					// 스택 불가 소모품은 대개 실수다 (설계 8장).
					Debug.LogWarning($"[ConsumableCatalog] Item {baked.itemId} 의 MaxStack 이 1입니다 — 소모품은 대개 스택됩니다.");
					issues++;
				}

				if (baked.effect == ConsumeEffect.None)
				{
					Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 ConsumeEffect 가 비었습니다.");
					issues++;
					continue;
				}

				issues += validateParams(baked);
			}

			return issues;
		}

		private static int validateParams(BakedConsumable baked)
		{
			int issues = 0;

			// 효과별 검증은 효과를 추가할 때 함께 넣는다 (소모품 설계 9장).
			switch (baked.effect)
			{
				default:
					Debug.LogWarning($"[ConsumableCatalog] Consumable {baked.itemId} 의 ConsumeEffect {baked.effect} 를 처리하는 코드가 없습니다.");
					issues++;
					break;
			}

			return issues;
		}

		// MainCategory=Consumable 인데 Consumable 행이 없는 아이템 — 사용 버튼이 죽는다.
		private static int validateItemSide()
		{
			int issues = 0;

			Dictionary<int, Table_Item.Row> items = Table_Item.All();
			Dictionary<int, Table_Item.Row>.Enumerator e = items.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Item.Row item = e.Current.Value;
				if (item.MainCategory != ItemMainCategory.Consumable)
				{
					continue;
				}

				if (_byItem.ContainsKey(item.ID) == false)
				{
					Debug.LogWarning($"[ConsumableCatalog] Item {item.ID}({item.Name}) 는 Consumable 인데 Consumable 행이 없습니다.");
					issues++;
				}
			}

			return issues;
		}
	}
}
