using System;
using System.Collections;
using UnityEngine;
using TMPro;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 던전 공용 메시지(UIPrefab_DungeonMessage) — 클리어·실패·진행도·경고를 화면 중앙에 띄운다.
	//
	// 던전 HUD 4종이 각자 들고 있던 배너(웨이브·안내·클리어)를 여기로 모았다. 던전 HUD 와 같은 수명이다 —
	// UIManager.EnsureDungeonHudAsync 가 HUD 와 함께 만들고 ReleaseDungeonHud 가 함께 걷는다.
	//
	//  - Progress / Warning : 즉시 뜨고 유지 후 페이드아웃한다(웨이브 시작·단계 안내).
	//  - Clear / Failed     : 결과창이 뜰 때까지 유지한다 — 디렉터가 서버 응답을 기다리는 동안 떠 있다.
	//
	// 패널은 하나만 보인다 — 새 메시지가 오면 이전 패널을 바로 걷는다.
	public class DungeonMessage : MonoBehaviour
	{
		// 미궁·유적 단계 시작 안내 — HUD 안내 배너에 있던 문구다.
		private const string LabyrinthNotice = "미궁이 아래에서 부터 무너지고 있습니다.\n위쪽에 있는 출구로 탈출 하세요.";
		private const string RuinsNotice = "유적의 방어 시스템을 무력화 하세요.";

		[Header("패널")]
		[SerializeField] private GameObject _clear;			// Clear
		[SerializeField] private TMP_Text _clearText;		// Clear/Text
		[SerializeField] private GameObject _failed;		// Failed
		[SerializeField] private TMP_Text _failedText;		// Failed/Text
		[SerializeField] private GameObject _progress;		// Progress
		[SerializeField] private TMP_Text _progressText;	// Progress/Text
		[SerializeField] private GameObject _warning;		// Warning
		[SerializeField] private TMP_Text _warningText;		// Warning/Text

		[Header("연출")]
		[SerializeField] private float _holdSeconds = 2f;
		[SerializeField] private float _fadeSeconds = 0.5f;

		private CanvasGroup _progressGroup;
		private CanvasGroup _warningGroup;

		private Action<WaveStartedEvent> _onWaveStarted;
		private Action<DungeonStageStartedEvent> _onStageStarted;
		private Coroutine _fadeRoutine;

		private void Awake()
		{
			// 페이드는 패널의 CanvasGroup 알파로 한다 — 프리팹에 없으면 붙인다.
			_progressGroup = ensureCanvasGroup(_progress);
			_warningGroup = ensureCanvasGroup(_warning);

			_onWaveStarted = onWaveStarted;
			_onStageStarted = onStageStarted;
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);

			HideAll();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
		}

		// ── 표시 ──────────────────────────────────────────────────────

		public void ShowClear(string text)
		{
			showPersistent(_clear, _clearText, text);
		}

		public void ShowFailed(string text)
		{
			showPersistent(_failed, _failedText, text);
		}

		public void ShowProgress(string text)
		{
			showTimed(_progress, _progressGroup, _progressText, text);
		}

		public void ShowWarning(string text)
		{
			showTimed(_warning, _warningGroup, _warningText, text);
		}

		public void HideAll()
		{
			stopFade();
			setActive(_clear, false);
			setActive(_failed, false);
			setActive(_progress, false);
			setActive(_warning, false);
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 골드는 전체 웨이브 수가 있고, 균열은 끝이 없어 0 으로 온다.
		private void onWaveStarted(WaveStartedEvent evt)
		{
			string text = (evt.TotalWaves > 0)
				? "웨이브 " + evt.CurrentWave + " / " + evt.TotalWaves
				: "웨이브 " + evt.CurrentWave;
			ShowProgress(text);
		}

		private void onStageStarted(DungeonStageStartedEvent evt)
		{
			if (evt.DungeonType == EDT.Dungeon.Labyrinth)
			{
				ShowWarning(LabyrinthNotice);
			}
			else if (evt.DungeonType == EDT.Dungeon.Ruins)
			{
				ShowWarning(RuinsNotice);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void showPersistent(GameObject panel, TMP_Text label, string text)
		{
			HideAll();
			setText(label, text);
			setActive(panel, true);
		}

		private void showTimed(GameObject panel, CanvasGroup group, TMP_Text label, string text)
		{
			HideAll();
			setText(label, text);
			setActive(panel, true);

			if (group == null)
			{
				return;
			}

			group.alpha = 1f;
			_fadeRoutine = StartCoroutine(holdThenFade(panel, group));
		}

		private IEnumerator holdThenFade(GameObject panel, CanvasGroup group)
		{
			yield return new WaitForSeconds(_holdSeconds);

			float elapsed = 0f;
			while (elapsed < _fadeSeconds)
			{
				elapsed += Time.deltaTime;
				group.alpha = Mathf.Clamp01(1f - elapsed / _fadeSeconds);
				yield return null;
			}

			panel.SetActive(false);
			_fadeRoutine = null;
		}

		private void stopFade()
		{
			if (_fadeRoutine != null)
			{
				StopCoroutine(_fadeRoutine);
				_fadeRoutine = null;
			}
		}

		private static CanvasGroup ensureCanvasGroup(GameObject panel)
		{
			if (panel == null)
			{
				return null;
			}

			CanvasGroup group = panel.GetComponent<CanvasGroup>();
			if (group == null)
			{
				group = panel.AddComponent<CanvasGroup>();
			}

			return group;
		}

		private static void setActive(GameObject panel, bool active)
		{
			if (panel != null)
			{
				panel.SetActive(active);
			}
		}

		private static void setText(TMP_Text label, string text)
		{
			if (label != null)
			{
				label.text = text;
			}
		}
	}
}
