using System.Collections.Generic;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Shared;
using ProjectOne.Unit;

namespace ProjectOne.Pets
{
	// 펫 보유·장착 상태 도메인.
	//
	// 코스튬(CostumeBook)과 같은 모양이다 — 개별 인스턴스가 없어 "보유 집합 + 장착 하나"로 상태가 끝난다.
	// 다른 점은 펫에 강화 레벨과 등급이 있어 보유 항목이 값을 들고 있다는 것뿐이다(PetEntry).
	//
	// 옵션(Opt_ID)은 **보유 효과**다 — 장착과 무관하게 가진 펫 전부가 스탯에 들어간다(PetAspect).
	// 장착은 모델을 불러 따라다니게 하는 것뿐이고, 그 효과는 전 펫 공용 자석 하나다.
	public sealed class PetBook
	{
		// PetAspect.SourceKey 와 같아야 한다.
		private const string PetSource = "Pet";

		private readonly Dictionary<EDT.Pet, PetEntry> _owned = new Dictionary<EDT.Pet, PetEntry>();

		// None = 미장착. 필드가 하나라 "동시에 한 마리" 제한이 자연히 성립한다.
		private EDT.Pet _equipped = EDT.Pet.None;

		public PetBook(PetDto dto)
		{
			buildFromDto(dto);
		}

		// ── 조회 ──────────────────────────────────────────────────────

		// 펫 창 타이틀의 분자.
		public int OwnedCount
		{
			get { return _owned.Count; }
		}

		public EDT.Pet Equipped
		{
			get { return _equipped; }
		}

		public bool IsEquipped(EDT.Pet id)
		{
			return id != EDT.Pet.None && _equipped == id;
		}

		public bool IsOwned(EDT.Pet id)
		{
			return _owned.ContainsKey(id);
		}

		// 보유 항목을 읽기만 한다. 없으면 null — 조회 때문에 항목이 생기는 것을 막는다.
		public PetEntry Find(EDT.Pet id)
		{
			PetEntry entry;
			_owned.TryGetValue(id, out entry);
			return entry;
		}

		// 미보유는 0. 슬롯이 레벨 뱃지를 끄는 기준이다.
		public int GetLevel(EDT.Pet id)
		{
			PetEntry entry = Find(id);
			return (entry != null) ? entry.Level : 0;
		}

		// 미보유는 테이블의 기본 등급 — 잠긴 카드도 등급 표기는 보여야 한다.
		public ItemGradeType GetGrade(EDT.Pet id)
		{
			PetEntry entry = Find(id);
			if (entry != null)
			{
				return entry.Grade;
			}

			Table_Pet.Row row = PetCatalog.Get(id);
			return (row != null) ? row.Grade : ItemGradeType.None;
		}

		// ── 변경 ──────────────────────────────────────────────────────

		// 지급으로 보유에 추가한다. 이미 가지고 있으면 아무것도 하지 않는다(중복 보유가 없다).
		public bool Grant(EDT.Pet id)
		{
			if (id == EDT.Pet.None || _owned.ContainsKey(id) == true)
			{
				return false;
			}

			Table_Pet.Row row = PetCatalog.Get(id);
			if (row == null)
			{
				return false;
			}

			_owned.Add(id, new PetEntry(id, row.Grade));
			notifyChanged(id);
			return true;
		}

		// 장착. 이전에 장착한 펫은 필드를 덮어쓰는 것으로 자동 해제된다.
		public bool TryEquip(EDT.Pet id)
		{
			if (IsOwned(id) == false)
			{
				return false;
			}

			if (_equipped == id)
			{
				return true;
			}

			_equipped = id;
			notifyChanged(id);
			return true;
		}

		public void Unequip()
		{
			if (_equipped == EDT.Pet.None)
			{
				return;
			}

			EDT.Pet prev = _equipped;
			_equipped = EDT.Pet.None;
			notifyChanged(prev);
		}

		// ── 강화 ──────────────────────────────────────────────────────

		// 강화가 막힌 이유. 버튼 잠금도 TryEnhance 도 이것만 본다.
		public PetEnhanceBlock GetEnhanceBlock(EDT.Pet id)
		{
			PetEntry entry = Find(id);
			if (entry == null)
			{
				return PetEnhanceBlock.NotOwned;
			}

			if (entry.IsMaxLevel == true)
			{
				return PetEnhanceBlock.MaxLevel;
			}

			EDT.Currency currency;
			int amount;
			if (PetCatalog.TryGetEnhanceCost(entry.Level, out currency, out amount) == false)
			{
				return PetEnhanceBlock.NoTable;
			}

			if (CurrencyManager.Instance.GetAmount(currency) < amount)
			{
				return PetEnhanceBlock.NotEnough;
			}

			return PetEnhanceBlock.None;
		}

		public bool TryEnhance(EDT.Pet id)
		{
			if (GetEnhanceBlock(id) != PetEnhanceBlock.None)
			{
				return false;
			}

			PetEntry entry = Find(id);

			EDT.Currency currency;
			int amount;
			PetCatalog.TryGetEnhanceCost(entry.Level, out currency, out amount);

			if (CurrencyManager.Instance.TrySpend(currency, amount) == false)
			{
				return false;
			}

			entry.LevelUp();
			notifyChanged(id);
			return true;
		}

		// ── 승급 ──────────────────────────────────────────────────────

		// 레벨 조건은 보지 않는다 — PetPromotion 테이블에 그런 컬럼이 없다.
		public PetPromoteBlock GetPromoteBlock(EDT.Pet id)
		{
			PetEntry entry = Find(id);
			if (entry == null)
			{
				return PetPromoteBlock.NotOwned;
			}

			Table_PetPromotion.Row row = PetCatalog.GetPromotion(entry.Grade);
			if (row == null)
			{
				return PetPromoteBlock.MaxGrade;
			}

			if (row.ToGrade == ItemGradeType.None || row.CostCurrencyID == EDT.Currency.None)
			{
				return PetPromoteBlock.NoTable;
			}

			if (CurrencyManager.Instance.GetAmount(row.CostCurrencyID) < row.CostCurrencyValue)
			{
				return PetPromoteBlock.NotEnough;
			}

			return PetPromoteBlock.None;
		}

		public bool TryPromote(EDT.Pet id)
		{
			if (GetPromoteBlock(id) != PetPromoteBlock.None)
			{
				return false;
			}

			PetEntry entry = Find(id);
			Table_PetPromotion.Row row = PetCatalog.GetPromotion(entry.Grade);

			if (CurrencyManager.Instance.TrySpend(row.CostCurrencyID, row.CostCurrencyValue) == false)
			{
				return false;
			}

			entry.Promote(row.ToGrade);
			notifyChanged(id);
			return true;
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public PetDto ToDto()
		{
			PetDto dto = new PetDto();

			Dictionary<EDT.Pet, PetEntry>.Enumerator e = _owned.GetEnumerator();
			while (e.MoveNext() == true)
			{
				dto.pets.Add(e.Current.Value.ToDto());
			}

			dto.equippedPetId = (int)_equipped;
			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 보유·레벨·등급이 바뀌면 보유 효과가 달라지고, 장착이 바뀌면 따라다니는 모델이 달라진다.
		// 둘 다 여기서 한 번에 처리한다 — 호출부가 무엇을 다시 굽을지 고르지 않게 한다.
		private void notifyChanged(EDT.Pet id)
		{
			reapplyHero();
			PetSpawner.Instance.Refresh();

			EventManager.Instance.Publish(new PetChangeEvent(id));
		}

		// 활성 Hero 의 펫 출처만 다시 적용한다 (CostumeBook.reapplyAvatar 의 펫판).
		private void reapplyHero()
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

				HeroAspectRegistry.Instance.Reapply(hero, PetSource);
			}
		}

		private void buildFromDto(PetDto dto)
		{
			_owned.Clear();
			_equipped = EDT.Pet.None;

			// 서버 DTO 주입 — 없으면 빈 상태(미로그인/오프라인).
			if (dto == null)
			{
				return;
			}

			if (dto.pets != null)
			{
				for (int i = 0; i < dto.pets.Count; i++)
				{
					PetEntryDto src = dto.pets[i];
					if (src == null)
					{
						continue;
					}

					EDT.Pet id = (EDT.Pet)src.petId;
					if (id == EDT.Pet.None || _owned.ContainsKey(id) == true)
					{
						continue;
					}

					// 테이블에 없는 펫은 버린다 — 테이블이 줄어든 뒤의 옛 저장 데이터 방어.
					Table_Pet.Row row = PetCatalog.Get(id);
					if (row == null)
					{
						continue;
					}

					PetEntry entry = new PetEntry(id, row.Grade);
					entry.LoadFrom(src);
					_owned.Add(id, entry);
				}
			}

			// 보유하지 않은 것이 장착으로 남아 있으면 버린다(데이터 불일치 방어).
			EDT.Pet equipped = (EDT.Pet)dto.equippedPetId;
			if (equipped != EDT.Pet.None && _owned.ContainsKey(equipped) == true)
			{
				_equipped = equipped;
			}
		}
	}
}
