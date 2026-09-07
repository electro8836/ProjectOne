using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectOne.UI
{
	// 모든 인터랙션 버튼의 추상 베이스.
	// 포인터 입력(Down/Up/Click/Hold)을 감지·발행만 하고, 연출/상태는 파생 클래스가 맡는다.
	// 단독으로 쓰이지 않으므로 abstract.
	public abstract class UIBaseInteractable : MonoBehaviour,
		IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IPointerExitHandler
	{
		[Header("Interaction")]
		[SerializeField] private bool _interactable = true;
		[SerializeField] private bool _holdRepeat = false;		// 누른 채 반복 호출 사용
		[SerializeField] private float _holdDelay = 0.5f;		// 첫 Hold까지 지연
		[SerializeField] private float _holdInterval = 0.1f;	// Hold 반복 간격

		public event Action OnPointerDownEvent;
		public event Action OnPointerUpEvent;
		public event Action OnClickEvent;
		public event Action OnHoldEvent;

		public virtual bool interactable { get => _interactable; set => _interactable = value; }

		private bool _isPressed;
		private Coroutine _holdRoutine;

		public void OnPointerDown(PointerEventData eventData)
		{
			if (!_interactable) { return; }

			_isPressed = true;
			invokeDown();
			startHold();
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			releasePress();
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (!_interactable) { return; }

			invokeClick();
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			// 누른 채 영역을 벗어나면 뗌으로 취급 (연출 원복 보장)
			releasePress();
		}

		// 눌린 상태에서만 Up을 1회 발행하고 홀드를 멈춘다.
		private void releasePress()
		{
			if (!_isPressed) { return; }

			_isPressed = false;
			stopHold();
			invokeUp();
		}

		private void startHold()
		{
			if (!_holdRepeat) { return; }

			_holdRoutine = StartCoroutine(holdRoutine());
		}

		private void stopHold()
		{
			if (_holdRoutine == null) { return; }

			StopCoroutine(_holdRoutine);
			_holdRoutine = null;
		}

		private IEnumerator holdRoutine()
		{
			yield return new WaitForSeconds(_holdDelay);

			WaitForSeconds wait = new WaitForSeconds(_holdInterval);
			while (true)
			{
				invokeHold();
				yield return wait;
			}
		}

		protected void invokeDown()
		{
			if (OnPointerDownEvent != null) { OnPointerDownEvent.Invoke(); }
		}

		protected void invokeUp()
		{
			if (OnPointerUpEvent != null) { OnPointerUpEvent.Invoke(); }
		}

		protected void invokeClick()
		{
			if (OnClickEvent != null) { OnClickEvent.Invoke(); }
		}

		protected void invokeHold()
		{
			if (OnHoldEvent != null) { OnHoldEvent.Invoke(); }
		}
	}
}
