using EDT;
using ProjectOne.Dungeon;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 던전 카드스킬 장착슬롯(DungeonRunState)의 스킬을 Hero 에 적용/제거하는 Aspect.
	// - 슬롯 6칸을 순회해 각 슬롯의 cardSkillId + level 에 맞는 스킬을 source "CardSkill" 로 등록
	// - 구매/판매/레벨업 시 HeroAspectRegistry.Reapply(hero, "CardSkill") 로 재적용
	// - 로비 등 던전이 아닐 때는 슬롯이 비어 있어 no-op
	public sealed class CardSkillAspect : IHeroAspect
	{
		public const string Source = "CardSkill";

		public HeroAspectStage Stage => HeroAspectStage.Skill;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null || hero.SkillContainer == null)
			{
				return;
			}

			for (int i = 0; i < DungeonRunState.SlotCount; i++)
			{
				DungeonRunState.CardSkillSlot slot = DungeonRunState.Instance.GetSlot(i);
				if (slot.cardSkillId <= 0)
				{
					continue;
				}

				applySlot(hero, slot.cardSkillId, slot.level);
			}
		}

		public void RemoveFrom(Hero hero)
		{
			if (hero == null || hero.SkillContainer == null)
			{
				return;
			}

			hero.SkillContainer.RemoveAllFromSource(Source);
		}

		private void applySlot(Hero hero, int cardSkillId, int level)
		{
			Table_CardSkill.Row card = Table_CardSkill.Get(cardSkillId);
			if (card == null)
			{
				return;
			}

			SkillInfo skill = CardSkillResolver.SkillForLevel(card, level);
			if (skill == SkillInfo.None)
			{
				return;
			}

			hero.SkillContainer.Register(skill, Source);
		}
	}
}
