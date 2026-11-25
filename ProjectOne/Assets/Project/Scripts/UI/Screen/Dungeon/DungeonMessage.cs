using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 던전 공용 메시지(UIPrefab_DungeonMessage, UI Toolkit) — 클리어·실패·진행도·경고를 화면 위 가운데에 띄운다.
	// 레이아웃은 UIToolkit/HUD/Dungeon/DungeonMessage.uxml.
	//
	// 던전 HUD 4종이 각자 들고 있던 배너(웨이브·안내·클리어)를 여기로 모았다. 던전 HUD 와 같은 수명이다 —
	// UIManager.EnsureDungeonHudAsync 가 HUD 와 함께 만들고 ReleaseDungeonHud 가 함께 걷는다.
	//
	//  - Progress / Warning : 즉시 뜨고 유지 후 페이드아웃한다(웨이브 시작·단계 안내).
	//  - Clear / Failed     : 결과창이 뜰 때까지 유지한다 — 디렉터가 서버 응답을 기다리는 동안 떠 있다.
	//
	// 배너는 하나다 — 종류마다 아이콘·장식 색·글자 크기만 바꾸고, 새 메시지가 오면 이전 것을 바로 덮는다.
	public class DungeonMessage : ToolkitScreen
	{
		// 미궁·유적 단계 시작 안내 — HUD 안내 배너에 있던 문구다.
		private const string LabyrinthNotice = "미궁이 아래에서 부터 무너지고 있습니다.\n위쪽에 있는 출구로 탈출 하세요.";
		private const string RuinsNotice = "유적의 방어 시스템을 무력화 하세요.";

		private const string ShowClass = "dungeon-message--show";
		private const string BadClass = "dungeon-message--bad";
		private const string SmallClass = "dungeon-message--small";

		private const float HoldSeconds = 2f;
		private const float FadeSeconds = 0.5f;

		private VisualElement _banner;
		private VisualElement _icon;
		private Label _text;
		private string _iconClass;

		private Action<WaveStartedEvent> _onWaveStarted;
		private Action<DungeonStageStartedEvent> _onStageStarted;
		private Coroutine _fadeRoutine;

		protected override void OnBind(VisualElement root)
		{
			_banner = root.Q("banner");
			_icon = root.Q("icon");
			_text = root.Q<Label>("text");

			_onWaveStarted = onWaveStarted;
			_onStageStarted = onStageStarted;
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);

			HideAll();
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onWaveStarted);
				EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
			}

			base.OnDestroy();
		}

		// ── 표시 ──────────────────────────────────────────────────────

		public void ShowClear(string text)
		{
			show(text, "picto-star", false, false);
		}

		public void ShowFailed(string text)
		{
			show(text, "picto-kill", true, false);
		}

		public void ShowProgress(string text)
		{
			show(text, "picto-wave", false, false);
			_fadeRoutine = StartCoroutine(holdThenFade());
		}

		public void ShowWarning(string text)
		{
			show(text, "picto-warning", true, true);
			_fadeRoutine = StartCoroutine(holdThenFade());
		}

		public void HideAll()
		{
			stopFade();
			_banner.RemoveFromClassList(ShowClass);
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

		private void show(string text, string iconClass, bool bad, bool small)
		{
			HideAll();

			if (_iconClass != null)
			{
				_icon.RemoveFromClassList(_iconClass);
			}

			_iconClass = iconClass;
			_icon.AddToClassList(iconClass);
			_banner.EnableInClassList(BadClass, bad);
			_banner.EnableInClassList(SmallClass, small);
			_banner.style.opacity = 1f;
			_text.text = text;
			_banner.AddToClassList(ShowClass);
		}

		private IEnumerator holdThenFade()
		{
			yield return new WaitForSeconds(HoldSeconds);

			float elapsed = 0f;
			while (elapsed < FadeSeconds)
			{
				elapsed += Time.deltaTime;
				_banner.style.opacity = Mathf.Clamp01(1f - elapsed / FadeSeconds);
				yield return null;
			}

			_banner.RemoveFromClassList(ShowClass);
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
	}
}
