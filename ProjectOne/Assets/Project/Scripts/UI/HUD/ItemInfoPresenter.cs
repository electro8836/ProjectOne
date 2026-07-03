using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 옵션 1줄 렌더 데이터 — 아이콘 주소 + 제목 + 설명(스탯/스킬/특성 공용).
	public struct ItemOptionEntry
	{
		public string iconAddress;
		public string title;
		public string desc;
	}

	// 아이템 정보 팝업 Presenter — 표시 데이터 계산과 장착/해제 결정(Loadout 조작)을 담당한다.
	// 장착 변경은 Loadout 이 dirty 로 누적하고, 실제 서버 저장은 장비 화면 닫힘 시 1회 flush 된다.
	public sealed class ItemInfoPresenter : Presenter<ItemInfoPopup>
	{
		private int _itemId;
		private EquipmentTypes _type = EquipmentTypes.None;

		protected override void OnInitialize()
		{
			view.OnEquipToggleClicked += onEquipToggleClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnEquipToggleClicked -= onEquipToggleClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 팝업 표시 — 데이터 계산 후 View 에 그리기 지시, 닫힘까지 대기.
		public async UniTask ShowAsync(int itemId, CancellationToken ct)
		{
			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null)
			{
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				return;
			}

			_itemId = itemId;
			_type = row.EquipmentType;

			Inventory inventory = Account.Instance.Inventory;
			bool owned = inventory.Has(itemId);
			int count = inventory.GetCount(itemId);
			int level = inventory.GetEnhanceLevel(itemId);

			view.SetInfo(row);
			view.SetEquipInteractable(owned);	// 미보유면 장착 불가
			view.SetEquipLabel(equipLabel());

			// 아이콘 로드가 끝난 뒤 한 번에 표시
			await view.BuildOptionsAsync(buildOptions(row), ct);
			await view.BindItemSlotAsync(row, owned, count, level, ct);
			view.Reveal();

			await view.WaitForCloseAsync(ct);
		}

		// 장착/해제 토글 — 현재 장착 중이면 해제, 아니면 장착.
		private void onEquipToggleClicked()
		{
			Loadout loadout = Account.Instance.Loadout;
			int selected = loadout.Selected;

			if (isEquipped())
			{
				loadout.ClearSlot(selected, _type);
			}
			else
			{
				loadout.TrySetSlot(selected, _type, _itemId);
			}

			view.SetEquipLabel(equipLabel());
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		private bool isEquipped()
		{
			Loadout loadout = Account.Instance.Loadout;
			return loadout.GetSlot(loadout.Selected, _type) == _itemId;
		}

		private string equipLabel()
		{
			return isEquipped() ? "장착해제" : "장착";
		}

		// 스탯 옵션 1~3, 스킬 옵션 1~2, 특성 옵션 순서대로 렌더 데이터를 만든다.
		private List<ItemOptionEntry> buildOptions(Table_Equipment.Row row)
		{
			List<ItemOptionEntry> entries = new List<ItemOptionEntry>();
			addStatOption(entries, row.StatOptionType_1, row.StatOptionValue_1);
			addStatOption(entries, row.StatOptionType_2, row.StatOptionValue_2);
			addStatOption(entries, row.StatOptionType_3, row.StatOptionValue_3);

			addSkillOption(entries, row.SkillOption_1);
			addSkillOption(entries, row.SkillOption_2);

			addTraitOption(entries, row);
			return entries;
		}

		// 스탯: 아이콘 + 스탯 이름 + 값(비율이면 % 부착).
		private void addStatOption(List<ItemOptionEntry> entries, StatInfo type, float value)
		{
			if (type == StatInfo.None)
			{
				return;
			}

			Table_StatInfo.Row info = Table_StatInfo.Get(type);
			if (info == null)
			{
				return;
			}

			string desc = info.IsRatio ? value.ToString("0.##") + "%" : value.ToString("0.##");

			ItemOptionEntry entry;
			entry.iconAddress = info.Icon;
			entry.title = info.Name;
			entry.desc = desc;
			entries.Add(entry);
		}

		// 스킬: 아이콘 + 스킬 이름 + 스킬 테이블 Desc 원문.
		private void addSkillOption(List<ItemOptionEntry> entries, SkillInfo skillOption)
		{
			if (skillOption == SkillInfo.None)
			{
				return;
			}

			Table_SkillInfo.Row info = Table_SkillInfo.Get(skillOption);
			if(info == null)
			{
				return;
			}

			ItemOptionEntry entry;
			entry.iconAddress = info.Icon;
			entry.title = info.Name;
			entry.desc = info.Desc;
			entries.Add(entry);
		}

		// 특성: Trait_Icon + "n번 특성 스킬 강화" + 설명(선택 캐릭터의 해당 특성 이름 포함).
		private void addTraitOption(List<ItemOptionEntry> entries, Table_Equipment.Row row)
		{
			int index = row.TraitSlotIndex;
			int value = row.TraitSlotValue;
			if (index <= 0 || value == 0)
			{
				return;
			}

			ItemOptionEntry entry;
			entry.iconAddress = "Trait_Icon";
			entry.title = index + "번 특성 스킬 강화";
			entry.desc = "캐릭터의 " + ordinal(index) + " 특성 스킬 레벨이 " + value + " 증가합니다\n[<color=#2ECC71>" + traitName(index) + "</color>]";
			entries.Add(entry);
		}

		// 선택 캐릭터의 index번 특성그룹 이름. 없으면 빈 문자열.
		private string traitName(int index)
		{
			int selected = Account.Instance.Loadout.Selected;
			Table_Character.Row charRow = Table_Character.Get(selected);
			if (charRow == null)
			{
				return string.Empty;
			}

			int groupId = traitGroupByIndex(charRow, index);
			Table_CharacterTrait.Row trait = Table_CharacterTrait.Get(groupId);
			if (trait== null)
			{
				return string.Empty; ;
			}

			return trait.Name;
		}

		// 캐릭터의 1~5번 특성그룹 ID (TraitGroup_n 필드 분기).
		private int traitGroupByIndex(Table_Character.Row charRow, int index)
		{
			switch (index)
			{
				case 1: return charRow.TraitGroup_1;
				case 2: return charRow.TraitGroup_2;
				case 3: return charRow.TraitGroup_3;
				case 4: return charRow.TraitGroup_4;
				case 5: return charRow.TraitGroup_5;
				default: return 0;
			}
		}

		// 1~5의 한글 서수.
		private string ordinal(int index)
		{
			switch (index)
			{
				case 1: return "첫 번째";
				case 2: return "두 번째";
				case 3: return "세 번째";
				case 4: return "네 번째";
				case 5: return "다섯 번째";
				default: return index + "번째";
			}
		}
	}
}
