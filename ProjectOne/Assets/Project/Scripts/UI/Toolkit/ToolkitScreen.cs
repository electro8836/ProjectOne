using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// UI Toolkit 화면의 베이스 (UI구조_설계.md 3.1).
	//
	// UIDocument 를 갖지 않는다. UIManager 가 층(Layer) 루트를 넘겨주면 레이아웃을 생성해 그 아래에 붙인다 —
	// 화면 수만큼 패널이 생기지 않고, 층 정렬·테마·입력이 층 하나로 모인다.
	//
	// 생명주기: Instantiate → Attach(OnBind 1회) → OnOpenAsync → … → OnCloseAsync → Destroy(Detach)
	// 요소 찾기와 Presenter 연결은 Awake 가 아니라 OnBind 에서 한다 — Awake 시점에는 아직 레이아웃이 없다.
	public abstract class ToolkitScreen : UIScreen
	{
		[SerializeField] private VisualTreeAsset _layout;

		private TemplateContainer _root;
		private VisualElement _popupFrame;

		// OnBind 가 끝났는가 — Update·OnDestroy 에서 연결 전 상태를 걸러낸다.
		protected bool IsBound
		{
			get { return _root != null; }
		}

		protected VisualElement Root
		{
			get { return _root; }
		}

		// 층 루트 아래에 붙인다. 처음 붙을 때만 레이아웃을 생성하고 OnBind 를 부른다.
		public void Attach(VisualElement layerRoot)
		{
			if (_layout == null)
			{
				Debug.LogError($"[ToolkitScreen] {name} 의 _layout(UXML)이 비었습니다.");
				return;
			}

			if (_root == null)
			{
				_root = _layout.Instantiate();

				// 템플릿 컨테이너는 크기가 없다 — 층 전체를 덮어야 안쪽의 .screen(absolute 0)이 화면을 채운다.
				_root.style.position = Position.Absolute;
				_root.style.left = 0;
				_root.style.right = 0;
				_root.style.top = 0;
				_root.style.bottom = 0;

				// 컨테이너 자체는 클릭을 받지 않는다 — 빈 영역은 아래 층으로 통과시킨다.
				_root.pickingMode = PickingMode.Ignore;

				OnBind(_root);
			}

			layerRoot.Add(_root);
		}

		public void Detach()
		{
			if (_root != null)
			{
				_root.RemoveFromHierarchy();
			}
		}

		// 팝업 등장 — 레이아웃의 .popup(딤 + 창)을 표시하고 한 프레임 뒤에 .popup--open 을 붙여 전환을 건다.
		// display 를 켠 같은 프레임에 붙이면 전환이 생략된다.
		protected void PlayPopupOpen()
		{
			if (_root == null)
			{
				return;
			}

			_popupFrame = _root.Q(className: "popup");
			if (_popupFrame == null)
			{
				return;
			}

			_popupFrame.style.display = DisplayStyle.Flex;
			_popupFrame.schedule.Execute(addPopupOpenClass);
		}

		private void addPopupOpenClass()
		{
			if (_popupFrame != null)
			{
				_popupFrame.AddToClassList("popup--open");
			}
		}

		// 요소 찾기(Q 는 여기서 한 번만) + 입력 연결 + Presenter 연결.
		protected abstract void OnBind(VisualElement root);

		protected virtual void OnDestroy()
		{
			Detach();
		}
	}
}
