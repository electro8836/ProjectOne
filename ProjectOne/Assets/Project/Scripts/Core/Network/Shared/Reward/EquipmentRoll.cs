using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 등급 추첨 결과 — 실패 사유를 호출자가 로그로 구분할 수 있게 나눈다.
	public enum EquipmentGradeRollResult
	{
		Success,
		NotEquipment,		// Item 또는 Equipment 행이 없다
		MissingWeight,		// EquipGradeWeight 행이 없다
		NoValidGrade		// 유효 등급 범위 안의 가중치 합이 0 — 드랍 스킵
	}

	// 장비 인스턴스의 등급·품질 추첨 (아이템 설계 7장). 클라·서버 공용.
	//
	//   1) 등급 추첨 — EquipGradeWeight, 유효 범위는 Item.Grade ~ Equipment.MaxGrade
	//   2) 품질 추첨 — EquipQuality 구간 → 구간 내 균등
	//
	// 유효 등급의 가중치 합이 0이면 **드랍을 스킵한다**. 최소 등급으로 강제하지 않는다 —
	// 그렇게 하면 가중치로 걸어둔 구간 제한이 무력화된다 (설계 7.2).
	public static class EquipmentRoll
	{
		// 추첨 버퍼 — 클라는 메인 스레드, 서버는 컨테이너당 단일 요청이라 재사용해도 안전하다.
		private static readonly List<int> _gradeWeights = new List<int>(6);
		private static readonly List<int> _qualityWeights = new List<int>(8);
		private static readonly List<Table_EquipQuality.Row> _qualityRows = new List<Table_EquipQuality.Row>(8);

		public static EquipmentGradeRollResult TryRollGrade(int itemId, int gradeWeightId, IRandomSource rng, out ItemGradeType grade)
		{
			grade = ItemGradeType.None;

			Table_Item.Row item = Table_Item.Get(itemId);
			Table_Equipment.Row equipment = Table_Equipment.Get(itemId);
			if (item == null || equipment == null)
			{
				return EquipmentGradeRollResult.NotEquipment;
			}

			Table_EquipGradeWeight.Row weights = Table_EquipGradeWeight.Get(gradeWeightId);
			if (weights == null)
			{
				return EquipmentGradeRollResult.MissingWeight;
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

			int index = WeightedPick.PickIndex(_gradeWeights, rng);
			if (index < 0)
			{
				return EquipmentGradeRollResult.NoValidGrade;
			}

			// _gradeWeights 는 Normal(=1) 부터 채웠으므로 인덱스에 Normal 을 더한다.
			grade = (ItemGradeType)(index + (int)ItemGradeType.Normal);
			return EquipmentGradeRollResult.Success;
		}

		// 구간을 먼저 뽑고, 구간 안에서 균등 분포로 정수를 뽑는다 (설계 3.9).
		// EquipQuality 가중치가 없으면 false 와 함께 품질 1 을 돌려준다.
		public static bool TryRollQuality(IRandomSource rng, out int quality)
		{
			_qualityWeights.Clear();
			_qualityRows.Clear();

			Dictionary<int, Table_EquipQuality.Row> all = Table_EquipQuality.All();
			Dictionary<int, Table_EquipQuality.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_qualityRows.Add(e.Current.Value);
				_qualityWeights.Add(e.Current.Value.AssignWeight);
			}

			int index = WeightedPick.PickIndex(_qualityWeights, rng);
			if (index < 0)
			{
				quality = 1;
				return false;
			}

			// Mathf.RoundToInt 와 같은 은행원 반올림(System.Math.Round 기본값).
			Table_EquipQuality.Row row = _qualityRows[index];
			int min = (int)System.Math.Round(row.MinValue);
			int max = (int)System.Math.Round(row.MaxValue);
			if (max < min)
			{
				max = min;
			}

			quality = rng.Range(min, max + 1);
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
