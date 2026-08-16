using System.Collections.Generic;
using EDT;

namespace ProjectOne.Items
{
	// 장비 인스턴스 하나가 공급하는 옵션 목록을 계산한다 (아이템 설계 5장).
	//
	//   기본 옵션 Opt1~4 : (Val + Step × Level) × EquipPurity.OptionMultiplier
	//   해금 옵션        : MinVal + (MaxVal - MinVal) × (Quality / 100)
	//
	// 해금 옵션은 **현재 등급 이하의 모든 등급 행**이 누적된다. Normal 에는 해금 옵션이 없다.
	// Val 은 0레벨 기준값이다 — 캐릭터 스탯(1레벨 기준)과 규칙이 반대다 (설계 5.1 주석).
	public static class EquipmentOptionCalculator
	{
		// 계산된 옵션 1개. 실제 적용(StatDetail/스킬)은 호출자가 OptionCatalog 로 해석한다.
		public struct Resolved
		{
			public Option option;
			public float value;

			// 해금 옵션일 때만 의미 있는 표시용 구간 (설계 8장 — 최종값 + (Min ~ Max))
			public bool isUnlock;
			public float minValue;
			public float maxValue;
		}

		// instance 가 공급하는 모든 옵션을 buffer 에 채운다(호출자가 버퍼를 소유).
		public static void Collect(EquipmentInstance instance, List<Resolved> buffer)
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

			Table_Equipment.Row equipment = instance.Equipment;
			if (equipment == null)
			{
				return;
			}

			float purityMult = getPurityMultiplier(instance.purity);

			// 기본 옵션 — 현재 등급 행 하나만 본다.
			Table_EquipOption.Row current = EquipmentCatalog.GetOption(equipment.EquipOptionGroupID, instance.grade);
			if (current != null)
			{
				addBase(buffer, current.Opt1_ID, current.Opt1_Val, current.Opt1_Step, instance.level, purityMult);
				addBase(buffer, current.Opt2_ID, current.Opt2_Val, current.Opt2_Step, instance.level, purityMult);
				addBase(buffer, current.Opt3_ID, current.Opt3_Val, current.Opt3_Step, instance.level, purityMult);
				addBase(buffer, current.Opt4_ID, current.Opt4_Val, current.Opt4_Step, instance.level, purityMult);
			}

			// 해금 옵션 — 현재 등급 이하 전부 누적.
			int maxGrade = (int)instance.grade;
			for (int g = (int)ItemGradeType.Normal; g <= maxGrade; g++)
			{
				Table_EquipOption.Row row = EquipmentCatalog.GetOption(equipment.EquipOptionGroupID, (ItemGradeType)g);
				if (row == null || row.UnlockOpt_ID == Option.None)
				{
					continue;
				}

				addUnlock(buffer, row, instance.quality);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void addBase(List<Resolved> buffer, Option option, float val, float step, int level, float purityMult)
		{
			if (option == Option.None)
			{
				return;
			}

			Resolved r;
			r.option = option;
			r.value = (val + step * level) * purityMult;
			r.isUnlock = false;
			r.minValue = 0f;
			r.maxValue = 0f;
			buffer.Add(r);
		}

		private static void addUnlock(List<Resolved> buffer, Table_EquipOption.Row row, int quality)
		{
			float t = quality / 100f;

			Resolved r;
			r.option = row.UnlockOpt_ID;
			r.value = row.UnlockOpt_MinVal + (row.UnlockOpt_MaxVal - row.UnlockOpt_MinVal) * t;
			r.isUnlock = true;
			r.minValue = row.UnlockOpt_MinVal;
			r.maxValue = row.UnlockOpt_MaxVal;
			buffer.Add(r);
		}

		// 순도 배율. 데이터가 없으면 1.0 으로 두어 옵션이 통째로 0이 되는 사고를 막는다.
		private static float getPurityMultiplier(EquipPurity purity)
		{
			Table_EquipPurity.Row row = Table_EquipPurity.Get(purity);
			if (row == null || row.OptionMultiplier <= 0f)
			{
				return 1f;
			}

			return row.OptionMultiplier;
		}
	}
}
