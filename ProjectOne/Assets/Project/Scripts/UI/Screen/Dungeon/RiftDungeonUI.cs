using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 균열 던전의 진행 표시.
	//
	// GoldDungeonUI 와 같은 자리·같은 규칙으로 산다 — MainHUD 에 상주하지 않고 UIManager.EnsureDungeonHudAsync
	// 가 던전 진입 시 만들고 종료 시 걷는다. 생성이 startStage 보다 앞이어야 첫 배너를 놓치지 않는다.
	//
	// 구성이 둘로 나뉘며 절대 같이 떠 있지 않는다.
	//  - WaveTitle    : 페이즈 시작 배너. 즉시 뜨고 유지 후 페이드아웃한다.
	//  - WaveProgress : 상시 진행 정보. 배너가 사라진 뒤 켜진다.
	//
	// **루트는 끄지 않고 자식 둘을 SetActive 한다.** 루트를 끄면 Update 가 멈춰서 보스 배너가 물러나도
	// 되살아나지 못한다.
	//
	// **진행 카운트가 골드 던전과 다르다.** 균열 던전은 몬스터를 계속 채워 넣으므로 살아있는 수를 빼는
	// 방식으로는 처치 수가 나오지 않는다 — MonsterKillEvent 를 직접 세고 페이즈 시작마다 0으로 되돌린다.
	public class RiftDungeonUI : MonoBehaviour
	{
		[Header("페이즈 시작 배너")]
		[SerializeField] private CanvasGroup _waveTitleGroup;
		[SerializeField] private TMP_Text _waveTitleText;

		[Header("진행 정보")]
		[SerializeField] private GameObject _waveProgress;
		[SerializeField] private TMP_Text _titleText;
		[SerializeField] private TMP_Text _waveText;
		[SerializeField] private TMP_Text _progressText;
		[SerializeField] private Slider _progressSlider;
		[SerializeField] private TMP_Text _remainTimeText;

		[Header("연출")]
		[SerializeField] private float _titleHoldSeconds = 2f;
		[SerializeField] private float _titleFadeSeconds = 0.5f;

		// 자리 양보 대상인 보스 배너. MainHUD 안에 살아 다른 프리팹이므로 인스펙터로 이을 수 없다 —
		// 소유자인 UIManager 에게 물어 Awake 에서 한 번만 잡는다.
		private BossUI _bossUI;

		private Action<WaveStartedEvent> _onPhaseStarted;
		private Action<DungeonStageClearedEvent> _onStageCleared;
		private Action<MonsterKillEvent> _onMonsterKill;
		private Coroutine _titleRoutine;

		// 논리적 표시 상태 — 실제 활성 여부는 여기에 보스 우선을 곱한 결과다(applyVisibility).
		private bool _titleShown;
		private bool _progressShown;

		// 마지막으로 반영한 보스 배너 상태 — 바뀐 프레임에만 SetActive 를 건드린다.
		private bool _lastBossShowing;

		// 이번 페이즈에서 처치해야 하는 총 마리 수. 0 이면 아직 페이즈가 시작되지 않았다.
		private int _requiredKills;

		// 이번 페이즈의 처치 수 — 모드와 별개로 여기서 직접 센다.
		private int _killed;

		// 마지막으로 그린 값 — 매 프레임 문자열을 새로 만들지 않기 위해 바뀐 프레임에만 갱신한다.
		private int _lastKilled = -1;
		private int _lastRemainSecond = -1;

		private void Awake()
		{
			if (UIManager.HasInstance == true)
			{
				_bossUI = UIManager.Instance.BossUI;
			}

			_onPhaseStarted = onPhaseStarted;
			_onStageCleared = onStageCleared;
			_onMonsterKill = onMonsterKill;
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onPhaseStarted);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(_onStageCleared);
			EventManager.Instance.Subscribe<MonsterKillEvent>(_onMonsterKill);

			hideAll();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onPhaseStarted);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(_onStageCleared);
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(_onMonsterKill);
		}

		private void Update()
		{
			if (_requiredKills <= 0)
			{
				return;
			}

			// 보스 배너와 자리가 겹친다 — 보스가 떠 있으면 비켜주고, 물러나면 되돌아온다.
			if (isBossShowing() != _lastBossShowing)
			{
				applyVisibility();
			}

			refreshProgress();
			refreshRemainTime();
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void refreshProgress()
		{
			if (_killed == _lastKilled)
			{
				return;
			}

			_lastKilled = _killed;

			if (_progressText != null)
			{
				_progressText.text = _killed.ToString() + "/" + _requiredKills.ToString();
			}

			if (_progressSlider != null)
			{
				_progressSlider.value = Mathf.Clamp01((float)_killed / _requiredKills);
			}
		}

		// 남은 시간의 소유자는 DungeonDirector 다 — 여기서 따로 세지 않는다.
		private void refreshRemainTime()
		{
			if (_remainTimeText == null || DungeonDirector.HasInstance == false)
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
			_remainTimeText.text = (second / 60).ToString("00") + ":" + (second % 60).ToString("00");
		}

		// ── 페이즈 전환 ───────────────────────────────────────────────

		private void onMonsterKill(MonsterKillEvent evt)
		{
			if (_requiredKills <= 0)
			{
				return;
			}

			_killed++;
		}

		// 페이즈 시작은 모드가 웨이브 알림으로 보낸다 — 균열 던전에서는 웨이브 번호가 곧 페이즈 번호다.
		private void onPhaseStarted(WaveStartedEvent evt)
		{
			_requiredKills = evt.RequiredKills;
			_killed = 0;
			_lastKilled = -1;
			_lastRemainSecond = -1;

			applyDungeonTitle();

			if (_waveText != null)
			{
				_waveText.text = evt.CurrentWave.ToString();
			}

			// 배너가 걷히면 진행 정보로 넘어간다.
			showTitle("균열 " + evt.CurrentWave.ToString() + "/" + evt.TotalWaves.ToString(), true);
		}

		// 던전 클리어 — 진행 정보를 걷고 배너만 남긴다.
		// 결과창은 DungeonDirector 가 이 배너 시간만큼 기다렸다가 연다.
		private void onStageCleared(DungeonStageClearedEvent evt)
		{
			_requiredKills = 0;

			showTitle("던전 클리어!", false);
		}

		// 배너를 즉시 띄우고 유지·페이드 후 진행 정보로 넘길지 정한다.
		private void showTitle(string text, bool showProgressAfter)
		{
			if (_waveTitleText != null)
			{
				_waveTitleText.text = text;
			}

			// 배너가 떠 있는 동안 진행 정보는 숨는다.
			_titleShown = true;
			_progressShown = false;
			applyVisibility();

			stopTitleRoutine();
			_waveTitleGroup.alpha = 1f;

			// 코루틴은 루트에서 돈다 — 배너가 보스 우선으로 꺼져도 끊기지 않는다.
			_titleRoutine = StartCoroutine(holdThenFadeTitle(showProgressAfter));
		}

		// 던전 이름은 판 내내 같지만, 진입 시점에는 Director 가 아직 없을 수 있어 페이즈 시작마다 확인한다.
		private void applyDungeonTitle()
		{
			if (_titleText == null || DungeonDirector.HasInstance == false)
			{
				return;
			}

			Table_Dungeon.Row row = Table_Dungeon.Get(DungeonDirector.Instance.DungeonType);
			if (row != null)
			{
				_titleText.text = row.Name;
			}
		}

		private IEnumerator holdThenFadeTitle(bool showProgressAfter)
		{
			yield return new WaitForSeconds(_titleHoldSeconds);

			float elapsed = 0f;
			while (elapsed < _titleFadeSeconds)
			{
				elapsed += Time.deltaTime;
				_waveTitleGroup.alpha = Mathf.Clamp01(1f - elapsed / _titleFadeSeconds);
				yield return null;
			}

			_titleShown = false;
			_progressShown = showProgressAfter;
			applyVisibility();
			_titleRoutine = null;
		}

		private void stopTitleRoutine()
		{
			if (_titleRoutine != null)
			{
				StopCoroutine(_titleRoutine);
				_titleRoutine = null;
			}
		}

		private bool isBossShowing()
		{
			return (_bossUI != null && _bossUI.IsShowing == true);
		}

		// 의도한 표시 상태에 보스 우선을 곱해 실제 활성 여부를 정한다.
		private void applyVisibility()
		{
			bool boss = isBossShowing();
			_lastBossShowing = boss;

			_waveTitleGroup.gameObject.SetActive(_titleShown == true && boss == false);

			if (_waveProgress != null)
			{
				_waveProgress.SetActive(_progressShown == true && boss == false);
			}
		}

		private void hideAll()
		{
			stopTitleRoutine();

			_requiredKills = 0;
			_killed = 0;
			_lastKilled = -1;
			_lastRemainSecond = -1;

			_titleShown = false;
			_progressShown = false;
			applyVisibility();
		}
	}
}
