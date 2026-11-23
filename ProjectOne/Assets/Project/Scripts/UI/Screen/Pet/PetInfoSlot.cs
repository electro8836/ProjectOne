using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 펫 목록 칸 1개 렌더 데이터. 보유 판정·등급색은 Presenter 가 끝낸다.
	public struct PetSlotData
	{
		public int pet;
		public bool owned;
		public string imageAddress;
		public string name;
		public int level;			// 미보유는 0 — 레벨 글자 자체가 꺼지므로 쓰이지 않는다
		public bool equipped;		// 장착 중인 한 마리만 true — 장착은 보유를 전제한다
		public bool selected;		// 위 무대에 보이는 펫
		public Color bgColor;		// 칸 바탕 판 — 등급 바탕색(보유만 쓴다)
		public Color gradientColor;	// 칸 틀 — 등급 테두리색(보유만 쓴다)
	}

	// 펫 목록 칸 1개 (UI Toolkit). 칸 하나에 틀·바탕·그림, 왼쪽 위 레벨, 오른쪽 위 장착/잠금 표시, 아래 이름 띠.
	// 보유 칸은 등급색, 미보유 칸은 실루엣·자물쇠. 누르면 위 무대에 그 펫을 보인다(미보유도 누를 수 있다).
	// 스타일은 UIToolkit/Screens/Pet/PetScreen.uss.
	public sealed class PetCell
	{
		private readonly ToolkitIconLoader _icons;

		private readonly VisualElement _root;
		private readonly VisualElement _frame;
		private readonly VisualElement _bg;
		private readonly VisualElement _image;
		private readonly Label _level;
		private readonly Label _name;

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
			_root.AddToClassList("pet-cell");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			_frame = createPart("pet-cell__frame");
			_bg = createPart("pet-cell__bg");
			_frame.Add(_bg);
			_image = createPart("pet-cell__image");
			_frame.Add(_image);
			_level = new Label();
			_level.AddToClassList("pet-cell__level");
			_level.pickingMode = PickingMode.Ignore;
			_frame.Add(_level);
			_frame.Add(createPart("pet-cell__equip"));
			_frame.Add(createPart("pet-cell__lock"));

			_name = new Label();
			_name.AddToClassList("pet-cell__name");
			_name.pickingMode = PickingMode.Ignore;
			_frame.Add(_name);

			// 고른 칸 표시 — 등급색 틀은 두고 안쪽에 흰 선
			_frame.Add(createPart("pet-cell__select"));
			_root.Add(_frame);

			parent.Add(_root);
		}

		public UniTask BindAsync(PetSlotData data, CancellationToken ct)
		{
			_petId = data.pet;

			_root.EnableInClassList("pet-cell--locked", data.owned == false);
			_root.EnableInClassList("pet-cell--equipped", data.equipped);
			_root.EnableInClassList("pet-cell--selected", data.selected);

			// 등급색은 보유 칸만 — 미보유 칸은 USS 의 잠금 색을 쓰도록 인라인 값을 지운다
			if (data.owned == true)
			{
				_frame.style.unityBackgroundImageTintColor = data.gradientColor;
				_bg.style.unityBackgroundImageTintColor = data.bgColor;
			}
			else
			{
				_frame.style.unityBackgroundImageTintColor = StyleKeyword.Null;
				_bg.style.unityBackgroundImageTintColor = StyleKeyword.Null;
			}

			setShown(_level, data.owned);
			if (data.owned == true)
			{
				_level.text = "Lv." + data.level.ToString();
			}

			_name.text = data.name;

			return _icons.SetAsync(_image, data.imageAddress, ct);
		}

		// 고른 칸 표시만 바꾼다 — 고를 때마다 목록 전체를 다시 묶지 않는다
		public void SetSelected(bool selected)
		{
			_root.EnableInClassList("pet-cell--selected", selected);
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

		private static VisualElement createPart(string className)
		{
			VisualElement element = new VisualElement();
			element.AddToClassList(className);
			element.pickingMode = PickingMode.Ignore;
			return element;
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
