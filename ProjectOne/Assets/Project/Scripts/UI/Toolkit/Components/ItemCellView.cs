using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using ProjectOne.Resources;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 아이템 칸 1개 (UI구조_설계.md 5) — 등급 틀·바탕·아이콘·분류 배지·레벨·개수·품질 막대·잠금·장착 표시.
	// UGUI ItemSlot 과 같은 표시 규칙이다. 스타일은 UIToolkit/Components/ItemCell.uss (Theme.uss 가 가져온다).
	//
	// 요소는 코드로 만든다 — 어느 화면이든 템플릿 연결 없이 new 한 번으로 칸을 붙인다.
	// 아이콘은 소유 화면의 ToolkitIconLoader 로 받는다(화면이 닫힐 때 한꺼번에 해제).
	public sealed class ItemCellView
	{
		private readonly ItemGradeColorTable _colors;
		private readonly ToolkitIconLoader _icons;

		private readonly VisualElement _root;
		private readonly VisualElement _bg;
		private readonly VisualElement _icon;
		private readonly VisualElement _type;
		private readonly VisualElement _typeIcon;
		private readonly VisualElement _lock;
		private readonly VisualElement _equip;
		private readonly Label _level;
		private readonly Label _count;
		private readonly VisualElement _quality;
		private readonly VisualElement _qualityFill;

		// 클릭 시 넘길 식별자 — uid 는 장비 인스턴스, itemId 는 테이블 행(재화면 재화 종류). 스택 아이템은 uid 0.
		private long _uid;
		private int _itemId;

		// 어느 칸이 눌렸는지(sender)를 함께 넘긴다 — 소유 화면이 칸마다 무엇을 바인딩했는지 알아야 띄울 팝업을 정한다.
		public event Action<ItemCellView, long, int> OnClicked;

		public VisualElement Root
		{
			get { return _root; }
		}

		public long Uid
		{
			get { return _uid; }
		}

		public int ItemId
		{
			get { return _itemId; }
		}

		// 소유 화면이 칸에 붙여 두는 값(인덱스 등).
		public object Tag { get; set; }

		public ItemCellView(VisualElement parent, ItemGradeColorTable colors, ToolkitIconLoader icons)
		{
			_colors = colors;
			_icons = icons;

			_root = new VisualElement();
			_root.AddToClassList("item-cell");

			_bg = addChild("item-cell__bg");
			_icon = addChild("item-cell__icon");
			_type = addChild("item-cell__type");
			_typeIcon = new VisualElement();
			_typeIcon.AddToClassList("item-cell__type-icon");
			_typeIcon.pickingMode = PickingMode.Ignore;
			_type.Add(_typeIcon);
			_lock = addChild("item-cell__lock");
			// 장착 표시 — 판(테마가 칩·배지로 칠함) 안에 글자. 바이올렛 테마는 글자를 감추고 E 배지 그림만 쓴다
			_equip = addChild("item-cell__equip");
			Label equipText = new Label("장착");
			equipText.AddToClassList("item-cell__equip-text");
			equipText.pickingMode = PickingMode.Ignore;
			_equip.Add(equipText);
			_level = addLabel("item-cell__level", string.Empty);
			_count = addLabel("item-cell__count", string.Empty);
			_quality = addChild("item-cell__quality");
			_qualityFill = new VisualElement();
			_qualityFill.AddToClassList("item-cell__quality-fill");
			_qualityFill.pickingMode = PickingMode.Ignore;
			_quality.Add(_qualityFill);

			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			parent.Add(_root);
		}

		// 장비 인스턴스 — 등급·레벨·품질은 인스턴스가 소유한다. 장착 여부는 Loadout 이 정하므로 호출자가 알려준다.
		public UniTask BindEquipmentAsync(EquipmentInstance instance, bool equipped, CancellationToken ct)
		{
			_uid = instance.uid;
			_itemId = instance.itemId;

			applyGrade(instance.grade);
			SetEquipped(equipped);
			SetLocked(instance.locked);

			setShown(_level, true);
			_level.text = "레벨 " + instance.level;
			setShown(_count, false);

			setShown(_quality, true);
			float rate = ProjectOne.Shared.EquipmentQuality.ToRate(instance.quality);
			_qualityFill.style.width = Length.Percent(Mathf.Clamp01(rate) * 100f);

			Table_Item.Row row = instance.Item;
			applyTypeIcon(row);
			return _icons.SetAsync(_icon, (row != null) ? row.Icon : string.Empty, ct);
		}

		// 스택 아이템(소모품) — 인스턴스가 없어 등급은 테이블 값. 개수는 2개 이상만 적는다.
		public UniTask BindItemAsync(Table_Item.Row row, int count, CancellationToken ct)
		{
			_uid = 0;
			_itemId = (row != null) ? row.ID : 0;

			applyGrade((row != null) ? row.Grade : ItemGradeType.None);
			applyTypeIcon(row);
			SetEquipped(false);
			SetLocked(false);
			setShown(_level, false);
			setShown(_quality, false);

			setShown(_count, count > 1);
			_count.text = count.ToString();

			return _icons.SetAsync(_icon, (row != null) ? row.Icon : string.Empty, ct);
		}

		// 재화 — 등급·분류·강화·품질이 없다. 개수는 항상 적는다.
		public UniTask BindCurrencyAsync(EDT.Currency currency, int count, CancellationToken ct)
		{
			_uid = 0;
			_itemId = (int)currency;

			applyGrade(ItemGradeType.Normal);
			setShown(_type, false);
			setShown(_level, false);
			SetEquipped(false);
			SetLocked(false);
			setShown(_quality, false);

			setShown(_count, true);
			_count.text = count.ToString();

			Table_Currency.Row row = Table_Currency.Get(currency);
			return _icons.SetAsync(_icon, (row != null) ? row.Icon : string.Empty, ct);
		}

		// 내용을 비운다 — 빈 칸(틀만)으로 남긴다. 클릭은 무시된다.
		public void Clear()
		{
			_uid = 0;
			_itemId = 0;
			applyGrade(ItemGradeType.None);
			setShown(_type, false);
			setShown(_level, false);
			setShown(_count, false);
			setShown(_quality, false);
			SetEquipped(false);
			SetLocked(false);
			_icon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
		}

		public void SetEquipped(bool equipped)
		{
			setShown(_equip, equipped);
			_root.EnableInClassList("item-cell--equipped", equipped);	// 바이올렛 테마는 장착 표시가 있으면 잠금을 그 아래로 내린다
		}

		public void SetLocked(bool locked)
		{
			setShown(_lock, locked);
		}

		public void SetInteractable(bool value)
		{
			_root.SetEnabled(value);
		}

		// 강화 레벨 표시를 끈다 — 레벨을 따로 적는 화면용(Bind 뒤에 호출).
		public void HideLevel()
		{
			setShown(_level, false);
		}

		// 등급 틀·바탕을 지정한 등급으로 칠한다 (보상 미리보기 등 아이템 없이 등급만 보일 때).
		public void SetGrade(ItemGradeType grade)
		{
			applyGrade(grade);
		}

		public void SetShown(bool shown)
		{
			setShown(_root, shown);
		}

		// ── 내부 ─────────────────────────────────────────────────────────

		private VisualElement addChild(string className)
		{
			VisualElement element = new VisualElement();
			element.AddToClassList(className);
			element.pickingMode = PickingMode.Ignore;
			_root.Add(element);
			return element;
		}

		private Label addLabel(string className, string text)
		{
			Label label = new Label(text);
			label.AddToClassList(className);
			label.pickingMode = PickingMode.Ignore;
			_root.Add(label);
			return label;
		}

		private void applyGrade(ItemGradeType grade)
		{
			if (_colors == null)
			{
				return;
			}

			ItemGradeColorTable.GradeColor gc = _colors.Get(grade);
			_root.style.unityBackgroundImageTintColor = gc.border;
			_bg.style.backgroundColor = gc.bg;
			_bg.style.unityBackgroundImageTintColor = gc.bg;	// 바탕 판이 그라데이션 이미지인 테마(바이올렛)는 틴트로 칠한다
		}

		// 분류 배지 — 바탕은 등급과 무관한 고정색(ItemCell.uss). 아이콘은 Atlas_Icon 에 상주하므로 동기로 바로 입힌다. 분류가 없으면(재료·수집품) 배지를 감춘다.
		private void applyTypeIcon(Table_Item.Row row)
		{
			string address = ItemTypeIcons.Get(row);
			Sprite sprite = string.IsNullOrEmpty(address) ? null : AtlasManager.Instance.Get(address);
			if (sprite == null)
			{
				setShown(_type, false);
				return;
			}

			setShown(_type, true);
			_typeIcon.style.backgroundImage = new StyleBackground(sprite);
		}

		private void onClicked(ClickEvent evt)
		{
			if (_uid == 0 && _itemId == 0)
			{
				return;
			}

			if (OnClicked != null)
			{
				OnClicked.Invoke(this, _uid, _itemId);
			}
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
