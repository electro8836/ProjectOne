using System;
using UnityEngine;
using TMPro;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 유적던전의 진행 표시. 미궁 HUD 와 같이 던전 입장 시 UIManager.EnsureDungeonHudAsync 가 만들고 종료 시 파괴한다.
	//
	// 구성 (입장 안내는 DungeonMessage 가 띄운다)
	//  - Stage / Time : 단계("n단계"), 남은 시간
	public class RuinsDungeonUI : MonoBehaviour
	{
		[Header("상태")]
		[SerializeField] private TMP_Text _stageText;			// State/Stage/Text
		[SerializeField] private TMP_Text _timeText;			// State/Time/Text

		private Action<DungeonStageStartedEvent> _onStageStarted;

		// 마지막으로 그린 남은 초 — 바뀐 프레임에만 문자열을 만든다.
		private int _lastRemainSecond = -1;

		private void Awake()
		{
			_onStageStarted = onStageStarted;
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
		}

		private void Update()
		{
			refreshRemainTime();
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 재도전·다음 단계도 같은 HUD 로 다시 시작한다 — 표시를 처음 상태로 돌린다.
		private void onStageStarted(DungeonStageStartedEvent evt)
		{
			if (evt.DungeonType != EDT.Dungeon.Ruins)
			{
				return;
			}

			if (_stageText != null)
			{
				_stageText.text = evt.Stage + "단계";
			}

			_lastRemainSecond = -1;
		}

		// ── 표시 ──────────────────────────────────────────────────────

		// 남은 시간의 소유자는 DungeonDirector 다 — 여기서 따로 세지 않는다.
		private void refreshRemainTime()
		{
			if (_timeText == null || DungeonDirector.HasInstance == false)
			{
				return;
			}

			int second = Mathf.CeilToInt(DungeonDirector.Instance.RemainTime);
			if (second < 0)
			{
				second = 0;
			}

			if (second == _lastRemainSecond)
			{
				return;
			}

			_lastRemainSecond = second;
			_timeText.text = (second / 60).ToString("00") + ":" + (second % 60).ToString("00");
		}
	}
}
