using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업. UIManager.ShowItemInfoPopupAsync가 ShowAsync로 닫힘을 기다린다.
	// 정보 표시 + 현재 선택 캐릭터의 장착/해제를 담당한다.
	public class ItemInfoPopup : UIScreen
	{
		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _nameText;	// NameText
		[SerializeField] private TMP_Text _gradeText;	// GradeText
		[SerializeField] private TMP_Text _descText;	// DescText

		[Header("등급 색상 대상")]
		[SerializeField] private Image _topBg;	// 연한 색(bgMask)
		[SerializeField] private Image _deco2;	// 진한 색(gradient)

		[Header("슬롯 / 옵션")]
		[SerializeField] private Transform _itemSlotRoot;	// ItemSlotRoot
		[SerializeField] private Transform _gridParent;		// GridLayout_Equipment

		[Header("버튼")]
		[SerializeField] private UIButton _equipButton;			// EquipButton
		[SerializeField] private TMP_Text _equipButtonLabel;	// EquipButton 라벨
		[SerializeField] private UIButton _exitButton;			// ExitButton

		[Header("프리펩 / 데이터")]
		[SerializeField] private EquipmentSlot _inventorySlotPrefab;		// Prefab_InventorySlot
		[SerializeField] private EquipmentOptionSlot _statSlotPrefab;	// Prefab_EquipmentStatSlot
		[SerializeField] private EquipmentOptionSlot _skillSlotPrefab;	// Prefab_EquipmentSkillSlot
		[SerializeField] private GradeColorTable _gradeColors;

		private int _itemId;
		private EquipmentTypes _type = EquipmentTypes.None;
		private UniTaskCompletionSource<bool> _tcs;

		private void Awake()
		{
			_equipButton.OnClickEvent += onEquipClicked;
			_exitButton.OnClickEvent += onExitClicked;
		}

		private void OnDestroy()
		{
			_equipButton.OnClickEvent -= onEquipClicked;
			_exitButton.OnClickEvent -= onExitClicked;
		}

		// UIManager가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public async UniTask ShowAsync(int itemId, CancellationToken ct)
		{
			Table_Equipment.Row row = Table_Equipment.Get(itemId);
			if (row == null)
			{
				return;
			}

			_itemId = itemId;
			_type = row.EquipmentType;

			applyGradeColor(row.Grade);
			_nameText.text = row.Name;
			_gradeText.text = row.Grade.ToString();
			_descText.text = row.Desc;

			Inventory inventory = Account.Instance.Inventory;
			bool owned = inventory.Has(itemId);

			await bindItemSlot(row, owned, inventory, ct);
			buildOptions(row);

			_equipButton.interactable = owned;	// 미보유면 장착 불가
			refreshEquipButton();

			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		private void applyGradeColor(ItemGradeTypes grade)
		{
			GradeColorTable.GradeColor gc = _gradeColors.Get(grade);
			_topBg.color = gc.bgMask;	// 연한 색
			_deco2.color = gc.gradient;	// 진한 색
		}

		// ItemSlotRoot 에 인벤토리 슬롯을 생성해 등급/아이콘/레벨/개수를 표시하되,
		// 미보유(Unlock)·장착(Focus) 표시는 숨긴다.
		private async UniTask bindItemSlot(Table_Equipment.Row row, bool owned, Inventory inventory, CancellationToken ct)
		{
			EquipmentSlot slot = Instantiate(_inventorySlotPrefab, _itemSlotRoot);
			int count = inventory.GetCount(row.ID);
			int level = inventory.GetEnhanceLevel(row.ID);
			await slot.Bind(row, owned, count, level, false, _gradeColors, ct);
			slot.HideStatusObjects();
		}

		// 스탯 옵션 1~3, 스킬 옵션 1~2 순서대로 GridLayout 에 슬롯을 추가한다.
		private void buildOptions(Table_Equipment.Row row)
		{
			addStatOption(row.StatOptionType_1, row.StatOptionValue_1);
			addStatOption(row.StatOptionType_2, row.StatOptionValue_2);
			addStatOption(row.StatOptionType_3, row.StatOptionValue_3);

			addSkillOption(row.SkillOption_1);
			addSkillOption(row.SkillOption_2);
		}

		private void addStatOption(StatInfo type, float value)
		{
			if (type == StatInfo.None)
			{
				return;
			}

			Table_StatInfo.Row info = Table_StatInfo.Get(type);
			string title = info != null ? info.Name : type.ToString();
			string text = info != null && info.IsRatio ? value.ToString("0.##") + "%" : value.ToString("0.##");

			EquipmentOptionSlot slot = Instantiate(_statSlotPrefab, _gridParent);
			slot.Set(title, text);
		}

		private void addSkillOption(int skillOption)
		{
			if (skillOption == 0)
			{
				return;
			}

			Table_SkillInfo.Row info = Table_SkillInfo.Get((SkillInfo)skillOption);
			string skillName = info != null ? info.Name : string.Empty;

			EquipmentOptionSlot slot = Instantiate(_skillSlotPrefab, _gridParent);
			slot.Set(null, skillName);	// 스킬은 제목 없이 이름만
		}

		// 현재 장착 여부에 따라 라벨을 Equip/Unequip 으로 갱신한다.
		private void refreshEquipButton()
		{
			_equipButtonLabel.text = isEquipped() ? "Unequip" : "Equip";
		}

		private bool isEquipped()
		{
			Loadout loadout = Account.Instance.Loadout;
			return loadout.GetSlot(loadout.Selected, _type) == _itemId;
		}

		private void onEquipClicked()
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

			refreshEquipButton();
		}

		private void onExitClicked()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}
	}
}
