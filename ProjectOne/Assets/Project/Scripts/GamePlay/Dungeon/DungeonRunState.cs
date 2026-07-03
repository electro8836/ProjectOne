using System.Collections.Generic;
using ProjectOne.Event;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 1회성 던전 런(로그라이트) 상태 홀더. 던전 진입 시 Reset, 종료 시 소멸(서버 미저장).
	// 던전 임시재화(MagicEssence)와 카드스킬 장착슬롯(최대 6칸)을 보유한다. 슬롯은 던전 종료 시 사라진다.
	public sealed class DungeonRunState : Singleton<DungeonRunState>
	{
		// 카드스킬 장착슬롯 수
		public const int SlotCount = 6;

		// 구매창 후보 카드 수 (UI 의 카드 항목 수와 일치)
		public const int ShopCardCount = 3;

		// 빈 슬롯/검색 실패 표시
		public const int InvalidSlot = -1;

		// 카드스킬 장착슬롯 1칸 상태. investedEssence 는 판매 시 반값 환급 기준(레벨업 구매가 누적).
		public struct CardSkillSlot
		{
			public int cardSkillId;
			public int level;
			public int investedEssence;
		}

		// 구매창 후보 1칸 상태 — 닫았다 다시 열어도 잠금(고정) 카드와 위치를 유지하기 위해 런 상태로 보관.
		public struct ShopCardState
		{
			public int cardSkillId;
			public bool locked;
		}

		// 던전 임시재화 보유량
		private int _essence;

		// 카드스킬 장착슬롯 (고정 6칸, cardSkillId<=0 이면 빈 슬롯)
		private readonly CardSkillSlot[] _slots = new CardSkillSlot[SlotCount];

		// 구매창 후보 상태 (고정 3칸, 잠금 카드만 유지 의미)
		private readonly ShopCardState[] _shopCards = new ShopCardState[ShopCardCount];

		// 리롤 횟수 (스테이지 경계에서 리셋, 스테이지 진행 중에는 누적)
		private int _rerollCount;

		// 이번 런에서 사용한 부활 횟수 (던전 종료 시 초기화)
		private int _reviveUsedCount;

		// 클리어한 스테이지 보상 누적 (던전 종료 결과창에 표시, 지급은 이후)
		private readonly List<DungeonRewardResult> _accumulatedRewards = new List<DungeonRewardResult>();

		// 다음 스테이지 선택 화면에 뜬 2개 후보의 롤 보상(표시용, 후속 StageSelectUI 가 조회)
		private int _optionStageIdA;
		private int _optionRewardItemIdA;
		private int _optionStageIdB;
		private int _optionRewardItemIdB;

		private DungeonRunState()
		{
		}

		public int EssenceAmount => _essence;
		public int RerollCount => _rerollCount;
		public int ReviveUsedCount => _reviveUsedCount;
		public IReadOnlyList<DungeonRewardResult> AccumulatedRewards => _accumulatedRewards;

		// ── 임시재화 ──────────────────────────────────────────────────

		// 임시재화 획득
		public void AddEssence(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			_essence += amount;
			publishEssenceChanged();
		}

		// 임시재화 소비 — 부족하면 false
		public bool TrySpendEssence(int cost)
		{
			if (cost <= 0 || _essence < cost)
			{
				return false;
			}

			_essence -= cost;
			publishEssenceChanged();
			return true;
		}

		// ── 리롤 ──────────────────────────────────────────────────────

		// 리롤 1회 소진 반영 (다음 가격 산정용 카운터 증가)
		public void IncrementRerollCount()
		{
			_rerollCount++;
		}

		// 스테이지 경계에서 리롤 가격을 최초 비용으로 되돌린다
		public void ResetRerollCount()
		{
			_rerollCount = 0;
		}

		// ── 부활 ──────────────────────────────────────────────────────

		// 부활 1회 사용 반영
		public void IncrementReviveUsed()
		{
			_reviveUsedCount++;
		}

		// ── 카드스킬 슬롯 ─────────────────────────────────────────────

		// 슬롯 1칸 조회 (읽기용 복사본)
		public CardSkillSlot GetSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= SlotCount)
			{
				return default(CardSkillSlot);
			}

			return _slots[slotIndex];
		}

		// 해당 카드스킬이 장착된 슬롯 인덱스 (없으면 InvalidSlot)
		public int FindSlotIndex(int cardSkillId)
		{
			if (cardSkillId <= 0)
			{
				return InvalidSlot;
			}

			for (int i = 0; i < SlotCount; i++)
			{
				if (_slots[i].cardSkillId == cardSkillId)
				{
					return i;
				}
			}

			return InvalidSlot;
		}

		// 첫 빈 슬롯 인덱스 (없으면 InvalidSlot)
		public int FirstEmptySlotIndex()
		{
			for (int i = 0; i < SlotCount; i++)
			{
				if (_slots[i].cardSkillId <= 0)
				{
					return i;
				}
			}

			return InvalidSlot;
		}

		// 빈 슬롯에 신규 장착 (level=1 구매). 성공 시 슬롯 인덱스, 빈 슬롯 없으면 InvalidSlot.
		public int EquipToEmptySlot(int cardSkillId, int level, int paidEssence)
		{
			if (cardSkillId <= 0)
			{
				return InvalidSlot;
			}

			int slotIndex = FirstEmptySlotIndex();
			if (slotIndex == InvalidSlot)
			{
				return InvalidSlot;
			}

			_slots[slotIndex].cardSkillId = cardSkillId;
			_slots[slotIndex].level = level;
			_slots[slotIndex].investedEssence = paidEssence;
			publishSlotChanged(slotIndex);
			return slotIndex;
		}

		// 슬롯 레벨업 (중복 구매). 구매가를 투자액에 누적.
		public void LevelUpSlot(int slotIndex, int paidEssence)
		{
			if (slotIndex < 0 || slotIndex >= SlotCount || _slots[slotIndex].cardSkillId <= 0)
			{
				return;
			}

			_slots[slotIndex].level += 1;
			_slots[slotIndex].investedEssence += paidEssence;
			publishSlotChanged(slotIndex);
		}

		// 슬롯 비우기 (판매)
		public void ClearSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= SlotCount)
			{
				return;
			}

			_slots[slotIndex] = default(CardSkillSlot);
			publishSlotChanged(slotIndex);
		}

		// ── 구매창 후보 상태 (잠금 카드 유지용) ───────────────────────

		public ShopCardState GetShopCard(int index)
		{
			if (index < 0 || index >= ShopCardCount)
			{
				return default(ShopCardState);
			}

			return _shopCards[index];
		}

		// 구매창 닫을 때 자리별 카드/잠금 상태 저장 (미잠금은 cardSkillId=0 으로 비워 다음에 새로 뽑히게)
		public void SetShopCard(int index, int cardSkillId, bool locked)
		{
			if (index < 0 || index >= ShopCardCount)
			{
				return;
			}

			_shopCards[index].cardSkillId = cardSkillId;
			_shopCards[index].locked = locked;
		}

		// ── 보상 ──────────────────────────────────────────────────────

		// 클리어 스테이지 보상 누적
		public void AddRewards(IReadOnlyList<DungeonRewardResult> rewards)
		{
			if (rewards == null)
			{
				return;
			}

			for (int i = 0; i < rewards.Count; i++)
			{
				_accumulatedRewards.Add(rewards[i]);
			}
		}

		// 선택 화면 2개 후보의 롤 보상 등록 (단일 후보면 B 를 0 으로)
		public void SetStageOptionRewards(int stageIdA, int rewardIdA, int stageIdB, int rewardIdB)
		{
			_optionStageIdA = stageIdA;
			_optionRewardItemIdA = rewardIdA;
			_optionStageIdB = stageIdB;
			_optionRewardItemIdB = rewardIdB;
		}

		// 후보 stageId 에 배정된 롤 보상 RewardItemId (없으면 0)
		public int GetStageOptionRewardItemId(int stageId)
		{
			if (stageId > 0 && stageId == _optionStageIdA)
			{
				return _optionRewardItemIdA;
			}

			if (stageId > 0 && stageId == _optionStageIdB)
			{
				return _optionRewardItemIdB;
			}

			return 0;
		}

		// ── 런 초기화 ─────────────────────────────────────────────────

		// 던전 진입 시 임시재화/슬롯/구매창 상태 모두 초기화
		public void Reset()
		{
			_essence = 0;
			_rerollCount = 0;
			_reviveUsedCount = 0;
			for (int i = 0; i < SlotCount; i++)
			{
				_slots[i] = default(CardSkillSlot);
			}

			for (int i = 0; i < ShopCardCount; i++)
			{
				_shopCards[i] = default(ShopCardState);
			}

			_accumulatedRewards.Clear();
			_optionStageIdA = 0;
			_optionRewardItemIdA = 0;
			_optionStageIdB = 0;
			_optionRewardItemIdB = 0;

			publishEssenceChanged();
			EventManager.Instance.Publish(new CardSkillSlotChangedEvent(-1, 0, 0));
		}

		private void publishEssenceChanged()
		{
			EventManager.Instance.Publish(new DungeonEssenceChangedEvent(_essence));
		}

		private void publishSlotChanged(int slotIndex)
		{
			CardSkillSlot slot = _slots[slotIndex];
			EventManager.Instance.Publish(new CardSkillSlotChangedEvent(slotIndex, slot.cardSkillId, slot.level));
		}
	}
}
