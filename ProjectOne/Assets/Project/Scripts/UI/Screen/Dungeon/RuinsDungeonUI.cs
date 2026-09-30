using System;
using System.Collections;
using UnityEngine;
using TMPro;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 유적던전의 진행 표시. 미궁 HUD 와 같이 던전 입장 시 UIManager.EnsureDungeonHudAsync 가 만들고 종료 시 파괴한다.
	//
	// 구성
	//  - NoticeTitle : 입장 안내. 즉시 뜨고 유지 후 페이드아웃한다.
	//  - Stage / Time : 단계("n단계"), 남은 시간
	//  - Chest : 남은 상자 / 이 단계의 상자 수 (3개 중 1개 열면 2/3)
	//  - Key   : 이번 판에 더 쓸 수 있는 보물 열쇠 수
	public class RuinsDungeonUI : MonoBehaviour
	{
		[Header("안내 배너")]
		[SerializeField] private CanvasGroup _noticeGroup;		// NoticeTitle
		[SerializeField] private float _noticeHoldSeconds = 3f;
		[SerializeField] private float _noticeFadeSeconds = 0.5f;

		[Header("상태")]
		[SerializeField] private TMP_Text _stageText;			// State/Stage/Text
		[SerializeField] private TMP_Text _timeText;			// State/Time/Text
		[SerializeField] private TMP_Text _chestText;			// State/Chest/Text
		[SerializeField] private TMP_Text _keyText;				// State/Key/Text

		private Action<DungeonStageStartedEvent> _onStageStarted;
		private Action<RuinsChestChangedEvent> _onChestChanged;
		private Coroutine _noticeRoutine;

		// 마지막으로 그린 남은 초 — 바뀐 프레임에만 문자열을 만든다.
		private int _lastRemainSecond = -1;

		private void Awake()
		{
			_onStageStarted = onStageStarted;
			_onChestChanged = onChestChanged;
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);
			EventManager.Instance.Subscribe<RuinsChestChangedEvent>(_onChestChanged);

			if (_noticeGroup != null)
			{
				_noticeGroup.gameObject.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
			EventManager.Instance.Unsubscribe<RuinsChestChangedEvent>(_onChestChanged);
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

			showNotice();
		}

		private void onChestChanged(RuinsChestChangedEvent evt)
		{
			if (_chestText != null)
			{
				int remaining = evt.Total - evt.Opened;
				_chestText.text = ((remaining > 0) ? remaining : 0) + "/" + evt.Total;
			}

			if (_keyText != null)
			{
				_keyText.text = evt.KeyRemaining.ToString();
			}
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

		// ── 안내 배너 ─────────────────────────────────────────────────

		private void showNotice()
		{
			if (_noticeGroup == null)
			{
				return;
			}

			if (_noticeRoutine != null)
			{
				StopCoroutine(_noticeRoutine);
				_noticeRoutine = null;
			}

			_noticeGroup.alpha = 1f;
			_noticeGroup.gameObject.SetActive(true);
			_noticeRoutine = StartCoroutine(holdThenFadeNotice());
		}

		private IEnumerator holdThenFadeNotice()
		{
			yield return new WaitForSeconds(_noticeHoldSeconds);

			float elapsed = 0f;
			while (elapsed < _noticeFadeSeconds)
			{
				elapsed += Time.deltaTime;
				_noticeGroup.alpha = Mathf.Clamp01(1f - elapsed / _noticeFadeSeconds);
				yield return null;
			}

			_noticeGroup.gameObject.SetActive(false);
			_noticeRoutine = null;
		}
	}
}
