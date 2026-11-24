using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 잠깐 떴다가 사라지는 한 줄 알림 (UI Toolkit). 경고(UIPrefab_AlertMessage)·시스템(UIPrefab_SystemMessage)이 같이 쓴다.
	// 레이아웃은 UIToolkit/HUD/Notice/NoticeMessage.uxml. 위치는 _positionClass(USS), 유지 시간은 _holdSeconds 로 프리펩마다 다르다.
	// 글자는 최대 2줄까지 줄바꿈되고, 2줄이면 _extraLineSeconds 만큼 더 머문다.
	// 여러 개가 오면 차례로 뜬다 — 하나가 사라진 뒤 다음이 뜬다.
	public class NoticeMessage : ToolkitScreen
	{
		[SerializeField] private string _positionClass = "notice--alert";	// 화면 위치를 정하는 USS 클래스
		[SerializeField] private float _holdSeconds = 3f;	// 1줄일 때 유지 시간
		[SerializeField] private float _extraLineSeconds = 2f;	// 줄이 하나 늘 때마다 더 머무는 시간
		[SerializeField] private float _fadeSeconds = 0.5f;

		private const int MaxLines = 2;	// USS(.pill__text max-height)가 2줄에서 자른다

		private readonly Queue<string> _pending = new Queue<string>();
		private VisualElement _notice;
		private Label _text;
		private bool _playing;

		protected override void OnBind(VisualElement root)
		{
			_notice = root.Q("notice");
			_notice.AddToClassList(_positionClass);
			_notice.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(_fadeSeconds) });
			_text = root.Q<Label>("text");
		}

		public void Show(string message)
		{
			_pending.Enqueue(message);
			if (_playing == false)
			{
				playNext();
			}
		}

		private void playNext()
		{
			if (_pending.Count == 0)
			{
				_playing = false;
				return;
			}

			_playing = true;
			_text.text = _pending.Dequeue();
			_notice.AddToClassList("notice--shown");
			_notice.schedule.Execute(startHold);
		}

		// 새 글자의 레이아웃이 끝난 다음 프레임에 줄 수를 재서 유지 시간을 정한다.
		private void startHold()
		{
			float lineHeight = _text.MeasureTextSize("가", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).y;
			int lines = 1;
			if (lineHeight > 0f)
			{
				lines = Mathf.Clamp(Mathf.RoundToInt(_text.contentRect.height / lineHeight), 1, MaxLines);
			}

			float hold = _holdSeconds + (lines - 1) * _extraLineSeconds;
			_notice.schedule.Execute(fadeOut).StartingIn((long)(hold * 1000f));
		}

		// 서서히 사라진 뒤 다음 것을 띄운다.
		private void fadeOut()
		{
			_notice.RemoveFromClassList("notice--shown");
			_notice.schedule.Execute(playNext).StartingIn((long)(_fadeSeconds * 1000f));
		}
	}
}
