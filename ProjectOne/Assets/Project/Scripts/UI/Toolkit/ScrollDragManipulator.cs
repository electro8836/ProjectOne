using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// ScrollView 를 끌어서 스크롤한다 — 손가락처럼 마우스로도 밀어 올리고 내릴 수 있게 한다.
	//
	// 터치는 ScrollView 가 기본으로 끌기 스크롤을 처리하므로 여기서는 마우스만 맡는다(에디터·PC 테스트에서도 모바일과 같은 조작).
	// 일정 거리 이상 끌면 드래그로 보고, 손을 뗀 뒤 따라오는 클릭은 삼켜 목록 행이 눌리지 않게 한다.
	//
	// 내용이 보이는 영역보다 작으면 움직이지 않는다 — 마우스 끌기는 스크롤 범위 안으로만 옮기고,
	// 터치는 누를 때마다 Elastic(넘치면 튕김) / Clamped(못 움직임)를 갈아 끼운다 — 줄바꿈 격자는 칸이 늘어도
	// 크기 변경 이벤트가 오지 않아 누르는 순간에 판단한다.
	public sealed class ScrollDragManipulator : PointerManipulator
	{
		// 이 거리(패널 px)보다 덜 움직이면 탭으로 본다
		private const float DragThreshold = 12f;

		private readonly ScrollView _scrollView;

		private int _pointerId = PointerId.invalidPointerId;
		private Vector2 _startPosition;
		private Vector2 _startOffset;
		private bool _isDragging;
		private bool _suppressClick;

		public ScrollDragManipulator(ScrollView scrollView)
		{
			_scrollView = scrollView;
			target = scrollView;
		}

		protected override void RegisterCallbacksOnTarget()
		{
			// TrickleDown — 행(자식)보다 먼저 받아야 드래그 뒤 클릭을 막을 수 있다
			target.RegisterCallback<PointerDownEvent>(onPointerDown, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerMoveEvent>(onPointerMove, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerUpEvent>(onPointerUp, TrickleDown.TrickleDown);
			target.RegisterCallback<ClickEvent>(onClick, TrickleDown.TrickleDown);
			target.RegisterCallback<WheelEvent>(onWheel, TrickleDown.TrickleDown);
		}

		protected override void UnregisterCallbacksFromTarget()
		{
			target.UnregisterCallback<PointerDownEvent>(onPointerDown, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerMoveEvent>(onPointerMove, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerUpEvent>(onPointerUp, TrickleDown.TrickleDown);
			target.UnregisterCallback<ClickEvent>(onClick, TrickleDown.TrickleDown);
			target.UnregisterCallback<WheelEvent>(onWheel, TrickleDown.TrickleDown);
		}

		// 세로로 밀 수 있는 거리. 내용이 보이는 영역보다 작으면 0 이다.
		// 내용 컨테이너 높이는 줄바꿈 격자에서 실제 내용과 다를 수 있어 ScrollView 가 계산한 스크롤 범위를 쓴다.
		// 아직 배치 전(숨김 등)이면 범위가 초기값이라 0 으로 본다.
		private float maxOffset()
		{
			if (_scrollView.contentViewport.layout.height <= 0f)
			{
				return 0f;
			}

			Scroller scroller = _scrollView.verticalScroller;
			return Mathf.Max(0f, scroller.highValue - scroller.lowValue);
		}

		private void applyTouchBehavior()
		{
			ScrollView.TouchScrollBehavior behavior = (maxOffset() > 0f) ? ScrollView.TouchScrollBehavior.Elastic : ScrollView.TouchScrollBehavior.Clamped;
			if (_scrollView.touchScrollBehavior != behavior)
			{
				_scrollView.touchScrollBehavior = behavior;
			}
		}

		private void onPointerDown(PointerDownEvent evt)
		{
			applyTouchBehavior();

			if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse || evt.button != 0)
			{
				return;
			}

			// 밀 거리가 없으면 끌기를 시작하지 않는다 — 클릭은 그대로 행에 간다.
			if (maxOffset() <= 0f)
			{
				return;
			}

			_pointerId = evt.pointerId;
			_startPosition = evt.position;
			_startOffset = _scrollView.scrollOffset;
			_isDragging = false;
			_suppressClick = false;
		}

		private void onPointerMove(PointerMoveEvent evt)
		{
			if (evt.pointerId != _pointerId)
			{
				return;
			}

			Vector2 delta = (Vector2)evt.position - _startPosition;
			if (_isDragging == false)
			{
				if (Mathf.Abs(delta.y) < DragThreshold)
				{
					return;
				}

				_isDragging = true;
				target.CapturePointer(_pointerId);
			}

			float offsetY = Mathf.Clamp(_startOffset.y - delta.y, 0f, maxOffset());
			_scrollView.scrollOffset = new Vector2(_startOffset.x, offsetY);
			evt.StopPropagation();
		}

		private void onPointerUp(PointerUpEvent evt)
		{
			if (evt.pointerId != _pointerId)
			{
				return;
			}

			if (_isDragging == true)
			{
				_suppressClick = true;
				target.ReleasePointer(_pointerId);
				evt.StopPropagation();
			}

			_pointerId = PointerId.invalidPointerId;
			_isDragging = false;
		}

		// 마우스 휠 — 스크롤 범위 안으로만 옮긴다. ScrollView 기본 처리에 맡기면 터치용 Elastic 이 휠에도 걸려
		// 끝에서 범위 밖으로 밀렸다가 튕겨 돌아오며 목록이 떨린다.
		private void onWheel(WheelEvent evt)
		{
			float offsetY = Mathf.Clamp(_scrollView.scrollOffset.y + evt.delta.y * _scrollView.mouseWheelScrollSize, 0f, maxOffset());
			if (offsetY != _scrollView.scrollOffset.y)
			{
				_scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, offsetY);
			}

			evt.StopPropagation();
		}

		private void onClick(ClickEvent evt)
		{
			if (_suppressClick == false)
			{
				return;
			}

			_suppressClick = false;
			evt.StopImmediatePropagation();
		}
	}
}
