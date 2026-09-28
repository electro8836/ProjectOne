using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 균열 던전 HUD — 웨이브·남은시간·라이프·처치 수 상태줄, 웨이브 배너, 균열 스킬 버튼.
	//
	// GoldDungeonUI 와 같은 자리·같은 규칙으로 산다 — MainHUD 에 상주하지 않고 UIManager.EnsureDungeonHudAsync
	// 가 던전 진입 시 만들고 종료 시 걷는다. 생성이 startStage 보다 앞이어야 첫 배너와 시작 알림을 놓치지 않는다.
	//
	// 값의 소유자는 RiftDungeonMode(라이프·게이지)와 DungeonDirector(남은시간)다. 여기서는 이벤트를 받아 그리기만 하고,
	// 스킬 버튼도 사용 요청만 발행한다 — 사용 가능 판정은 모드가 한다.
	public class RiftDungeonUI : MonoBehaviour
	{
		[Header("상태")]
		[SerializeField] private TMP_Text _waveText;		// State/Wave/Text
		[SerializeField] private TMP_Text _remainTimeText;	// State/Time/Text
		[SerializeField] private TMP_Text _lifeText;		// State/Life/Text
		[SerializeField] private TMP_Text _killText;		// State/MonsterKill/Text

		[Header("웨이브 배너")]
		[SerializeField] private CanvasGroup _waveTitleGroup;	// WaveTitle
		[SerializeField] private TMP_Text _waveTitleText;		// WaveTitle/WaveText

		[Header("균열 스킬")]
		[SerializeField] private UIButton _skillButton;		// SkillButton
		[SerializeField] private Image _skillIcon;			// SkillButton/Icon
		[SerializeField] private Image _gaugeFill;			// SkillButton/GaugeSlider (Filled)
		[SerializeField] private TMP_Text _gaugeText;		// SkillButton/Gauge/Text
		[SerializeField] private TMP_Text _stackText;		// SkillButton/Stack/StackText

		[Header("연출")]
		[SerializeField] private float _titleHoldSeconds = 2f;
		[SerializeField] private float _titleFadeSeconds = 0.5f;

		private Action<WaveStartedEvent> _onWaveStarted;
		private Action<DungeonStageClearedEvent> _onStageCleared;
		private Action<MonsterKillEvent> _onMonsterKill;
		private Action<RiftLifeChangedEvent> _onLifeChanged;
		private Action<RiftGaugeChangedEvent> _onGaugeChanged;
		private Coroutine _titleRoutine;

		private int _killed;
		private int _lastRemainSecond = -1;

		// 마지막으로 그린 균열 스킬 — 게이지 알림마다 아이콘을 다시 걸지 않는다.
		private int _iconSkillId;
		private SpriteBinder _skillIconBinder;

		private void Awake()
		{
			_skillIconBinder = new SpriteBinder(_skillIcon);

			_onWaveStarted = onWaveStarted;
			_onStageCleared = onStageCleared;
			_onMonsterKill = onMonsterKill;
			_onLifeChanged = onLifeChanged;
			_onGaugeChanged = onGaugeChanged;
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(_onStageCleared);
			EventManager.Instance.Subscribe<MonsterKillEvent>(_onMonsterKill);
			EventManager.Instance.Subscribe<RiftLifeChangedEvent>(_onLifeChanged);
			EventManager.Instance.Subscribe<RiftGaugeChangedEvent>(_onGaugeChanged);

			_skillButton.OnClickEvent += onSkillClicked;

			_waveTitleGroup.gameObject.SetActive(false);
			renderKill();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(_onStageCleared);
			EventManager.Instance.Unsubscribe<MonsterKillEvent>(_onMonsterKill);
			EventManager.Instance.Unsubscribe<RiftLifeChangedEvent>(_onLifeChanged);
			EventManager.Instance.Unsubscribe<RiftGaugeChangedEvent>(_onGaugeChanged);

			_skillButton.OnClickEvent -= onSkillClicked;

			_skillIconBinder.Release();
		}

		private void Update()
		{
			refreshRemainTime();
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

		// ── 이벤트 ────────────────────────────────────────────────────

		private void onWaveStarted(WaveStartedEvent evt)
		{
			_waveText.text = evt.CurrentWave.ToString();
			showTitle("웨이브 " + evt.CurrentWave.ToString());
		}

		// 한 판 종료(라이프 소진·제한시간). 결과창은 DungeonDirector 가 이 배너 시간만큼 기다렸다가 연다.
		private void onStageCleared(DungeonStageClearedEvent evt)
		{
			showTitle("균열 종료");
		}

		private void onMonsterKill(MonsterKillEvent evt)
		{
			_killed++;
			renderKill();
		}

		// 모드가 시작할 때 가득 찬 라이프를 1회 알린다 — 재도전으로 HUD 가 이어질 때 처치 수도 여기서 되돌린다.
		private void onLifeChanged(RiftLifeChangedEvent evt)
		{
			if (evt.Life == evt.MaxLife)
			{
				_killed = 0;
				renderKill();
			}

			_lifeText.text = evt.Life.ToString();
		}

		private void onGaugeChanged(RiftGaugeChangedEvent evt)
		{
			if (evt.RiftSkillId != _iconSkillId)
			{
				_iconSkillId = evt.RiftSkillId;
				applySkillIcon(evt.RiftSkillId);
			}

			int stack = (evt.ReqGauge > 0) ? evt.Gauge / evt.ReqGauge : 0;

			// 다음 1회분이 얼마나 찼는지. 상한까지 다 모았으면 가득으로 본다.
			float ratio;
			if (evt.ReqGauge <= 0)
			{
				ratio = 0f;
			}
			else if (evt.Gauge >= evt.MaxGauge)
			{
				ratio = 1f;
			}
			else
			{
				ratio = (float)(evt.Gauge % evt.ReqGauge) / evt.ReqGauge;
			}

			_gaugeFill.fillAmount = ratio;
			_gaugeText.text = Mathf.FloorToInt(ratio * 100f).ToString() + "%";
			_stackText.text = stack.ToString();

			// 게이지 덮개와 %는 스택과 무관하게 항상 보인다 — 다음 1회분 충전률(슬라이더와 같은 값)을 계속 알린다.
			_skillButton.interactable = stack > 0;
		}

		private void onSkillClicked()
		{
			EventManager.Instance.Publish(new RiftSkillUseRequestedEvent());
		}

		private void renderKill()
		{
			_killText.text = _killed.ToString();
		}

		// ── 배너 ──────────────────────────────────────────────────────

		// 배너를 즉시 띄우고 유지·페이드 후 숨긴다.
		private void showTitle(string text)
		{
			_waveTitleText.text = text;

			stopTitleRoutine();
			_waveTitleGroup.alpha = 1f;
			_waveTitleGroup.gameObject.SetActive(true);

			_titleRoutine = StartCoroutine(holdThenFadeTitle());
		}

		private IEnumerator holdThenFadeTitle()
		{
			yield return new WaitForSeconds(_titleHoldSeconds);

			float elapsed = 0f;
			while (elapsed < _titleFadeSeconds)
			{
				elapsed += Time.deltaTime;
				_waveTitleGroup.alpha = Mathf.Clamp01(1f - elapsed / _titleFadeSeconds);
				yield return null;
			}

			_waveTitleGroup.gameObject.SetActive(false);
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

		// ── 스킬 아이콘 ───────────────────────────────────────────────

		private void applySkillIcon(int riftSkillId)
		{
			Table_RiftSkill.Row rift = Table_RiftSkill.Get(riftSkillId);
			Table_Skill.Row skill = (rift != null) ? Table_Skill.Get(rift.SkillID) : null;
			string address = (skill != null) ? skill.Icon : string.Empty;

			_skillIconBinder.SetAsync(address, this.GetCancellationTokenOnDestroy()).Forget();
		}
	}
}
