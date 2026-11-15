using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UI;
using ProjectOne.UserData;

namespace ProjectOne.Consumables
{
	public enum ConsumableUseResult
	{
		Success = 0,
		NoData,			// Consumable 행이 없거나 파라미터 해석 실패
		NotOwned,		// 인벤토리에 없다
		Failed,			// 효과 자체가 실패 (상한·서버 거절 등)
		NoBoss,			// 보스소환석 — 보스 구역이 아니다
		BossAlive,		// 보스소환석 — 보스가 살아 있다
		Pending			// 서버 응답 대기 — 차감과 효과는 응답이 오면 반영한다
	}

	// 소모품 사용의 단일 진입점. UI 버튼은 Use() 하나만 부르면 된다.
	//
	// 순서가 중요하다 — 효과를 먼저 굴리고 성공했을 때만 차감한다.
	// 효과가 상한에 막혔는데 아이템이 사라지면 복구할 방법이 없다.
	// 서버가 판정하는 효과(보스소환석)는 Pending 을 돌려주고, 응답이 성공일 때 차감·효과를 함께 반영한다.
	public static class ConsumableUser
	{
		// 응답 대기 중인 서버 판정 사용 — 응답 전 재사용을 막는다. 0 이면 대기 없음.
		private static int _pendingItemId;

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

			if (_pendingItemId != 0)
			{
				return finish(itemId, ConsumableUseResult.Failed);
			}

			ConsumableUseResult result = apply(baked);
			if (result != ConsumableUseResult.Success)
			{
				// 효과가 실패하면 차감도 없다. 서버 대기면 응답이 차감한다.
				return finish(itemId, result);
			}

			Account.Instance.Inventory.TrySpend(itemId, 1);
			return finish(itemId, ConsumableUseResult.Success);
		}

		// ── 타입별 실행 ───────────────────────────────────────────────

		private static ConsumableUseResult apply(ConsumableCatalog.BakedConsumable baked)
		{
			switch (baked.effect)
			{
				case ConsumeEffect.BossSummon:
					return summonBoss(baked.itemId);

				default:
					return ConsumableUseResult.NoData;
			}
		}

		// 보스소환석 — 히어로가 있는 구역의 보스가 오늘 처치돼 리젠 대기 중일 때만 쓸 수 있다.
		// 처치 기록을 지우면 스포너가 다음 프레임에 보스를 다시 띄운다.
		private static ConsumableUseResult summonBoss(int itemId)
		{
			int spawnId;
			bool killedToday;
			if (WorldDirector.HasInstance == false || WorldDirector.Instance.TryGetCurrentBossSlot(out spawnId, out killedToday) == false)
			{
				return ConsumableUseResult.NoBoss;
			}

			if (killedToday == false)
			{
				return ConsumableUseResult.BossAlive;
			}

			int zoneId = WorldDirector.Instance.CurrentZoneId;

			// 미로그인(오프라인 테스트) — 로컬에서 바로 반영한다.
			if (NetworkManager.Instance.IsLoggedIn == false)
			{
				MonsterRespawnClock.Instance.ClearBossKill(zoneId, spawnId);
				return ConsumableUseResult.Success;
			}

			BossSummonRequest request = new BossSummonRequest();
			request.fieldId = zoneId;
			request.spawnId = spawnId;
			request.itemId = itemId;

			_pendingItemId = itemId;
			NetworkManager.Instance.RequestBossSummon(request, onBossSummonResponse);
			return ConsumableUseResult.Pending;
		}

		private static void onBossSummonResponse(bool success, BossSummonResponse data, string error)
		{
			int itemId = _pendingItemId;
			_pendingItemId = 0;

			if (success == false || data == null)
			{
				Debug.LogWarning($"[ConsumableUser] 보스 소환 실패: {error}");
				if (UIManager.HasInstance == true)
				{
					UIManager.Instance.ShowAlertMessage("보스를 소환하지 못했습니다");
				}

				finish(itemId, ConsumableUseResult.Failed);
				return;
			}

			Account.Instance.Inventory.TrySpend(itemId, 1);
			MonsterRespawnClock.Instance.ClearBossKill(data.fieldId, data.spawnId);
			finish(itemId, ConsumableUseResult.Success);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static ConsumableUseResult finish(int itemId, ConsumableUseResult result)
		{
			EventManager.Instance.Publish(new ConsumableUsedEvent(itemId, result));
			return result;
		}
	}
}
