using System;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Event;
using ProjectOne.Settings;
using ProjectOne.Unit;
using ProjectOne.Unit.Input;

namespace ProjectOne.UI
{
	// 플로팅 캐릭터 이동 조이스틱(UIPrefab_Joystick, UI Toolkit). 레이아웃은 UIToolkit/HUD/Joystick/Joystick.uxml.
	// UIManager 가 HUD 층의 맨 아래에 붙인다 — HUD 의 빈 곳으로 들어온 터치만 여기까지 온다.
	// - 입력 영역을 터치하면 배경을 터치 위치로 옮김(유동 모드). 고정 모드는 영역 가운데에 둔다
	// - 끌면 배경 중심부터 손잡이를 반지름 _radius 원 안에서 움직임
	// - 정규화한 방향(-1~1, 위가 +y)을 Hero 의 TouchInputProvider 로 전달, 부호에 따라 4분면 표시 토글
	// - 손을 떼면 배경을 기본 위치로 되돌리고 입력을 0으로
	public class JoystickController : ToolkitScreen
	{
		private const string FocusOnClass = "joystick__focus--on";
		private const int NoPointer = -1;

		[SerializeField] private float _radius = 100;

		private VisualElement _area;
		private VisualElement _base;
		private VisualElement _handle;
		private VisualElement _focusLeftUp;
		private VisualElement _focusLeftDown;
		private VisualElement _focusRightUp;
		private VisualElement _focusRightDown;

		private Action<UnitSpawnedEvent> _onUnitSpawned;
		private Action<UnitDiedEvent> _onUnitDied;
		private TouchInputProvider _touchProvider;
		private Vector2 _output;
		private Vector2 _center;	// 배경 중심 — 입력 영역 기준 좌표(왼쪽 위 원점, 아래가 +y)
		private int _pointerId = NoPointer;
		private bool _useFloating;	// true: 누른 위치로 배경 이동, false: 고정

		// 히어로 스폰보다 먼저 구독해야 한다 — 레이아웃이 붙기 전(Awake)에 건다.
		private void Awake()
		{
			_useFloating = SettingsManager.Instance.UseFloatingJoystick;
			SettingsManager.Instance.FloatingJoystickChanged += onFloatingChanged;

			_onUnitSpawned = onUnitSpawned;
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);
		}

		protected override void OnBind(VisualElement root)
		{
			_area = root.Q("area");
			_base = root.Q("base");
			_handle = root.Q("handle");
			_focusLeftUp = root.Q("focus-left-up");
			_focusLeftDown = root.Q("focus-left-down");
			_focusRightUp = root.Q("focus-right-up");
			_focusRightDown = root.Q("focus-right-down");

			_area.RegisterCallback<PointerDownEvent>(onPointerDown);
			_area.RegisterCallback<PointerMoveEvent>(onPointerMove);
			_area.RegisterCallback<PointerUpEvent>(onPointerUp);
			_area.RegisterCallback<PointerCaptureOutEvent>(onPointerCaptureOut);

			resetToHome();
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(_onUnitDied);
			SettingsManager.Instance.FloatingJoystickChanged -= onFloatingChanged;
			base.OnDestroy();
		}

		private void Update()
		{
			if (_touchProvider != null)
			{
				_touchProvider.SetMoveInput(_output);
			}
		}

		private void onPointerDown(PointerDownEvent evt)
		{
			if (_pointerId != NoPointer)
			{
				return;
			}

			_pointerId = evt.pointerId;
			_area.CapturePointer(_pointerId);

			// 유동 모드만 배경 중앙을 터치 위치로 옮긴다. 고정 모드는 영역 가운데에 그대로 둔다.
			if (_useFloating == true)
			{
				_center = evt.localPosition;
				_base.style.left = _center.x;
				_base.style.top = _center.y;
			}
			else
			{
				_center = _area.contentRect.center;
			}

			setHandle(Vector2.zero);
		}

		private void onPointerMove(PointerMoveEvent evt)
		{
			if (evt.pointerId != _pointerId)
			{
				return;
			}

			// 배경 중심으로부터의 offset → 반지름 클램프
			Vector2 offset = (Vector2)evt.localPosition - _center;
			setHandle(Vector2.ClampMagnitude(offset, _radius));
		}

		private void onPointerUp(PointerUpEvent evt)
		{
			if (evt.pointerId != _pointerId)
			{
				return;
			}

			_area.ReleasePointer(_pointerId);
		}

		// 손을 뗐거나(ReleasePointer) 다른 곳이 포인터를 가져갔을 때 — 둘 다 여기서 정리한다.
		private void onPointerCaptureOut(PointerCaptureOutEvent evt)
		{
			_pointerId = NoPointer;
			resetToHome();
		}

		// 손잡이 위치(입력 영역 좌표, 아래가 +y)와 출력(위가 +y)을 함께 맞춘다.
		private void setHandle(Vector2 clamped)
		{
			_handle.style.translate = new Translate(clamped.x, clamped.y);
			_output = new Vector2(clamped.x, -clamped.y) / _radius;
			updateFocus(_output);
		}

		// 배경을 기본 위치(영역 가운데)로 되돌리고 손잡이·입력·표시를 초기화 (배경은 계속 표시)
		private void resetToHome()
		{
			if (IsBound == false)
			{
				_output = Vector2.zero;
				return;
			}

			_base.style.left = Length.Percent(50);
			_base.style.top = Length.Percent(50);
			setHandle(Vector2.zero);
		}

		// 옵션 변경 즉시 반영 — 모드 갱신 후 배경을 기본 위치로 스냅
		private void onFloatingChanged(bool enabled)
		{
			_useFloating = enabled;
			resetToHome();
		}

		// 출력 방향의 부호로 4분면 중 1개만 표시 (거의 0이면 전부 숨김)
		private void updateFocus(Vector2 dir)
		{
			bool active = dir.sqrMagnitude > 0.0001f;
			_focusLeftUp.EnableInClassList(FocusOnClass, active && dir.x < 0f && dir.y > 0f);
			_focusRightUp.EnableInClassList(FocusOnClass, active && dir.x > 0f && dir.y > 0f);
			_focusLeftDown.EnableInClassList(FocusOnClass, active && dir.x < 0f && dir.y < 0f);
			_focusRightDown.EnableInClassList(FocusOnClass, active && dir.x > 0f && dir.y < 0f);
		}

		private void onUnitSpawned(UnitSpawnedEvent evt)
		{
			if (evt.UnitType != UnitType.Hero)
			{
				return;
			}

			_touchProvider = evt.Unit.GetComponent<TouchInputProvider>();
			if (_touchProvider == null)
			{
				Debug.LogError("[JoystickController] Hero에 TouchInputProvider가 없습니다.");
			}
		}

		private void onUnitDied(UnitDiedEvent evt)
		{
			if (_touchProvider == null || evt.UnitType != UnitType.Hero)
			{
				return;
			}

			_touchProvider.SetMoveInput(Vector2.zero);
			_touchProvider = null;
		}
	}
}
