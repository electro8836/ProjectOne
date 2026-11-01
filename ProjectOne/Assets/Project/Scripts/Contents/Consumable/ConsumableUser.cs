using UnityEngine;
using ProjectOne.Event;
using ProjectOne.UserData;

namespace ProjectOne.Consumables
{
	public enum ConsumableUseResult
	{
		Success = 0,
		NoData,			// Consumable 행이 없거나 파라미터 해석 실패
		NotOwned,		// 인벤토리에 없다
		Failed			// 효과 자체가 실패 (상한 등)
	}

	// 소모품 사용의 단일 진입점. UI 버튼은 Use() 하나만 부르면 된다.
	//
	// 순서가 중요하다 — 효과를 먼저 굴리고 성공했을 때만 차감한다.
	// 효과가 상한에 막혔는데 아이템이 사라지면 복구할 방법이 없다.
	public static class ConsumableUser
	{
		public static ConsumableUseResult Use(int itemId)
		{
			ConsumableCatalog.BakedConsumable baked = ConsumableCatalog.Get(itemId);
			if (baked == null || baked.isValid == false)
			{
				Debug.LogError($"[ConsumableUser] 사용할 수 없는 소모품입니다 — Item:{itemId}");
				return finish(itemId, ConsumableUseResult.NoData);
			}

			if (Account.Instance.Inventory.GetCount(itemId) <= 0)
			{
				return finish(itemId, ConsumableUseResult.NotOwned);
			}

			ConsumableUseResult result = apply(baked);
			if (result != ConsumableUseResult.Success)
			{
				// 효과가 실패하면 차감도 없다.
				return finish(itemId, result);
			}

			Account.Instance.Inventory.TrySpend(itemId, 1);
			return finish(itemId, ConsumableUseResult.Success);
		}

		// ── 타입별 실행 ───────────────────────────────────────────────

		// 현재 정의된 효과가 없다 — 효과를 추가할 때 분기와 서버 반영 요청을 함께 넣는다 (소모품 설계 5·8장).
		private static ConsumableUseResult apply(ConsumableCatalog.BakedConsumable baked)
		{
			switch (baked.effect)
			{
				default:
					return ConsumableUseResult.NoData;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static ConsumableUseResult finish(int itemId, ConsumableUseResult result)
		{
			EventManager.Instance.Publish(new ConsumableUsedEvent(itemId, result));
			return result;
		}
	}
}
