using System.Collections.Generic;
using ProjectOne.Costumes;
using ProjectOne.Shared;
using ProjectOne.Unit;

namespace ProjectOne.UserData
{
	// 코스튬 보유·착용 상태 도메인.
	//
	// 장비(Inventory/Loadout)와 분리한 이유 — 코스튬은 개별 인스턴스가 없다.
	// 강화·등급·순도가 없어 uid 채번이 무의미하고, "보유 ID 집합 + 착용 ID 둘"이면 상태가 전부 표현된다.
	//
	// 착용은 CostumeCatalog 로 타입을 검증한 뒤 아바타를 다시 그린다 — Loadout 이 장비에 대해 하는 것과 같다.
	public sealed class CostumeBook
	{
		// HeroAvatarAspect.SourceKey 와 같아야 한다.
		private const string AvatarSource = "Avatar";

		private readonly HashSet<int> _owned = new HashSet<int>();

		// 0 = 미착용. 무기는 장비 무기가 그대로 보이고, 바디는 기본 코스튬이 적용된다.
		private int _equippedWeaponId;
		private int _equippedBodyId;

		public CostumeBook(CostumeDto dto)
		{
			buildFromDto(dto);
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public int EquippedWeaponId
		{
			get { return _equippedWeaponId; }
		}

		public int EquippedBodyId
		{
			get { return _equippedBodyId; }
		}

		public bool IsOwned(int costumeId)
		{
			return _owned.Contains(costumeId);
		}

		public IReadOnlyCollection<int> Owned
		{
			get { return _owned; }
		}

		// ── 변경 ──────────────────────────────────────────────────────

		// 구매·지급으로 보유에 추가한다.
		public void Add(int costumeId)
		{
			if (costumeId <= 0)
			{
				return;
			}

			_owned.Add(costumeId);
		}

		// 무기 코스튬 착용. 0 을 넘기면 해제한다(장비 무기가 다시 보인다).
		//
		// 직업 제한은 여기서 보지 않는다 — 지금 안 맞는 무기를 들고 있어도 착용은 되고,
		// 맞는 무기로 갈아끼우면 그때 보인다. 표시 시점 판정은 HeroAvatarAspect 가 한다.
		public bool EquipWeapon(int costumeId)
		{
			if (costumeId != 0)
			{
				if (IsOwned(costumeId) == false || CostumeCatalog.IsWeapon(costumeId) == false)
				{
					return false;
				}
			}

			if (_equippedWeaponId == costumeId)
			{
				return true;
			}

			_equippedWeaponId = costumeId;
			reapplyAvatar();
			return true;
		}

		// 바디 코스튬 착용. 0 을 넘기면 기본 코스튬으로 되돌린다.
		public bool EquipBody(int costumeId)
		{
			if (costumeId != 0)
			{
				if (IsOwned(costumeId) == false || CostumeCatalog.IsBody(costumeId) == false)
				{
					return false;
				}
			}

			if (_equippedBodyId == costumeId)
			{
				return true;
			}

			_equippedBodyId = costumeId;
			reapplyAvatar();
			return true;
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용
		public CostumeDto ToDto()
		{
			CostumeDto dto = new CostumeDto();
			dto.owned.AddRange(_owned);
			dto.equippedWeaponId = _equippedWeaponId;
			dto.equippedBodyId = _equippedBodyId;
			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 활성 Hero 의 외형만 다시 그린다 (Loadout.reapplyHero 의 코스튬판).
		// 스탯·스킬은 코스튬과 무관하므로 Avatar 출처만 재적용한다.
		private void reapplyAvatar()
		{
			if (UnitManager.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				Hero hero = heroes[i] as Hero;
				if (hero == null)
				{
					continue;
				}

				HeroAspectRegistry.Instance.Reapply(hero, AvatarSource);
			}
		}

		private void buildFromDto(CostumeDto dto)
		{
			_owned.Clear();
			_equippedWeaponId = 0;
			_equippedBodyId = 0;

			// 서버 DTO 주입 — 없으면 빈 상태(미로그인/오프라인).
			if (dto == null)
			{
				return;
			}

			if (dto.owned != null)
			{
				for (int i = 0; i < dto.owned.Count; i++)
				{
					int id = dto.owned[i];
					if (id > 0)
					{
						_owned.Add(id);
					}
				}
			}

			// 보유하지 않은 것이 착용으로 남아 있으면 버린다(서버 데이터 불일치 방어).
			if (dto.equippedWeaponId > 0 && _owned.Contains(dto.equippedWeaponId) == true)
			{
				_equippedWeaponId = dto.equippedWeaponId;
			}

			if (dto.equippedBodyId > 0 && _owned.Contains(dto.equippedBodyId) == true)
			{
				_equippedBodyId = dto.equippedBodyId;
			}
		}
	}
}
