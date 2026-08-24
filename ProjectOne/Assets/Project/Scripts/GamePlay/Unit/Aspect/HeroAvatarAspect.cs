using EDT;
using ProjectOne.Avatar;
using ProjectOne.Costumes;
using ProjectOne.Items;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	// 장비·코스튬 외형을 Hero 에 반영하는 Aspect.
	//
	// ResetAll 로 시작해 파츠를 프리팹 원본으로 되돌린 뒤 다시 얹는다 —
	// 이전 장비의 스프라이트가 남는 잔상을 원천적으로 없앤다.
	//
	// 되돌리기를 RemoveFrom 이 아니라 여기서 하는 이유는 MasteryAspect 와 같다:
	// Reapply 는 RemoveFrom → ApplyTo 를 연달아 부르므로, 양쪽에서 건드리면 한 사이클에 두 번 바뀐다.
	public sealed class HeroAvatarAspect : IHeroAspect
	{
		const string Source = "Avatar";

		public HeroAspectStage Stage => HeroAspectStage.Collection;

		public string SourceKey => Source;

		public void ApplyTo(Hero hero)
		{
			if (hero == null)
			{
				return;
			}

			HeroAvatar avatar = hero.GetComponent<HeroAvatar>();
			if (avatar == null)
			{
				return;
			}

			avatar.ResetAll();

			// 바디가 먼저다 — 몸 27파츠를 완전히 지정한다.
			avatar.ApplyBodySet(resolveBodySet());

			// 무기가 나중이다 — 코스튬 무기가 장비 무기를 덮어쓴다.
			avatar.ApplyWeaponSet(resolveWeaponSet());
		}

		public void RemoveFrom(Hero hero)
		{
		}

		// 입고 있는 바디 코스튬. 미착용이면 기본 코스튬(IsDefault)으로 되돌아간다.
		static AvatarBodySet resolveBodySet()
		{
			CostumeBook book = Account.Instance.Costume;
			int equippedId = (book != null) ? book.EquippedBodyId : 0;

			Table_Costume.Row row = CostumeCatalog.Get(equippedId);
			if (row == null || row.CostumeType != CostumeType.Body)
			{
				// 미착용이거나 데이터가 어긋났으면 기본 코스튬. 그것마저 없으면 프리팹 원본이 남는다.
				row = CostumeCatalog.DefaultBody;
			}

			if (row == null)
			{
				return null;
			}

			return AvatarCatalog.GetBodySet(row.SetAddress);
		}

		// 손에 보일 무기. 코스튬 무기가 우선이고, 없거나 직업이 안 맞으면 장비 무기다.
		// 둘 다 없으면 null — 양손이 비워진다.
		static AvatarWeaponSet resolveWeaponSet()
		{
			Loadout loadout = Account.Instance.Loadout;
			if (loadout == null)
			{
				return null;
			}

			// 직업 제한은 표시 시점에 본다 — 안 맞는 무기를 든 동안만 장비 무기가 보이고,
			// 맞는 무기로 갈아끼우면 코스튬이 다시 나타난다(착용 상태는 유지된다).
			CostumeBook book = Account.Instance.Costume;
			if (book != null)
			{
				int costumeId = book.EquippedWeaponId;
				if (CostumeCatalog.CanShowWeapon(costumeId, loadout.EquippedWeaponType) == true)
				{
					return AvatarCatalog.GetWeaponSet(CostumeCatalog.Get(costumeId).SetAddress);
				}
			}

			EquipmentInstance weapon = loadout.GetEquipped(EquipSlotTypes.Weapon);
			if (weapon == null)
			{
				return null;
			}

			Table_Equipment.Row row = weapon.Equipment;
			if (row == null)
			{
				return null;
			}

			return AvatarCatalog.GetWeaponSet(row.WeaponSetAddress);
		}
	}
}
