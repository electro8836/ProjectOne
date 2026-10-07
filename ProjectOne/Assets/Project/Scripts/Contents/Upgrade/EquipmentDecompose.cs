using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 장비 분해 — 장비를 없애고 등급·강화에 드는 주 재화의 일부를 돌려받는다.
	//
	// 환급량은 서버가 공유 규칙(EquipmentGrowthRules.GetDecomposeRefund)으로 계산한다.
	// 실행은 서버 권위다 — 클라는 요청만 하고, 성공 응답을 반영한다.
	// 자동 분해만 예외다 — 획득 시점에 서버와 같은 조건으로 각자 판정한다(TryAutoDecompose).
	public static class EquipmentDecompose
	{
		// 자동 분해 환급 계산용 버퍼
		private static readonly List<CurrencyCost> _refundBuffer = new List<CurrencyCost>(1);

		// 착용 중이거나 잠근 장비는 분해할 수 없다 — 먼저 해제해야 한다.
		public static bool CanDecompose(EquipmentInstance instance)
		{
			return instance != null && instance.IsEquipped == false && instance.locked == false;
		}

		// 분해를 요청한다. 응답은 여기서 받아 반영한다 — 요청한 팝업은 응답 전에 닫힌다.
		public static void Request(long uid)
		{
			// 서버 레벨이 로컬과 같아야 환급량이 맞는다 — 아직 보내지 않은 강화 묶음을 앞에 보낸다.
			EnhanceBatcher.Instance.Flush();

			EquipmentDecomposeRequest request = new EquipmentDecomposeRequest();
			request.uid = uid;
			NetworkManager.Instance.RequestEquipmentDecompose(request, onResponse);
		}

		private static void onResponse(bool success, EquipmentDecomposeResponse data, string error)
		{
			if (success == false || data == null)
			{
				Debug.LogWarning($"[EquipmentDecompose] 장비 분해 실패: {error}");
				return;
			}

			removeEquipment(data.uid);
			addGained(data.gained);
		}

		// ── 일괄 분해 ─────────────────────────────────────────────────

		// 조건에 맞는 장비 UID 를 채운다. 착용·잠금 장비와 보관함에 넣어 둔 장비는 빠진다.
		public static void CollectTargets(DecomposeSettingDto setting, List<long> buffer)
		{
			buffer.Clear();

			IReadOnlyList<EquipmentInstance> all = Account.Instance.Inventory.GetAllEquipments();
			for (int i = 0; i < all.Count; i++)
			{
				EquipmentInstance instance = all[i];
				if (CanDecompose(instance) == false || instance.inStash == true)
				{
					continue;
				}

				if (EquipmentDecomposeRules.Matches(setting, instance.grade, instance.quality) == true)
				{
					buffer.Add(instance.uid);
				}
			}
		}

		// 일괄 분해를 요청한다. 응답은 여기서 받아 반영한다.
		public static void RequestAll(List<long> uids)
		{
			EnhanceBatcher.Instance.Flush();

			EquipmentDecomposeAllRequest request = new EquipmentDecomposeAllRequest();
			request.uids = uids.ToArray();
			NetworkManager.Instance.RequestEquipmentDecomposeAll(request, onAllResponse);
		}

		// 서버가 실제로 분해한 장비만 뺀다 — 요청보다 적을 수 있다.
		private static void onAllResponse(bool success, EquipmentDecomposeAllResponse data, string error)
		{
			if (success == false || data == null)
			{
				Debug.LogWarning($"[EquipmentDecompose] 일괄 분해 실패: {error}");
				return;
			}

			if (data.uids != null)
			{
				for (int i = 0; i < data.uids.Length; i++)
				{
					removeEquipment(data.uids[i]);
				}
			}

			addGained(data.gained);
		}

		// ── 자동 분해 ─────────────────────────────────────────────────

		// 갓 얻은 장비가 자동 분해 대상이면 인벤토리에 넣는 대신 환급 재화를 더하고 true.
		// 서버(RewardApplier)가 같은 조건으로 같은 처리를 하므로 요청을 보내지 않는다.
		public static bool TryAutoDecompose(EquipmentInstance instance)
		{
			DecomposeSettingDto setting = Account.Instance.Inventory.DecomposeSetting;
			if (EquipmentDecomposeRules.IsAutoTarget(setting, instance.grade, instance.quality) == false)
			{
				return false;
			}

			// 무엇이 분해됐는지 먼저 알린다 — 로그에서 환급 재화 줄보다 위에 찍힌다.
			EventManager.Instance.Publish(new EquipmentAutoDecomposedEvent(instance.itemId, instance.grade, instance.quality));

			EquipmentGrowthRules.GetDecomposeRefund(instance.itemId, instance.grade, instance.level, _refundBuffer);
			for (int i = 0; i < _refundBuffer.Count; i++)
			{
				addCurrency(_refundBuffer[i].currency, _refundBuffer[i].amount);
			}

			return true;
		}

		// 분해 조건을 바꾼다 — 이 시점부터 자동 분해가 새 조건을 따르고, 서버에도 저장한다.
		public static void SaveSetting(DecomposeSettingDto setting)
		{
			SaveDecomposeSettingRequest request = new SaveDecomposeSettingRequest();
			request.setting = setting;

			// 요청이 처치 배치를 앞에 보낸 뒤에 조건을 바꾼다 — 그 전의 획득은 옛 조건으로 이미 반영됐다.
			NetworkManager.Instance.RequestSaveDecomposeSetting(request, onSettingSaved);
			Account.Instance.Inventory.SetDecomposeSetting(setting);
		}

		private static void onSettingSaved(bool success, SaveDecomposeSettingResponse data, string error)
		{
			if (success == false)
			{
				Debug.LogWarning($"[EquipmentDecompose] 분해 조건 저장 실패: {error}");
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void removeEquipment(long uid)
		{
			if (Account.Instance.Inventory.RemoveEquipment(uid) == false)
			{
				Debug.LogWarning($"[EquipmentDecompose] 분해한 장비를 인벤토리에서 빼지 못했습니다 — uid:{uid}");
			}
		}

		private static void addGained(CurrencyAmountDto[] gained)
		{
			if (gained == null)
			{
				return;
			}

			for (int i = 0; i < gained.Length; i++)
			{
				addCurrency((EDT.Currency)gained[i].currencyId, gained[i].amount);
			}
		}

		// 환급 재화를 더한다. 획득 로그가 여기서 찍힌다.
		private static void addCurrency(EDT.Currency currency, int amount)
		{
			CurrencyManager.Instance.Add(currency, amount);
			EventManager.Instance.Publish(new RewardAcquiredEvent(RewardType.Currency, 0, currency, amount, ItemGradeType.None, 0, false));
		}
	}
}
