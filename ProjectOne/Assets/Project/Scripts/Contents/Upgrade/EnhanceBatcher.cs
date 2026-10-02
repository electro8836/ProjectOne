using System.Collections.Generic;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 장비 강화 묶음 전송 — 묶음·되돌림·환불 규칙은 GrowthBatcher 참고. 키는 장비 uid 다.
	public sealed class EnhanceBatcher : GrowthBatcher<EnhanceBatcher>
	{
		private readonly List<UpgradeCost> _costs = new List<UpgradeCost>(2);

		private EnhanceBatcher()
		{
		}

		protected override string LogTag
		{
			get { return "EnhanceBatcher"; }
		}

		// 강화 1회를 즉시 적용하고 묶음에 쌓는다. 적용했으면 true.
		public bool TryEnhance(EquipmentInstance instance)
		{
			if (NetworkManager.Instance.IsLoggedIn == false || EquipmentUpgrade.CanEnhance(instance) == false)
			{
				return false;
			}

			EquipmentUpgrade.GetEnhanceCost(instance, _costs);
			if (EquipmentUpgrade.IsAffordable(_costs) == false)
			{
				return false;
			}

			Apply(instance.uid, instance.level, _costs);
			return true;
		}

		protected override void SetLevel(long key, int level)
		{
			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(key);
			if (instance == null)
			{
				return;
			}

			instance.level = level;
			Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
			Account.Instance.Loadout.ReapplyEquipped(instance.uid);
		}

		protected override void Send(long key, int fromLevel, int count)
		{
			EquipmentEnhanceRequest request = new EquipmentEnhanceRequest();
			request.uid = key;
			request.fromLevel = fromLevel;
			request.count = count;
			NetworkManager.Instance.RequestEquipmentEnhance(request, onResponse);
		}

		private void onResponse(bool success, EquipmentGrowthResponse data, string error)
		{
			EquipmentInstanceDto server = findDto(data);
			OnSendResult(success, (server != null) ? server.level : -1, (data != null) ? data.spent : null, error);
		}

		// 묶음 응답은 대상 장비 1개만 싣는다.
		private static EquipmentInstanceDto findDto(EquipmentGrowthResponse data)
		{
			if (data == null || data.equipments == null || data.equipments.Length == 0)
			{
				return null;
			}

			return data.equipments[0];
		}
	}
}
