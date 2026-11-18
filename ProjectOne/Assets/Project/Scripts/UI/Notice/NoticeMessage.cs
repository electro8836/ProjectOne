using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 잠깐 떴다가 사라지는 한 줄 알림 (UI Toolkit). 경고(UIPrefab_AlertMessage)·시스템(UIPrefab_SystemMessage)이 같이 쓴다.
	// 레이아웃은 UIToolkit/HUD/Notice/NoticeMessage.uxml. 위치는 _positionClass(USS), 유지 시간은 _holdSeconds 로 프리펩마다 다르다.
	// 여러 개가 오면 차례로 뜬다 — 하나가 사라진 뒤 다음이 뜬다.
	public class NoticeMessage : ToolkitScreen
	{
		[SerializeField] private string _positionClass = "notice--alert";	// 화면 위치를 정하는 USS 클래스
		[SerializeField] private float _holdSeconds = 3f;
		[SerializeField] private float _fadeSeconds = 0.5f;

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
			_notice.schedule.Execute(fadeOut).StartingIn((long)(_holdSeconds * 1000f));
		}

		// 서서히 사라진 뒤 다음 것을 띄운다.
		private void fadeOut()
		{
			_notice.RemoveFromClassList("notice--shown");
			_notice.schedule.Execute(playNext).StartingIn((long)(_fadeSeconds * 1000f));
		}
	}
}
