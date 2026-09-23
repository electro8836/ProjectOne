using System.Collections.Generic;
using EDT;
using ProjectOne.Items;

namespace ProjectOne.Reward
{
	// 표시용 보상 한 건. 추첨 결과가 아니라 "무엇이 들어있는지" 를 나타낸다.
	public struct RewardPreviewItem
	{
		public RewardType type;
		public int itemId;
		public EDT.Currency currency;
		public int count;
		public EquipmentInstance equipment;	// 장비면 uid=0 인 표시용 인스턴스, 아니면 null
	}

	// 보상 그룹 하나를 추첨 없이 목록으로 펼친다.
	//
	// RewardGranter.Roll 과 목적이 다르다 — 저쪽은 실제로 굴려서 지급할 것을 정하고,
	// 여기는 "구매 전에 보여줄 구성품"을 만든다. 그래서 같은 그룹을 여러 번 불러도 결과가 같아야 한다.
	//
	// 장비의 등급·순도·품질은 원래 추첨이라 구매 전에는 존재하지 않는 값이다.
	// Reward 행에 FixedGrade 가 적혀 있으면 그 확정값을(지급도 같은 값을 쓴다),
	// 없으면 아래 대표값으로 보여준다.
	public static class RewardPreview
	{
		// 미지정 행에 쓸 대표 수치.
		public const int DEFAULT_QUALITY = 50;

		// buffer 는 비우지 않고 누적한다 (RewardGranter 와 같은 관례).
		public static void Build(int groupId, List<RewardPreviewItem> buffer)
		{
			if (groupId <= 0 || buffer == null)
			{
				return;
			}

			IReadOnlyList<RewardCatalog.RewardEntry> entries = RewardCatalog.GetGroup(groupId);
			for (int i = 0; i < entries.Count; i++)
			{
				RewardCatalog.RewardEntry entry = entries[i];
				if (entry.isValid == false)
				{
					continue;
				}

				// 아이템 풀은 후보가 여럿이라 칸 하나로 그릴 수 없다 — 미리보기에서는 뺀다.
				if (entry.row.RewardType == RewardType.ItemPool)
				{
					continue;
				}

				RewardPreviewItem item = default(RewardPreviewItem);
				item.type = entry.row.RewardType;
				item.count = getCount(entry.row);

				if (entry.row.RewardType == RewardType.Currency)
				{
					item.currency = entry.currency;
					buffer.Add(item);
					continue;
				}

				item.itemId = entry.itemId;

				// 장비냐 아니냐는 최종 지급 대상이 기준이다 (RewardGranter.rollItem 과 같은 판정).
				if (Table_Equipment.Get(entry.itemId) != null)
				{
					item.equipment = buildEquipment(entry.row, entry.itemId);
					if (item.equipment == null)
					{
						continue;
					}

					item.count = 1;
				}

				buffer.Add(item);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 확정 수량을 보여준다 — 범위로 적힌 행은 하한을 쓴다.
		private static int getCount(Table_Reward.Row row)
		{
			return row.MinCount > 0 ? row.MinCount : row.MaxCount;
		}

		private static EquipmentInstance buildEquipment(Table_Reward.Row row, int itemId)
		{
			if (row.FixedGrade != ItemGradeType.None)
			{
				return EquipmentFactory.CreateExact(itemId, row.FixedGrade, row.FixedQuality);
			}

			// 확정값이 없으면 최소 보장 등급(Item.Grade)에 대표 품질을 얹어 보여준다.
			Table_Item.Row item = Table_Item.Get(itemId);
			ItemGradeType grade = (item != null && item.Grade != ItemGradeType.None) ? item.Grade : ItemGradeType.Normal;

			return EquipmentFactory.CreateExact(itemId, grade, DEFAULT_QUALITY);
		}
	}
}
