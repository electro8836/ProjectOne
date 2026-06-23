using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 옵션 1줄 렌더 데이터 — isSkill 로 스탯/스킬 슬롯 프리펩을 구분한다.
	public struct ItemOptionEntry
	{
		public string title;
		public string text;
		public bool isSkill;
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
				return;
			}

			_itemId = itemId;
			_type = row.EquipmentType;

			Inventory inventory = Account.Instance.Inventory;
			bool owned = inventory.Has(itemId);
			int count = inventory.GetCount(itemId);
			int level = inventory.GetEnhanceLevel(itemId);

			view.SetInfo(row);
			await view.BindItemSlotAsync(row, owned, count, level, ct);
			view.BuildOptions(buildOptions(row));

			view.SetEquipInteractable(owned);	// 미보유면 장착 불가
			view.SetEquipLabel(equipLabel());

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
			return isEquipped() ? "Unequip" : "Equip";
		}

		// 스탯 옵션 1~3, 스킬 옵션 1~2 순서대로 렌더 데이터를 만든다.
		private List<ItemOptionEntry> buildOptions(Table_Equipment.Row row)
		{
			List<ItemOptionEntry> entries = new List<ItemOptionEntry>();
			addStatOption(entries, row.StatOptionType_1, row.StatOptionValue_1);
			addStatOption(entries, row.StatOptionType_2, row.StatOptionValue_2);
			addStatOption(entries, row.StatOptionType_3, row.StatOptionValue_3);

			addSkillOption(entries, row.SkillOption_1);
			addSkillOption(entries, row.SkillOption_2);
			return entries;
		}

		private void addStatOption(List<ItemOptionEntry> entries, StatInfo type, float value)
		{
			if (type == StatInfo.None)
			{
				return;
			}

			Table_StatInfo.Row info = Table_StatInfo.Get(type);
			string title = info != null ? info.Name : type.ToString();
			string text = info != null && info.IsRatio ? value.ToString("0.##") + "%" : value.ToString("0.##");

			ItemOptionEntry entry;
			entry.title = title;
			entry.text = text;
			entry.isSkill = false;
			entries.Add(entry);
		}

		private void addSkillOption(List<ItemOptionEntry> entries, int skillOption)
		{
			if (skillOption == 0)
			{
				return;
			}

			Table_SkillInfo.Row info = Table_SkillInfo.Get((SkillInfo)skillOption);
			string skillName = info != null ? info.Name : string.Empty;

			ItemOptionEntry entry;
			entry.title = null;	// 스킬은 제목 없이 이름만
			entry.text = skillName;
			entry.isSkill = true;
			entries.Add(entry);
		}
	}
}
