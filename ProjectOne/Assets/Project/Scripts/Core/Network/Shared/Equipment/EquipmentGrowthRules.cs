using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 성장 비용 1건 — 재화와 수량.
	public struct CurrencyCost
	{
		public EDT.Currency currency;
		public int amount;
	}

	// 승급이 거부된 이유.
	public enum PromoteBlock
	{
		None,
		Invalid,		// 테이블 행이 없는 장비
		MaxGrade,		// 최대 등급 — 다음 등급이 없음
		NotMaxLevel,	// 현재 등급의 강화 레벨 상한에 도달하지 않음
	}

	// 전이가 거부된 이유. UI 가 버튼 상태와 문구를 이걸로 결정한다 (PetBook.GetEnhanceBlock 관례).
	public enum TransferBlock
	{
		None,
		Invalid,		// 테이블 행이 없는 장비
		SameInstance,	// 같은 장비를 양쪽으로 지정
		SlotMismatch,	// 착용 슬롯 종류가 다름 (무기 ↔ 장갑 등)
		SameState,		// 등급·강화도가 이미 동일해 교체 의미가 없음
		NoTable,		// ItemTransfer 비용 행 없음
		NotEnough,		// 재화 부족 — 보유량은 호출자가 판정한다(이 규칙은 내지 않는다)
	}

	// 장비 성장(강화·승급·전이)·분해 규칙 중 테이블만 보고 판단하는 부분 (아이템 설계 6장). 클라·서버 공용.
	//
	// 클라는 버튼 상태·비용 표시에 쓰고, 서버는 같은 규칙으로 요청을 검증한 뒤 차감·변경한다.
	// 확률이 없는 결정적 규칙이라 양쪽 결과가 항상 같다.
	//
	// ItemEnhanceTier 는 **두 방향으로 쓴다** (설계 3.5) — 혼동하면 조용히 틀린다.
	//   - 레벨 상한 : 현재 '등급' 으로 조회 → MaxLevel
	//   - 비용 티어 : 강화 '목표 레벨' 이 속한 구간으로 조회 → EnhanceTier
	//
	// 조회 키가 테이블 기본 키와 달라 처음 쓸 때 인덱스를 만든다 —
	// ItemEnhance 는 (EquipmentType, EnhanceTier), ItemPromotion 은 (EquipmentType, FromGrade), ItemTransfer 는 (EquipmentType, Grade).
	public static class EquipmentGrowthRules
	{
		// 분해 시 돌려주는 주 재화 비율
		public const float DecomposeRefundRate = 0.8f;

		private static Dictionary<long, Table_ItemEnhance.Row> _enhances;
		private static Dictionary<long, Table_ItemPromotion.Row> _promotions;
		private static Dictionary<long, Table_ItemTransfer.Row> _transfers;

		// 등급 → 티어 행 (레벨 상한 조회용)
		private static Dictionary<ItemGradeType, Table_ItemEnhanceTier.Row> _tierByGrade;

		// 레벨 구간 순회용 (비용 티어 조회용) — MinLevel 오름차순
		private static List<Table_ItemEnhanceTier.Row> _tiersByLevel;

		// ── 강화 ──────────────────────────────────────────────────────

		// 현재 '등급' 기준 강화 레벨 상한. 티어 행이 없으면 0(강화 불가).
		public static int GetMaxLevel(ItemGradeType grade)
		{
			ensureBuilt();

			Table_ItemEnhanceTier.Row row;
			if (_tierByGrade.TryGetValue(grade, out row) == false)
			{
				return 0;
			}

			return row.MaxLevel;
		}

		public static bool CanEnhance(int itemId, ItemGradeType grade, int level)
		{
			if (Table_Equipment.Get(itemId) == null)
			{
				return false;
			}

			int max = GetMaxLevel(grade);
			return max > 0 && level < max;
		}

		// 다음 강화 1회의 비용을 buffer 에 채운다. 행을 못 찾으면 비운다.
		// 비용 티어는 '올라갈 레벨(level + 1)' 이 속한 구간이다 — 20레벨에서 강화하면 21이 되므로 Tier_2 를 낸다.
		public static void GetEnhanceCost(int itemId, int level, List<CurrencyCost> buffer)
		{
			buffer.Clear();

			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (equipment == null)
			{
				return;
			}

			Table_ItemEnhanceTier.Row tier = getTierByLevel(level + 1);
			if (tier == null)
			{
				return;
			}

			Table_ItemEnhance.Row cost;
			if (_enhances.TryGetValue(slotKey(equipment.EquipSlotType, (int)tier.ID), out cost) == false)
			{
				return;
			}

			addCost(buffer, cost.ReqMainCurrency, cost.ReqMainCost);
			addCost(buffer, cost.ReqCurrency_1, cost.ReqCost_1);
			addCost(buffer, cost.ReqCurrency_2, cost.ReqCost_2);
		}

		// ── 승급 ──────────────────────────────────────────────────────

		// 승급 후 등급. None 이면 승급 불가 (MaxGrade 도달이거나 승급 행 없음).
		public static ItemGradeType GetNextGrade(int itemId, ItemGradeType grade)
		{
			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (equipment == null || (int)grade >= (int)equipment.MaxGrade)
			{
				return ItemGradeType.None;
			}

			Table_ItemPromotion.Row row = getPromotion(equipment.EquipSlotType, grade);
			if (row == null)
			{
				return ItemGradeType.None;
			}

			return row.ToGrade;
		}

		// 승급은 다음 등급이 있고, 현재 등급의 강화 레벨 상한까지 올린 뒤에만 가능하다.
		public static PromoteBlock GetPromoteBlock(int itemId, ItemGradeType grade, int level)
		{
			if (Table_Equipment.Get(itemId) == null)
			{
				return PromoteBlock.Invalid;
			}

			if (GetNextGrade(itemId, grade) == ItemGradeType.None)
			{
				return PromoteBlock.MaxGrade;
			}

			if (level < GetMaxLevel(grade))
			{
				return PromoteBlock.NotMaxLevel;
			}

			return PromoteBlock.None;
		}

		// 승급은 1회성이라 배율이 없다.
		public static void GetPromoteCost(int itemId, ItemGradeType grade, List<CurrencyCost> buffer)
		{
			buffer.Clear();

			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (equipment == null)
			{
				return;
			}

			Table_ItemPromotion.Row cost = getPromotion(equipment.EquipSlotType, grade);
			if (cost == null || cost.ToGrade == ItemGradeType.None)
			{
				return;
			}

			addCost(buffer, cost.ReqMainCurrency, cost.ReqMainCost);
			addCost(buffer, cost.ReqCurrency_1, cost.ReqCost_1);
			addCost(buffer, cost.ReqCurrency_2, cost.ReqCost_2);
		}

		// ── 분해 ──────────────────────────────────────────────────────
		//
		// 장비를 없애고, 그 등급·강화에 드는 주 재화(ReqMainCurrency)의 일부를 돌려준다.
		// 실제로 쓴 내역을 저장하지 않으므로 현재 상태만 보고 계산한다 — 높은 등급으로 주운 장비도 그 등급만큼 받는다.
		//   강화 : 레벨 1 ~ 현재 레벨 각각이 속한 티어의 ReqMainCost 합
		//   승급 : Normal 부터 현재 등급까지 거친 승급 행의 ReqMainCost 합

		// 분해 환급량을 buffer 에 채운다 (재화별 합계 × DecomposeRefundRate, 소수점 버림). 돌려줄 것이 없으면 비운다.
		public static void GetDecomposeRefund(int itemId, ItemGradeType grade, int level, List<CurrencyCost> buffer)
		{
			buffer.Clear();

			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (equipment == null)
			{
				return;
			}

			ensureBuilt();

			for (int lv = 1; lv <= level; lv++)
			{
				Table_ItemEnhanceTier.Row tier = getTierByLevel(lv);
				if (tier == null)
				{
					break;
				}

				Table_ItemEnhance.Row enhance;
				if (_enhances.TryGetValue(slotKey(equipment.EquipSlotType, (int)tier.ID), out enhance) == true)
				{
					sumCost(buffer, enhance.ReqMainCurrency, enhance.ReqMainCost);
				}
			}

			// 승급 행을 Normal 부터 따라 올라간다. 등급 수를 넘겨 돌지 않도록 횟수를 묶어 둔다.
			ItemGradeType current = ItemGradeType.Normal;
			for (int i = 0; i < _promotions.Count && (int)current < (int)grade; i++)
			{
				Table_ItemPromotion.Row promotion = getPromotion(equipment.EquipSlotType, current);
				if (promotion == null || promotion.ToGrade == ItemGradeType.None)
				{
					break;
				}

				sumCost(buffer, promotion.ReqMainCurrency, promotion.ReqMainCost);
				current = promotion.ToGrade;
			}

			for (int i = buffer.Count - 1; i >= 0; i--)
			{
				CurrencyCost refund = buffer[i];
				refund.amount = (int)(refund.amount * DecomposeRefundRate);
				if (refund.amount <= 0)
				{
					buffer.RemoveAt(i);
					continue;
				}

				buffer[i] = refund;
			}
		}

		// ── 전이 ──────────────────────────────────────────────────────
		//
		// 두 장비의 등급과 강화도를 서로 교체한다. 품질(quality)은 전이되지 않는다.
		// 등급과 강화도가 함께 움직이므로 교체 후에도 등급별 레벨 상한 정합성은 깨지지 않는다.
		// 비용은 두 장비의 등급 중 '높은 쪽' 행을 쓴다 — 저등급 장비를 끼워 비용을 깎지 못하게 한다.

		// 구조 판정만 한다 — 재화 보유량은 호출자가 비용과 비교한다.
		public static TransferBlock GetTransferBlock(long uidA, int itemIdA, ItemGradeType gradeA, int levelA,
			long uidB, int itemIdB, ItemGradeType gradeB, int levelB)
		{
			if (uidA == uidB)
			{
				return TransferBlock.SameInstance;
			}

			Table_Equipment.Row rowA = Table_Equipment.Get(itemIdA);
			Table_Equipment.Row rowB = Table_Equipment.Get(itemIdB);
			if (rowA == null || rowB == null)
			{
				return TransferBlock.Invalid;
			}

			if (rowA.EquipSlotType != rowB.EquipSlotType || rowA.EquipSlotType == EquipSlotTypes.None)
			{
				return TransferBlock.SlotMismatch;
			}

			if (gradeA == gradeB && levelA == levelB)
			{
				return TransferBlock.SameState;
			}

			if (getTransfer(rowA.EquipSlotType, gradeA, gradeB) == null)
			{
				return TransferBlock.NoTable;
			}

			return TransferBlock.None;
		}

		// 전이 1회의 비용을 buffer 에 채운다. 행을 못 찾으면 비운다.
		public static void GetTransferCost(int itemIdA, ItemGradeType gradeA, int itemIdB, ItemGradeType gradeB, List<CurrencyCost> buffer)
		{
			buffer.Clear();

			Table_Equipment.Row rowA = Table_Equipment.Get(itemIdA);
			Table_Equipment.Row rowB = Table_Equipment.Get(itemIdB);
			if (rowA == null || rowB == null || rowA.EquipSlotType != rowB.EquipSlotType)
			{
				return;
			}

			Table_ItemTransfer.Row cost = getTransfer(rowA.EquipSlotType, gradeA, gradeB);
			if (cost == null)
			{
				return;
			}

			addCost(buffer, cost.ReqCurrency, cost.ReqCost);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static long slotKey(EquipSlotTypes slot, int sub)
		{
			return ((long)slot << 32) | (uint)sub;
		}

		private static Table_ItemEnhanceTier.Row getTierByLevel(int level)
		{
			ensureBuilt();

			for (int i = 0; i < _tiersByLevel.Count; i++)
			{
				Table_ItemEnhanceTier.Row row = _tiersByLevel[i];
				if (level >= row.MinLevel && level <= row.MaxLevel)
				{
					return row;
				}
			}

			return null;
		}

		private static Table_ItemPromotion.Row getPromotion(EquipSlotTypes slot, ItemGradeType fromGrade)
		{
			ensureBuilt();

			Table_ItemPromotion.Row row;
			_promotions.TryGetValue(slotKey(slot, (int)fromGrade), out row);
			return row;
		}

		// 두 등급 중 높은 쪽으로 비용 행을 찾는다.
		private static Table_ItemTransfer.Row getTransfer(EquipSlotTypes slot, ItemGradeType gradeA, ItemGradeType gradeB)
		{
			ensureBuilt();

			ItemGradeType higher = (int)gradeA >= (int)gradeB ? gradeA : gradeB;

			Table_ItemTransfer.Row row;
			_transfers.TryGetValue(slotKey(slot, (int)higher), out row);
			return row;
		}

		private static void addCost(List<CurrencyCost> buffer, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			CurrencyCost cost;
			cost.currency = currency;
			cost.amount = amount;
			buffer.Add(cost);
		}

		// 같은 재화는 한 항목으로 합친다 (분해 환급 합산용).
		private static void sumCost(List<CurrencyCost> buffer, EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.None || amount <= 0)
			{
				return;
			}

			for (int i = 0; i < buffer.Count; i++)
			{
				if (buffer[i].currency == currency)
				{
					CurrencyCost total = buffer[i];
					total.amount += amount;
					buffer[i] = total;
					return;
				}
			}

			addCost(buffer, currency, amount);
		}

		// 테이블 로드 후 처음 쓸 때 만든다.
		private static void ensureBuilt()
		{
			if (_enhances != null)
			{
				return;
			}

			buildTiers();
			buildPromotions();
			buildTransfers();
			buildEnhances();
		}

		private static void buildTiers()
		{
			_tierByGrade = new Dictionary<ItemGradeType, Table_ItemEnhanceTier.Row>();
			_tiersByLevel = new List<Table_ItemEnhanceTier.Row>();

			Dictionary<ItemEnhanceTier, Table_ItemEnhanceTier.Row> all = Table_ItemEnhanceTier.All();
			Dictionary<ItemEnhanceTier, Table_ItemEnhanceTier.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemEnhanceTier.Row row = e.Current.Value;
				if (row.ID == ItemEnhanceTier.None)
				{
					continue;
				}

				if (row.Grade != ItemGradeType.None)
				{
					_tierByGrade[row.Grade] = row;
				}

				_tiersByLevel.Add(row);
			}

			_tiersByLevel.Sort(compareTierMinLevel);
		}

		private static int compareTierMinLevel(Table_ItemEnhanceTier.Row a, Table_ItemEnhanceTier.Row b)
		{
			return a.MinLevel.CompareTo(b.MinLevel);
		}

		private static void buildPromotions()
		{
			_promotions = new Dictionary<long, Table_ItemPromotion.Row>();

			Dictionary<int, Table_ItemPromotion.Row> all = Table_ItemPromotion.All();
			Dictionary<int, Table_ItemPromotion.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemPromotion.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.FromGrade == ItemGradeType.None)
				{
					continue;
				}

				_promotions[slotKey(row.EquipmentType, (int)row.FromGrade)] = row;
			}
		}

		private static void buildTransfers()
		{
			_transfers = new Dictionary<long, Table_ItemTransfer.Row>();

			Dictionary<int, Table_ItemTransfer.Row> all = Table_ItemTransfer.All();
			Dictionary<int, Table_ItemTransfer.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemTransfer.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.Grade == ItemGradeType.None)
				{
					continue;
				}

				_transfers[slotKey(row.EquipmentType, (int)row.Grade)] = row;
			}
		}

		// 마지막에 만든다 — ensureBuilt 의 완료 표시를 겸한다.
		private static void buildEnhances()
		{
			Dictionary<long, Table_ItemEnhance.Row> enhances = new Dictionary<long, Table_ItemEnhance.Row>();

			Dictionary<int, Table_ItemEnhance.Row> all = Table_ItemEnhance.All();
			Dictionary<int, Table_ItemEnhance.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ItemEnhance.Row row = e.Current.Value;
				if (row.EquipmentType == EquipSlotTypes.None || row.EnhanceTier == ItemEnhanceTier.None)
				{
					continue;
				}

				enhances[slotKey(row.EquipmentType, (int)row.EnhanceTier)] = row;
			}

			_enhances = enhances;
		}
	}
}
