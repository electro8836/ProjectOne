using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Items;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 장착 장비의 옵션을 Hero 에 적용/제거하는 Aspect (아이템 설계 5.3 의 [4] 계층).
	//
	// 슬롯 8칸을 순회하며 각 인스턴스의 옵션을 계산하고, Option → StatDetail / Skill 로 풀어 적용한다.
	// source 태그 "Equipment" 로 일괄 부착하므로 해제는 RemoveAllFromSource 한 번이면 끝난다.
	//
	// Modifier 타입 옵션은 스탯 식에 들어가지 않고 스킬 리졸브 파이프라인으로 전달된다 — STEP 7.
	public sealed class EquipmentAspect : IHeroAspect
	{
		const string Source = "Equipment";

		// 슬롯마다 새로 할당하지 않도록 재사용. Aspect 적용은 메인 스레드 단일 경로다.
		private readonly List<EquipmentOptionCalculator.Resolved> _buffer = new List<EquipmentOptionCalculator.Resolved>(8);

		public HeroAspectStage Stage => HeroAspectStage.Equipment;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null || hero.Stats == null)
			{
				return;
			}

			Loadout loadout = Account.Instance.Loadout;
			for (int slot = 1; slot < LoadoutDto.SlotCount; slot++)
			{
				EquipmentInstance instance = loadout.GetEquipped((EquipSlotTypes)slot);
				if (instance == null)
				{
					continue;
				}

				EquipmentOptionCalculator.Collect(instance, _buffer);
				for (int i = 0; i < _buffer.Count; i++)
				{
					apply(hero, _buffer[i]);
				}
			}
		}

		public void RemoveFrom(Hero hero)
		{
			if (hero == null)
			{
				return;
			}

			if (hero.Stats != null)
			{
				hero.Stats.RemoveAllFromSource(Source);
			}

			if (hero.SkillContainer != null)
			{
				hero.SkillContainer.RemoveAllFromSource(Source);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void apply(Hero hero, EquipmentOptionCalculator.Resolved resolved)
		{
			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(resolved.option, out entry) == false)
			{
				Debug.LogWarning($"[EquipmentAspect] 정의되지 않은 옵션: {resolved.option}");
				return;
			}

			switch (entry.type)
			{
				case OptionTypes.Stat:
					hero.Stats.AddModifier(entry.statDetail, resolved.value, Source);
					break;

				case OptionTypes.SkillGrant:
					if (hero.SkillContainer != null)
					{
						hero.SkillContainer.Register(entry.skill, Source);
					}

					break;

				case OptionTypes.SkillLevel:
				case OptionTypes.Modifier:
					// 스킬 레벨·변형은 리졸브 파이프라인이 소유한다 — STEP 7 에서 연결한다.
					break;
			}
		}
	}
}
