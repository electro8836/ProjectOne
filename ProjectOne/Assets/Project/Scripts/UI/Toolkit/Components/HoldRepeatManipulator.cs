using System;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 누른 채로 있으면 반복 호출한다 (UGUI UIBaseInteractable 의 _holdRepeat 와 같은 규칙).
	// 첫 반복까지 0.5초, 이후 0.1초마다. 반복이 한 번이라도 나갔으면 뗄 때의 클릭은 건너뛰어야 한다 — HoldFired 로 확인한다.
	public sealed class HoldRepeatManipulator : Manipulator
	{
		private const long HoldDelayMs = 500;
		private const long HoldIntervalMs = 100;

		private readonly Action _onHold;
		private IVisualElementScheduledItem _repeat;
		private bool _holdFired;

		// 이번 누름에서 반복이 나갔는가 — 클릭 핸들러가 이 값이 true 면 무시한다.
		public bool HoldFired
		{
			get { return _holdFired; }
		}

		public HoldRepeatManipulator(Action onHold)
		{
			_onHold = onHold;
		}

		protected override void RegisterCallbacksOnTarget()
		{
			// Button 의 Clickable 이 포인터를 잡기 전에 받도록 TrickleDown 으로 건다.
			target.RegisterCallback<PointerDownEvent>(onPointerDown, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerUpEvent>(onPointerUp, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerCancelEvent>(onPointerCancel);
			target.RegisterCallback<PointerCaptureOutEvent>(onCaptureOut);
		}

		protected override void UnregisterCallbacksFromTarget()
		{
			target.UnregisterCallback<PointerDownEvent>(onPointerDown, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerUpEvent>(onPointerUp, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerCancelEvent>(onPointerCancel);
			target.UnregisterCallback<PointerCaptureOutEvent>(onCaptureOut);
			stop();
		}

		private void onPointerDown(PointerDownEvent evt)
		{
			_holdFired = false;
			stop();

			if (target.enabledInHierarchy == false)
			{
				return;
			}

			_repeat = target.schedule.Execute(fire).StartingIn(HoldDelayMs).Every(HoldIntervalMs);
		}

		private void onPointerUp(PointerUpEvent evt)
		{
			stop();
		}

		private void onPointerCancel(PointerCancelEvent evt)
		{
			stop();
		}

		private void onCaptureOut(PointerCaptureOutEvent evt)
		{
			stop();
		}

		private void fire()
		{
			// 반복 중 버튼이 잠기면(재화 부족 등) 멈춘다.
			if (target.enabledInHierarchy == false)
			{
				stop();
				return;
			}

			_holdFired = true;
			if (_onHold != null)
			{
				_onHold.Invoke();
			}
		}

		private void stop()
		{
			if (_repeat != null)
			{
				_repeat.Pause();
				_repeat = null;
			}
		}
	}
}
