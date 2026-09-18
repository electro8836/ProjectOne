using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Items;
using ProjectOne.Pets;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 펫의 보유 효과를 Hero 에 적용하는 Aspect.
	//
	// **장착이 아니라 보유로 걸린다** — 가지고 있는 펫 전부의 옵션이 합산된다.
	// 장착은 모델을 따라다니게 할 뿐이고 그쪽은 PetSpawner 가 맡는다.
	//
	// Stage 가 Collection 인 이유 — 이 단계의 정의가 "콜렉션(스킨 보유 등) 효과" 이고,
	// 펫도 같은 축(보유하면 붙는 상시 효과)이다.
	public sealed class PetAspect : IHeroAspect
	{
		const string Source = "Pet";

		public HeroAspectStage Stage => HeroAspectStage.Collection;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null || hero.Stats == null)
			{
				return;
			}

			PetBook book = Account.Instance.Pet;

			IReadOnlyList<Table_Pet.Row> rows = PetCatalog.AllSorted();
			for (int i = 0; i < rows.Count; i++)
			{
				Table_Pet.Row row = rows[i];

				PetEntry entry = book.Find(row.ID);
				if (entry == null || row.Opt_ID == Option.None)
				{
					continue;
				}

				float value = PetEntry.GetOptionValue(row, entry.Level);
				if (value == 0f)
				{
					continue;
				}

				applyStatOption(hero, row.ID, row.Opt_ID, value);
			}
		}

		public void RemoveFrom(Hero hero)
		{
			if (hero == null || hero.Stats == null)
			{
				return;
			}

			hero.Stats.RemoveAllFromSource(Source);
		}

		// 펫 옵션은 Stat 타입만 허용한다 — 스킬 부여나 변형은 펫의 설계에 없다.
		private void applyStatOption(Hero hero, EDT.Pet petId, Option option, float value)
		{
			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(option, out entry) == false)
			{
				Debug.LogWarning($"[PetAspect] 정의되지 않은 옵션 — pet:{petId} option:{option}");
				return;
			}

			if (entry.type != OptionTypes.Stat)
			{
				Debug.LogError($"[PetAspect] 펫 보유 효과는 Stat 옵션만 가능합니다: {option} (type={entry.type})");
				return;
			}

			hero.Stats.AddModifier(entry.statDetail, value, Source);
		}
	}
}
