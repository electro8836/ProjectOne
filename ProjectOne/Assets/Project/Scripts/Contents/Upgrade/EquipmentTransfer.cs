using System.Collections.Generic;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.Shared;

namespace ProjectOne.Upgrade
{
	// 장비 전이 — 두 장비의 등급과 강화도를 서로 교체한다.
	//
	// 품질(quality)은 전이되지 않고 각자 그대로 남는다.
	// 비용은 두 장비의 등급 중 '높은 쪽' 행을 쓴다 — 저등급 장비를 끼워 비용을 깎지 못하게 한다.
	//
	// 판정·비용은 서버와 같은 공유 규칙(EquipmentGrowthRules)에 위임하고, 재화 부족만 여기서 더 본다.
	// 실행은 서버 권위다 — 성공 응답은 EquipmentUpgrade.ApplyGrowthResponse 로 반영한다.
	public static class EquipmentTransfer
	{
		// 비용 조회용 공유 버퍼
		private static readonly List<CurrencyCost> _rawCosts = new List<CurrencyCost>(1);

		public static TransferBlock GetBlock(EquipmentInstance a, EquipmentInstance b)
		{
			if (a == null || b == null)
			{
				return TransferBlock.Invalid;
			}

			TransferBlock block = EquipmentGrowthRules.GetTransferBlock(a.uid, a.itemId, a.grade, a.level, b.uid, b.itemId, b.grade, b.level);
			if (block != TransferBlock.None)
			{
				return block;
			}

			EquipmentGrowthRules.GetTransferCost(a.itemId, a.grade, b.itemId, b.grade, _rawCosts);
			for (int i = 0; i < _rawCosts.Count; i++)
			{
				if (CurrencyManager.Instance.GetAmount(_rawCosts[i].currency) < _rawCosts[i].amount)
				{
					return TransferBlock.NotEnough;
				}
			}

			return TransferBlock.None;
		}

		// 전이 1회의 비용을 buffer 에 채운다. 행을 못 찾으면 비운다.
		public static void GetCost(EquipmentInstance a, EquipmentInstance b, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (a == null || b == null)
			{
				return;
			}

			EquipmentGrowthRules.GetTransferCost(a.itemId, a.grade, b.itemId, b.grade, _rawCosts);
			EquipmentUpgrade.FillCosts(_rawCosts, buffer);
		}
	}
}
