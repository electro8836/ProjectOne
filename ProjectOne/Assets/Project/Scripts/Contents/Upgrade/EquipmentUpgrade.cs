using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Currency;
using ProjectOne.Items;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Upgrade
{
	// 비용 1건 — 재화. UI 가 부족분을 표시할 수 있도록 보유량도 함께 싣는다.
	// Craft 테이블 이관으로 강화 재료(강화주문서·승급석 등)가 아이템에서 재화로 승격되어
	// 아이템 비용은 더 이상 존재하지 않는다.
	public struct UpgradeCost
	{
		public EDT.Currency currency;
		public int amount;
		public int owned;

		public bool IsEnough
		{
			get { return owned >= amount; }
		}
	}

	// 장비 강화 · 승급 (아이템 설계 6장).
	//
	//   강화 : 레벨 +1. 상한은 현재 '등급' 의 ItemEnhanceTier.MaxLevel.
	//   승급 : 등급 +1. 레벨·품질은 그대로 유지된다. 강화 레벨이 현재 등급 상한일 때만 가능하다.
	//
	// 판정·비용은 서버와 같은 공유 규칙(EquipmentGrowthRules)에 위임한다.
	// 실행은 서버 권위다 — 클라는 미리 판정해 요청하고, 성공 응답을 ApplyGrowthResponse 로 반영한다.
	public static class EquipmentUpgrade
	{
		// 비용 조회용 공유 버퍼
		private static readonly List<CurrencyCost> _rawCosts = new List<CurrencyCost>(3);

		// ── 강화 ──────────────────────────────────────────────────────

		// 현재 등급 기준 강화 레벨 상한. 0 이면 티어 데이터가 없어 강화 불가.
		public static int GetMaxLevel(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return 0;
			}

			return EquipmentGrowthRules.GetMaxLevel(instance.grade);
		}

		public static bool CanEnhance(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return false;
			}

			return EquipmentGrowthRules.CanEnhance(instance.itemId, instance.grade, instance.level);
		}

		// 다음 강화 1회의 비용을 buffer 에 채운다. 행을 못 찾으면 비운다.
		public static void GetEnhanceCost(EquipmentInstance instance, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (instance == null)
			{
				return;
			}

			EquipmentGrowthRules.GetEnhanceCost(instance.itemId, instance.level, _rawCosts);
			if (_rawCosts.Count == 0)
			{
				Debug.LogWarning($"[EquipmentUpgrade] 강화 비용 행 없음 — item:{instance.itemId} level:{instance.level}");
			}

			FillCosts(_rawCosts, buffer);
		}

		// ── 승급 ──────────────────────────────────────────────────────

		// 승급 후 등급. None 이면 승급 불가 (Mythic 이거나 MaxGrade 도달).
		public static ItemGradeType GetNextGrade(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return ItemGradeType.None;
			}

			return EquipmentGrowthRules.GetNextGrade(instance.itemId, instance.grade);
		}

		// 승급이 막힌 이유. UI 가 버튼 상태와 문구를 이걸로 결정한다 (TransferBlock 관례).
		public static PromoteBlock GetPromoteBlock(EquipmentInstance instance)
		{
			if (instance == null)
			{
				return PromoteBlock.Invalid;
			}

			return EquipmentGrowthRules.GetPromoteBlock(instance.itemId, instance.grade, instance.level);
		}

		public static bool CanPromote(EquipmentInstance instance)
		{
			return GetPromoteBlock(instance) == PromoteBlock.None;
		}

		public static void GetPromoteCost(EquipmentInstance instance, List<UpgradeCost> buffer)
		{
			if (buffer == null)
			{
				return;
			}

			buffer.Clear();
			if (instance == null)
			{
				return;
			}

			EquipmentGrowthRules.GetPromoteCost(instance.itemId, instance.grade, _rawCosts);
			FillCosts(_rawCosts, buffer);
		}

		// ── 비용 ──────────────────────────────────────────────────────

		public static bool IsAffordable(List<UpgradeCost> costs)
		{
			if (costs == null || costs.Count == 0)
			{
				return false;
			}

			for (int i = 0; i < costs.Count; i++)
			{
				if (costs[i].IsEnough == false)
				{
					return false;
				}
			}

			return true;
		}

		// ── 서버 응답 반영 ─────────────────────────────────────────────

		// 강화·승급·전이 성공 응답 — 차감량만큼 재화를 빼고, 바뀐 장비는 서버 최종 상태로 덮는다.
		public static void ApplyGrowthResponse(EquipmentGrowthResponse response)
		{
			if (response.spent != null)
			{
				for (int i = 0; i < response.spent.Length; i++)
				{
					CurrencyAmountDto spent = response.spent[i];
					if (CurrencyManager.Instance.TrySpend((EDT.Currency)spent.currencyId, spent.amount) == false)
					{
						Debug.LogWarning($"[EquipmentUpgrade] 서버 차감분이 로컬 보유량보다 큽니다 — currency:{spent.currencyId} amount:{spent.amount}");
					}
				}
			}

			if (response.equipments == null)
			{
				return;
			}

			for (int i = 0; i < response.equipments.Length; i++)
			{
				EquipmentInstanceDto dto = response.equipments[i];
				EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(dto.uid);
				if (instance == null)
				{
					Debug.LogWarning($"[EquipmentUpgrade] 응답 장비를 찾을 수 없습니다 — uid:{dto.uid}");
					continue;
				}

				instance.grade = (ItemGradeType)dto.grade;
				instance.level = dto.level;
				Account.Instance.Inventory.NotifyEquipmentChanged(instance.uid);
				Account.Instance.Loadout.ReapplyEquipped(instance.uid);
			}
		}

		// ── 비용 변환 ─────────────────────────────────────────────────

		// 공유 규칙의 비용에 보유량을 붙인다 (전이 비용도 같이 쓴다).
		public static void FillCosts(List<CurrencyCost> source, List<UpgradeCost> buffer)
		{
			for (int i = 0; i < source.Count; i++)
			{
				UpgradeCost cost;
				cost.currency = source[i].currency;
				cost.amount = source[i].amount;
				cost.owned = CurrencyManager.Instance.GetAmount(source[i].currency);
				buffer.Add(cost);
			}
		}
	}
}
