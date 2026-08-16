using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Items
{
	// 드랍/보상으로 장비 인스턴스를 만드는 생성기 (아이템 설계 7장).
	//
	//   1) 대상 ItemID 는 호출자(드랍 테이블)가 정한다
	//   2) 등급 추첨 — EquipGradeWeight, 유효 범위는 Item.Grade ~ Equipment.MaxGrade
	//   3) 순도 추첨 — EquipPurity.AssignWeight
	//   4) 품질 추첨 — EquipQuality 구간 → 구간 내 균등
	//   5) Level = 1 로 생성
	//
	// 유효 등급의 가중치 합이 0이면 **드랍을 스킵한다**(null 반환). 최소 등급으로 강제하지 않는다 —
	// 그렇게 하면 가중치로 걸어둔 구간 제한이 무력화된다 (설계 7.2).
	public static class EquipmentFactory
	{
		// 등급 6종 가중치 버퍼 — 추첨은 메인 스레드 단일 경로라 재사용해도 안전하다.
		private static readonly List<int> _gradeWeights = new List<int>(6);
		private static readonly List<int> _weightBuffer = new List<int>(8);
		private static readonly List<EquipPurity> _purityKeys = new List<EquipPurity>(8);
		private static readonly List<Table_EquipQuality.Row> _qualityRows = new List<Table_EquipQuality.Row>(8);

		// 장비 인스턴스 생성. 등급을 뽑을 수 없으면 null(드랍 스킵).
		public static EquipmentInstance Create(int itemId, int gradeWeightId)
		{
			Table_Item.Row item = Table_Item.Get(itemId);
			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (item == null || equipment == null)
			{
				Debug.LogError($"[EquipmentFactory] 장비 아이템이 아닙니다: {itemId}");
				return null;
			}

			ItemGradeType grade;
			if (tryRollGrade(item, equipment, gradeWeightId, out grade) == false)
			{
				Debug.LogWarning($"[EquipmentFactory] 유효 등급이 없어 드랍을 스킵합니다 — item:{itemId} weightGroup:{gradeWeightId}");
				return null;
			}

			EquipmentInstance instance = new EquipmentInstance();
			instance.itemId = itemId;
			instance.grade = grade;
			instance.level = 1;
			instance.purity = RollPurity();
			instance.quality = RollQuality();
			instance.equippedSlot = EquipSlotTypes.None;
			return instance;
		}

		// 등급을 지정해 직접 생성 — 개발/테스트·확정 보상 경로.
		public static EquipmentInstance CreateFixed(int itemId, ItemGradeType grade)
		{
			if (Table_Equipment.Get(itemId) == null)
			{
				Debug.LogError($"[EquipmentFactory] 장비 아이템이 아닙니다: {itemId}");
				return null;
			}

			EquipmentInstance instance = new EquipmentInstance();
			instance.itemId = itemId;
			instance.grade = grade;
			instance.level = 1;
			instance.purity = RollPurity();
			instance.quality = RollQuality();
			instance.equippedSlot = EquipSlotTypes.None;
			return instance;
		}

		// ── 추첨 ──────────────────────────────────────────────────────

		public static EquipPurity RollPurity()
		{
			_weightBuffer.Clear();
			_purityKeys.Clear();

			Dictionary<EquipPurity, Table_EquipPurity.Row> all = Table_EquipPurity.All();
			Dictionary<EquipPurity, Table_EquipPurity.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_EquipPurity.Row row = e.Current.Value;
				if (row.ID == EquipPurity.None)
				{
					continue;
				}

				_purityKeys.Add(row.ID);
				_weightBuffer.Add(row.AssignWeight);
			}

			int index = WeightedRandom.PickIndex(_weightBuffer);
			if (index < 0)
			{
				Debug.LogWarning("[EquipmentFactory] EquipPurity 가중치가 없어 Purity_1 로 대체합니다.");
				return EquipPurity.Purity_1;
			}

			return _purityKeys[index];
		}

		// 구간을 먼저 뽑고, 구간 안에서 균등 분포로 정수를 뽑는다 (설계 3.9).
		public static int RollQuality()
		{
			_weightBuffer.Clear();
			_qualityRows.Clear();

			Dictionary<int, Table_EquipQuality.Row> all = Table_EquipQuality.All();
			Dictionary<int, Table_EquipQuality.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_qualityRows.Add(e.Current.Value);
				_weightBuffer.Add(e.Current.Value.AssignWeight);
			}

			int index = WeightedRandom.PickIndex(_weightBuffer);
			if (index < 0)
			{
				Debug.LogWarning("[EquipmentFactory] EquipQuality 가중치가 없어 품질 1 로 대체합니다.");
				return 1;
			}

			Table_EquipQuality.Row row = _qualityRows[index];
			int min = Mathf.RoundToInt(row.MinValue);
			int max = Mathf.RoundToInt(row.MaxValue);
			if (max < min)
			{
				max = min;
			}

			return Random.Range(min, max + 1);
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static bool tryRollGrade(Table_Item.Row item, Table_Equipment.Row equipment, int gradeWeightId, out ItemGradeType grade)
		{
			grade = ItemGradeType.None;

			Table_EquipGradeWeight.Row weights = Table_EquipGradeWeight.Get(gradeWeightId);
			if (weights == null)
			{
				Debug.LogError($"[EquipmentFactory] EquipGradeWeight {gradeWeightId} 를 찾지 못했습니다.");
				return false;
			}

			// 유효 등급 범위 — 하한은 Item.Grade(최소 드랍 등급), 상한은 Equipment.MaxGrade.
			int min = (int)item.Grade;
			int max = (int)equipment.MaxGrade;
			if (min <= 0)
			{
				min = (int)ItemGradeType.Normal;
			}

			if (max <= 0)
			{
				max = (int)ItemGradeType.Mythic;
			}

			_gradeWeights.Clear();
			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				bool inRange = g >= min && g <= max;
				_gradeWeights.Add(inRange ? getWeight(weights, (ItemGradeType)g) : 0);
			}

			int index = WeightedRandom.PickIndex(_gradeWeights);
			if (index < 0)
			{
				return false;
			}

			// _gradeWeights 는 Normal(=1) 부터 채웠으므로 인덱스에 Normal 을 더한다.
			grade = (ItemGradeType)(index + (int)ItemGradeType.Normal);
			return true;
		}

		private static int getWeight(Table_EquipGradeWeight.Row row, ItemGradeType grade)
		{
			switch (grade)
			{
				case ItemGradeType.Normal:		return row.Normal;
				case ItemGradeType.Magic:		return row.Magic;
				case ItemGradeType.Rare:		return row.Rare;
				case ItemGradeType.Epic:		return row.Epic;
				case ItemGradeType.Legendary:	return row.Legendary;
				case ItemGradeType.Mythic:		return row.Mythic;
			}

			return 0;
		}
	}
}
