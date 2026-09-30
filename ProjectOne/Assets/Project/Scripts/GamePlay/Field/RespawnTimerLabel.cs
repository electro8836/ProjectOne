using TMPro;
using UnityEngine;

namespace ProjectOne.Field
{
	// 리스폰 남은 시간 월드 라벨 — "n시간 n분 n초". 앞자리가 0이면 생략한다 (DailyReset.FormatDuration 과 같은 표기).
	//
	// 무엇이 리스폰되는지는 모른다. 위치와 남은 초만 받으므로 필드보스 외 리스폰 콘텐츠도 그대로 쓴다.
	// 제목(몬스터 이름 등)을 주면 시간 위 줄에 붙이고, 없으면 시간만 출력한다 (상자류 — 활성/비활성은 오브젝트가 직접 표시).
	// 생성·회수는 RespawnTimerLabelManager 가 한다.
	[RequireComponent(typeof(TextMeshPro))]
	public class RespawnTimerLabel : MonoBehaviour
	{
		private const string FormatHour = "{0}시간 {1}분 {2}초";
		private const string FormatMinute = "{0}분 {1}초";
		private const string FormatSecond = "{0}초";

		private TextMeshPro _text;

		// 이번 표시에 쓸 포맷 — 제목이 있으면 Show 에서 한 번만 합쳐 둔다 (매 초 갱신은 GC 없음).
		private string _formatHour = FormatHour;
		private string _formatMinute = FormatMinute;
		private string _formatSecond = FormatSecond;

		// 0에 도달하는 시각 (Time.time 기준)
		private float _endTime;

		// 마지막으로 그린 남은 초 — 초가 바뀔 때만 텍스트를 다시 만든다.
		private int _lastSeconds = -1;

		private void Awake()
		{
			_text = GetComponent<TextMeshPro>();
		}

		// title 이 비어 있으면 시간만 출력한다.
		public void Show(Vector3 position, float remainingSeconds, string title)
		{
			if (string.IsNullOrEmpty(title) == true)
			{
				_formatHour = FormatHour;
				_formatMinute = FormatMinute;
				_formatSecond = FormatSecond;
			}
			else
			{
				_formatHour = title + "\n" + FormatHour;
				_formatMinute = title + "\n" + FormatMinute;
				_formatSecond = title + "\n" + FormatSecond;
			}

			transform.position = position;
			_endTime = Time.time + remainingSeconds;
			_lastSeconds = -1;
			gameObject.SetActive(true);
			refresh();
		}

		private void Update()
		{
			refresh();
		}

		// 0에 도달해도 표시는 유지한다 — 실제 리스폰 시점에 호출자가 회수한다.
		private void refresh()
		{
			int seconds = Mathf.Max(0, Mathf.CeilToInt(_endTime - Time.time));
			if (seconds == _lastSeconds)
			{
				return;
			}

			_lastSeconds = seconds;

			int hour = seconds / 3600;
			int minute = (seconds % 3600) / 60;
			int second = seconds % 60;

			// 포맷 인자 방식은 문자열을 새로 만들지 않는다 (GC 없음).
			if (hour > 0)
			{
				_text.SetText(_formatHour, hour, minute, second);
			}
			else if (minute > 0)
			{
				_text.SetText(_formatMinute, minute, second);
			}
			else
			{
				_text.SetText(_formatSecond, second);
			}
		}
	}
}
