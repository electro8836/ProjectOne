using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 카드스킬 구매창 로직(데이터 측). 보유 카드 중 구매 가능한 후보를 뽑아 3장 제시하고,
	// 임시재화(MagicEssence)로 구매/레벨업/판매를 처리한 뒤 Hero 에 즉시 재적용한다.
	// 런 1회성이므로 Singleton(순수 C#) — 슬롯 상태는 DungeonRunState 가 보유한다.
	public sealed class DungeonCardShop : Singleton<DungeonCardShop>
	{
		// 구매창에 제시할 후보 1장 (UI 표시용)
		public struct ShopChoice
		{
			public int cardSkillId;
			public int displayLevel;   // 구매 시 적용될 레벨 (신규=1, 레벨업=현재+1)
			public int price;
		}

		// RollChoices 가 제시하는 카드 수
		public const int ChoiceCount = 3;

		// 후보 수집용 재사용 버퍼
		private readonly List<ShopChoice> _candidateBuffer = new List<ShopChoice>();

		private DungeonCardShop()
		{
		}

		// 해금된 후보 중 무작위 count 장을 반환(부족하면 있는 만큼). exclude 에 든 cardSkillId 는 제외(잠금 카드 중복 방지).
		public List<ShopChoice> RollChoices(int count, List<int> exclude)
		{
			collectCandidates(_candidateBuffer);

			if (exclude != null && exclude.Count > 0)
			{
				for (int i = _candidateBuffer.Count - 1; i >= 0; i--)
				{
					if (exclude.Contains(_candidateBuffer[i].cardSkillId) == true)
					{
						_candidateBuffer.RemoveAt(i);
					}
				}
			}

			List<ShopChoice> result = new List<ShopChoice>(count);
			int pick = _candidateBuffer.Count < count ? _candidateBuffer.Count : count;
			for (int i = 0; i < pick; i++)
			{
				int idx = Random.Range(0, _candidateBuffer.Count);
				result.Add(_candidateBuffer[idx]);
				_candidateBuffer.RemoveAt(idx);
			}

			return result;
		}

		// 특정 카드스킬의 현재 구매 정보 조회 — 슬롯에 있으면 레벨업(현재+1), 없으면 신규(레벨1).
		// buyable 은 레벨/슬롯 제약 충족 여부(재화 부족은 별도). 카드 항목 표시 갱신용.
		public bool GetBuyInfo(int cardSkillId, out int displayLevel, out int price, out bool buyable)
		{
			displayLevel = 0;
			price = 0;
			buyable = false;

			Table_CardSkill.Row card = Table_CardSkill.Get(cardSkillId);
			if (card == null)
			{
				return false;
			}

			int slotIndex = DungeonRunState.Instance.FindSlotIndex(cardSkillId);
			if (slotIndex != DungeonRunState.InvalidSlot)
			{
				int nextLevel = DungeonRunState.Instance.GetSlot(slotIndex).level + 1;
				displayLevel = nextLevel;
				price = CardSkillResolver.BuyPriceForLevel(card, nextLevel);
				buyable = canLevelUp(card, nextLevel);
				return true;
			}

			displayLevel = CardSkillResolver.MinLevel;
			price = CardSkillResolver.BuyPriceForLevel(card, CardSkillResolver.MinLevel);
			buyable = DungeonRunState.Instance.FirstEmptySlotIndex() != DungeonRunState.InvalidSlot;
			return true;
		}

		// 카드스킬 구매 — 슬롯에 있으면 레벨업, 없으면 빈 슬롯에 신규 장착. 성공 시 true.
		public bool TryBuy(int cardSkillId)
		{
			Table_CardSkill.Row card = Table_CardSkill.Get(cardSkillId);
			if (card == null)
			{
				return false;
			}

			int slotIndex = DungeonRunState.Instance.FindSlotIndex(cardSkillId);
			if (slotIndex != DungeonRunState.InvalidSlot)
			{
				return tryLevelUp(card, slotIndex);
			}

			return tryEquipNew(card, cardSkillId);
		}

		// 슬롯 판매 — 투자한 임시재화의 반값 환급 후 슬롯 비우고 Hero 재적용. 성공 시 true.
		public bool TrySell(int slotIndex)
		{
			DungeonRunState.CardSkillSlot slot = DungeonRunState.Instance.GetSlot(slotIndex);
			if (slot.cardSkillId <= 0)
			{
				return false;
			}

			int refund = slot.investedEssence / 2;
			DungeonRunState.Instance.ClearSlot(slotIndex);
			DungeonRunState.Instance.AddEssence(refund);
			reapplyHero();
			return true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 슬롯에 장착된 카드 레벨업 (중복 구매)
		private bool tryLevelUp(Table_CardSkill.Row card, int slotIndex)
		{
			DungeonRunState.CardSkillSlot slot = DungeonRunState.Instance.GetSlot(slotIndex);
			int nextLevel = slot.level + 1;
			if (canLevelUp(card, nextLevel) == false)
			{
				return false;
			}

			int price = CardSkillResolver.BuyPriceForLevel(card, nextLevel);
			if (DungeonRunState.Instance.TrySpendEssence(price) == false)
			{
				return false;
			}

			DungeonRunState.Instance.LevelUpSlot(slotIndex, price);
			reapplyHero();
			return true;
		}

		// 빈 슬롯에 신규 장착 (레벨 1)
		private bool tryEquipNew(Table_CardSkill.Row card, int cardSkillId)
		{
			if (DungeonRunState.Instance.FirstEmptySlotIndex() == DungeonRunState.InvalidSlot)
			{
				return false;
			}

			int price = CardSkillResolver.BuyPriceForLevel(card, CardSkillResolver.MinLevel);
			if (DungeonRunState.Instance.TrySpendEssence(price) == false)
			{
				return false;
			}

			DungeonRunState.Instance.EquipToEmptySlot(cardSkillId, CardSkillResolver.MinLevel, price);
			reapplyHero();
			return true;
		}

		// 구매 가능한 후보 수집 — 해금된 전체 카드 중에서.
		// 미장착 카드는 빈 슬롯 유무와 무관하게 후보(구매 가능 여부는 GetBuyInfo 가 별도 판정),
		// 장착 카드는 레벨업 가능(최대레벨/MaxShopLevel 미도달)일 때만. 미해금/장착중 최대레벨은 제외.
		private void collectCandidates(List<ShopChoice> output)
		{
			output.Clear();

			List<Table_CardSkill.Row> all = new List<Table_CardSkill.Row>(Table_CardSkill.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				Table_CardSkill.Row card = all[i];
				if (CardSkillResolver.IsUnlocked(card) == false)
				{
					continue;
				}

				int slotIndex = DungeonRunState.Instance.FindSlotIndex(card.ID);
				if (slotIndex != DungeonRunState.InvalidSlot)
				{
					// 장착됨 → 레벨업 가능할 때만 후보 (최대레벨 도달 시 제외)
					int nextLevel = DungeonRunState.Instance.GetSlot(slotIndex).level + 1;
					if (canLevelUp(card, nextLevel) == true)
					{
						output.Add(makeChoice(card, card.ID, nextLevel));
					}
				}
				else
				{
					// 미장착 → 항상 신규(레벨1) 후보
					output.Add(makeChoice(card, card.ID, CardSkillResolver.MinLevel));
				}
			}
		}

		// 다음 레벨로 올릴 수 있는지 — 최대레벨(5) 및 강화레벨 기반 MaxShopLevel 제한.
		// 로비 강화 이력이 없는(미보유) 해금 카드는 강화레벨을 최소 1로 보정한다.
		private bool canLevelUp(Table_CardSkill.Row card, int nextLevel)
		{
			if (nextLevel > CardSkillResolver.MaxLevel)
			{
				return false;
			}

			int enchantLevel = Mathf.Max(Account.Instance.CardSkillBook.GetEnchantLevel(card.ID), CardSkillResolver.MinLevel);
			int maxShop = CardSkillResolver.MaxShopLevel(card.CardGrade, enchantLevel);
			return nextLevel <= maxShop;
		}

		private static ShopChoice makeChoice(Table_CardSkill.Row card, int cardSkillId, int level)
		{
			ShopChoice choice = new ShopChoice();
			choice.cardSkillId = cardSkillId;
			choice.displayLevel = level;
			choice.price = CardSkillResolver.BuyPriceForLevel(card, level);
			return choice;
		}

		// 구매/판매 후 던전 히어로에 카드스킬을 재적용 (source "CardSkill" 만 Remove→Apply)
		private static void reapplyHero()
		{
			if (UnitContainer.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero != null && hero.IsDead == false)
				{
					HeroAspectRegistry.Instance.Reapply(hero, CardSkillAspect.Source);
				}
			}
		}
	}
}
