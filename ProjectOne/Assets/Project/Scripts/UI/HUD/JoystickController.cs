using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ProjectOne.Event;
using ProjectOne.Settings;
using ProjectOne.Unit;
using ProjectOne.Unit.Input;

namespace ProjectOne.UI
{
	// 플로팅 캐릭터 이동 조이스틱.
	// - 이 스크립트가 붙은 입력 영역을 터치하면 배경(_base)을 터치 위치로 옮기고 표시
	// - 드래그로 배경 중심부터 Handle을 반지름 _radius 원 안에서 움직임
	// - 정규화한 방향(-1~1)을 Hero의 TouchInputProvider로 전달, 부호에 따라 4분면 Focus 토글
	// - 손을 떼면 배경을 숨기고 입력을 0으로
	public class JoystickController : MonoBehaviour,
		IPointerDownHandler, IDragHandler, IPointerUpHandler
	{
		[Header("참조")]
		[SerializeField] private RectTransform _base;     // 터치 위치로 이동하는 조이스틱 배경(중앙 anchor/pivot)
		[SerializeField] private RectTransform _handle;
		[SerializeField] private float _radius = 150f;

		[Header("방향 포커스")]
		[SerializeField] private GameObject _focusLeftUp;
		[SerializeField] private GameObject _focusLeftDown;
		[SerializeField] private GameObject _focusRightUp;
		[SerializeField] private GameObject _focusRightDown;

		private Action<UnitSpawnedEvent> _onUnitSpawned;
		private Action<UnitDiedEvent> _onUnitDied;
		private TouchInputProvider _touchProvider;
		private Vector2 _output;
		private Vector2 _homePosition;     // 시작 시점의 배경 기본 위치 — 고정 모드 위치이자 유동성 모드 복귀 위치
		private bool _useFloating;         // true: 누른 위치로 배경 이동, false: 고정

		private void Awake()
		{
			// 다른 이동이 일어나기 전에 기본(홈) 위치를 캡처
			_homePosition = _base.anchoredPosition;
			_useFloating = SettingsManager.Instance.UseFloatingJoystick;
			SettingsManager.Instance.FloatingJoystickChanged += onFloatingChanged;

			_onUnitSpawned = onUnitSpawned;
			_onUnitDied = onUnitDied;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Subscribe<UnitDiedEvent>(_onUnitDied);

			_base.gameObject.SetActive(true);
			resetToHome();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(_onUnitDied);
			SettingsManager.Instance.FloatingJoystickChanged -= onFloatingChanged;
		}

		private void Update()
		{
			if (_touchProvider != null)
			{
				_touchProvider.SetMoveInput(_output);
			}
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			Vector2 local;
			if (tryGetLocal(eventData, out local) == false)
			{
				return;
			}

			// 유동성 모드만 배경 중앙을 터치 위치로 이동 (입력영역·배경 모두 중앙 anchor/pivot 전제)
			// 고정 모드는 배경을 홈 위치에 그대로 둔다
			if (_useFloating == true)
			{
				_base.anchoredPosition = local;
			}

			_handle.anchoredPosition = Vector2.zero;
			_output = Vector2.zero;
			updateFocus(Vector2.zero);
		}

		public void OnDrag(PointerEventData eventData)
		{
			Vector2 local;
			if (tryGetLocal(eventData, out local) == false)
			{
				return;
			}

			// 배경 중심으로부터의 offset → 반지름 클램프
			Vector2 offset = local - _base.anchoredPosition;
			Vector2 clamped = Vector2.ClampMagnitude(offset, _radius);
			_handle.anchoredPosition = clamped;
			_output = clamped / _radius;
			updateFocus(_output);
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			resetToHome();
		}

		// 입력 영역(이 스크립트가 붙은 rect) 기준 로컬 좌표 — area pivot 기준
		private bool tryGetLocal(PointerEventData eventData, out Vector2 local)
		{
			RectTransform area = (RectTransform)transform;
			return RectTransformUtility.ScreenPointToLocalPointInRectangle(area, eventData.position, eventData.pressEventCamera, out local);
		}

		// 배경을 홈 위치로 되돌리고 핸들·입력·포커스를 초기화 (배경은 계속 표시 유지)
		private void resetToHome()
		{
			_base.anchoredPosition = _homePosition;
			_handle.anchoredPosition = Vector2.zero;
			_output = Vector2.zero;
			updateFocus(Vector2.zero);
		}

		// 옵션 변경 즉시 반영 — 모드 갱신 후 배경을 홈으로 스냅
		private void onFloatingChanged(bool enabled)
		{
			_useFloating = enabled;
			resetToHome();
		}

		// 출력 방향의 부호로 4분면 중 1개만 활성 (거의 0이면 전부 비활성)
		private void updateFocus(Vector2 dir)
		{
			bool active = dir.sqrMagnitude > 0.0001f;
			setFocus(_focusLeftUp, active && dir.x < 0f && dir.y > 0f);
			setFocus(_focusRightUp, active && dir.x > 0f && dir.y > 0f);
			setFocus(_focusLeftDown, active && dir.x < 0f && dir.y < 0f);
			setFocus(_focusRightDown, active && dir.x > 0f && dir.y < 0f);
		}

		private static void setFocus(GameObject focus, bool on)
		{
			if (focus != null && focus.activeSelf != on)
			{
				focus.SetActive(on);
			}
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
