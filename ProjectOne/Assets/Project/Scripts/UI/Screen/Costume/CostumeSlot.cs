using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 코스튬 목록 칸 1개 렌더 데이터.
	// 보유/미보유 판정과 문구 결정은 Presenter 가 끝내고, 칸은 받은 값을 그리기만 한다.
	public struct CostumeSlotData
	{
		public int costumeId;
		public bool owned;
		public bool equipped;
		public bool previewing;			// 지금 입어보는 중인가
		public string iconAddress;
		public string name;
		public string gradeText;			// 등급색이 입혀진 리치텍스트. 무기면 " / 쌍검" 처럼 마스터리가 붙는다
		public string acquisitionText;		// 미보유일 때만 보인다
		public string equipLabel;			// "장착" / "해제"
	}

	// 코스튬 목록 칸 1개 (UI Toolkit). 보유/미보유 두 상태를 같은 칸으로 표현한다.
	//
	// 미보유 칸도 입어보기는 된다 — 무엇을 사는지 보여 주는 것이 목록의 목적이라 미리보기를 막지 않는다.
	// 대신 장착 버튼은 감추고 그 자리에 획득처를 띄운다. 스타일은 UIToolkit/Screens/Costume/CostumeScreen.uss.
	public sealed class CostumeCell
	{
		private readonly ToolkitIconLoader _icons;

		private readonly VisualElement _root;
		private readonly VisualElement _icon;
		private readonly Label _name;
		private readonly Label _grade;
		private readonly Label _acquisition;
		private readonly Button _preview;
		private readonly Button _equip;

		private int _costumeId;

		public event Action<CostumeCell> OnPreviewClicked;
		public event Action<CostumeCell> OnEquipClicked;

		public int CostumeId
		{
			get { return _costumeId; }
		}

		public CostumeCell(VisualElement parent, ToolkitIconLoader icons)
		{
			_icons = icons;

			_root = new VisualElement();
			_root.AddToClassList("costume-card");

			VisualElement frame = new VisualElement();
			frame.AddToClassList("costume-card__icon-frame");
			_icon = new VisualElement();
			_icon.AddToClassList("icon");
			frame.Add(_icon);
			_root.Add(frame);

			_name = new Label();
			_name.AddToClassList("costume-card__name");
			_root.Add(_name);

			_grade = new Label();
			_grade.AddToClassList("costume-card__grade");
			_root.Add(_grade);

			VisualElement buttons = new VisualElement();
			buttons.AddToClassList("costume-card__buttons");

			_preview = new Button();
			_preview.text = "입어보기";
			_preview.AddToClassList("btn");
			_preview.AddToClassList("btn--small");
			_preview.AddToClassList("costume-card__preview");
			_preview.clicked += onPreviewClicked;
			buttons.Add(_preview);

			_equip = new Button();
			_equip.AddToClassList("btn");
			_equip.AddToClassList("btn--small");
			_equip.AddToClassList("costume-card__equip");
			_equip.clicked += onEquipClicked;
			buttons.Add(_equip);

			_acquisition = new Label();
			_acquisition.AddToClassList("costume-card__acquisition");
			buttons.Add(_acquisition);

			_root.Add(buttons);
			parent.Add(_root);
		}

		public UniTask BindAsync(CostumeSlotData data, CancellationToken ct)
		{
			_costumeId = data.costumeId;

			_root.EnableInClassList("costume-card--locked", data.owned == false);
			_root.EnableInClassList("costume-card--previewing", data.previewing);
			_preview.EnableInClassList("btn--green", data.previewing);

			_name.text = data.name;
			_grade.text = data.gradeText;

			// 획득처와 장착 버튼은 같은 자리를 나눠 쓴다 — 항상 둘 중 하나만 보인다.
			setShown(_acquisition, data.owned == false);
			setShown(_equip, data.owned);
			if (data.owned == false)
			{
				_acquisition.text = data.acquisitionText;
			}
			else
			{
				_equip.text = data.equipLabel;
				_equip.EnableInClassList("btn--red", data.equipped);
				_equip.EnableInClassList("btn--green", data.equipped == false);
			}

			return _icons.SetAsync(_icon, data.iconAddress, ct);
		}

		public void SetShown(bool shown)
		{
			setShown(_root, shown);
		}

		private void onPreviewClicked()
		{
			if (OnPreviewClicked != null)
			{
				OnPreviewClicked.Invoke(this);
			}
		}

		private void onEquipClicked()
		{
			if (OnEquipClicked != null)
			{
				OnEquipClicked.Invoke(this);
			}
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
