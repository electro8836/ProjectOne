using EDT;
using UnityEngine;
using ProjectOne.ServerData;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 캐릭터별 장착(OwnedCharacter.preset)의 장비 스탯/스킬을 Hero 에 적용/제거하는 Aspect.
	// - 스폰된 Hero 의 characterId(GetTableID) 로 해당 캐릭터의 장착을 찾아 적용
	// - source 태그 "Equipment" 로 일괄 부착/해제
	public sealed class EquipmentAspect : IHeroAspect
	{
		const string Source = "Equipment";

		public HeroAspectStage Stage => HeroAspectStage.Equipment;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null)
			{
				return;
			}

			OwnedCharacter oc = Account.Instance.Loadout.GetOwned(hero.GetTableID());
			if (oc == null)
			{
				return;
			}

			applySlot(hero, oc.preset.weaponItemId);
			applySlot(hero, oc.preset.armorItemId);
			applySlot(hero, oc.preset.accessoryItemId);
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

		private void applySlot(Hero hero, int itemId)
		{
			if (itemId <= 0)
			{
				return;
			}

			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null)
			{
				return;
			}

			// TODO: 강화 수치(Inventory.GetEnhanceLevel) 반영 — 현재 범위 외(수치 보관만)
			applyStat(hero, row.StatOptionType_1, row.StatOptionValue_1);
			applyStat(hero, row.StatOptionType_2, row.StatOptionValue_2);
			applySkill(hero, row.SkillOption_1);
			applySkill(hero, row.SkillOption_2);
		}

		private void applyStat(Hero hero, StatInfo type, int value)
		{
			if (type == StatInfo.None || value == 0 || hero.Stats == null)
			{
				return;
			}

			// AddModifier 는 _Add/_Ratio/_Amp 만 허용 — 부적합 타입은 예외 대신 경고 후 무시
			if (hero.Stats.CanAddModifier(type) == false)
			{
				Debug.LogWarning($"[EquipmentAspect] 장비 옵션 스탯 타입이 모디파이어 불가: {type} (_Add/_Ratio/_Amp 만 허용)");
				return;
			}

			hero.Stats.AddModifier(type, value, Source);
		}

		private void applySkill(Hero hero, int skillOption)
		{
			if (skillOption == 0 || hero.SkillContainer == null)
			{
				return;
			}

			hero.SkillContainer.Register((SkillInfo)skillOption, Source);
		}
	}
}
