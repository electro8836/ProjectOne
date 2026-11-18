using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 펫 목록 칸 1개 렌더 데이터. 보유 판정·등급색·문구는 Presenter 가 끝낸다.
	public struct PetSlotData
	{
		public int pet;
		public bool owned;
		public string imageAddress;
		public string name;
		public int level;			// 미보유는 0 — 뱃지 자체가 꺼지므로 쓰이지 않는다
		public string bonusText;	// 보유는 수치까지, 미보유는 "공격력 증가" 처럼 간략히
		public bool equipped;		// 장착 중인 한 마리만 true — 장착은 보유를 전제한다
		public Color bgColor;
		public Color gradientColor;
		public Color textColor;
	}

	// 펫 목록 칸 1개 (UI Toolkit). 보유 칸은 등급색 바탕·레벨 뱃지, 미보유 칸은 잠금 표시이며 누를 수 없다.
	// 스타일은 UIToolkit/Screens/Pet/PetScreen.uss.
	public sealed class PetCell
	{
		private readonly ToolkitIconLoader _icons;

		private readonly VisualElement _root;
		private readonly VisualElement _image;
		private readonly Label _level;
		private readonly VisualElement _lock;
		private readonly Label _name;
		private readonly Label _bonusTitle;
		private readonly Label _bonus;
		private readonly Label _equip;

		private int _petId;

		public event Action<PetCell> OnClicked;

		public int PetId
		{
			get { return _petId; }
		}

		public PetCell(VisualElement parent, ToolkitIconLoader icons)
		{
			_icons = icons;

			_root = new VisualElement();
			_root.AddToClassList("pet-card");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			_image = new VisualElement();
			_image.AddToClassList("pet-card__image");
			_image.pickingMode = PickingMode.Ignore;
			_root.Add(_image);

			_level = new Label();
			_level.AddToClassList("pet-card__level");
			_level.pickingMode = PickingMode.Ignore;
			_root.Add(_level);

			_lock = new VisualElement();
			_lock.AddToClassList("pet-card__lock");
			_lock.pickingMode = PickingMode.Ignore;
			_root.Add(_lock);

			_equip = new Label("장착중");
			_equip.AddToClassList("pet-card__equip");
			_equip.pickingMode = PickingMode.Ignore;
			_root.Add(_equip);

			_name = new Label();
			_name.AddToClassList("pet-card__name");
			_name.pickingMode = PickingMode.Ignore;
			_root.Add(_name);

			_bonusTitle = new Label("보유 효과");
			_bonusTitle.AddToClassList("pet-card__bonus-title");
			_bonusTitle.pickingMode = PickingMode.Ignore;
			_root.Add(_bonusTitle);

			_bonus = new Label();
			_bonus.AddToClassList("pet-card__bonus");
			_bonus.pickingMode = PickingMode.Ignore;
			_root.Add(_bonus);

			parent.Add(_root);
		}

		public UniTask BindAsync(PetSlotData data, CancellationToken ct)
		{
			_petId = data.pet;

			_root.style.backgroundColor = data.bgColor;
			_root.style.unityBackgroundImageTintColor = data.owned ? data.gradientColor : new Color(1f, 1f, 1f, 0.6f);
			_root.EnableInClassList("pet-card--locked", data.owned == false);
			_root.EnableInClassList("pet-card--equipped", data.equipped);

			setShown(_level, data.owned);
			if (data.owned == true)
			{
				_level.text = "Lv." + data.level.ToString();
			}

			setShown(_lock, data.owned == false);
			setShown(_equip, data.equipped);

			_name.text = data.name;
			_name.style.color = data.textColor;
			_bonusTitle.style.color = data.textColor;
			_bonus.text = data.bonusText;
			_bonus.style.color = data.textColor;

			// 미보유 칸은 누를 수 없다.
			_root.SetEnabled(data.owned);

			return _icons.SetAsync(_image, data.imageAddress, ct);
		}

		public void SetShown(bool shown)
		{
			setShown(_root, shown);
		}

		private void onClicked(ClickEvent evt)
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
