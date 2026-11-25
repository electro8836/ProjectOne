using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 균열 던전 HUD(UI Toolkit, UIToolkit/HUD/Dungeon/RiftDungeonHud.uxml) — 웨이브·남은시간·라이프·처치 수 상태줄, 균열 스킬 버튼. 웨이브·종료 배너는 DungeonMessage 가 띄운다.
	//
	// GoldDungeonUI 와 같은 자리·같은 규칙으로 산다 — MainHUD 에 상주하지 않고 UIManager.EnsureDungeonHudAsync
	// 가 던전 진입 시 만들고 종료 시 걷는다. 생성이 startStage 보다 앞이어야 첫 배너와 시작 알림을 놓치지 않는다.
	//
	// 값의 소유자는 RiftDungeonMode(라이프·게이지)와 DungeonDirector(남은시간)다. 여기서는 이벤트를 받아 그리기만 하고,
	// 스킬 버튼도 사용 요청만 발행한다 — 사용 가능 판정은 모드가 한다.
	public class RiftDungeonUI : ToolkitScreen
	{
		private Label _waveText;
		private Label _remainTimeText;
		private Label _lifeText;
		private Label _killText;

		private Button _skillButton;
		private VisualElement _skillIcon;
		private VisualElement _gaugeShade;	// 아직 안 찬 만큼 위에서 덮는다
		private Label _gaugeText;
		private Label _stackText;

		private Action<WaveStartedEvent> _onWaveStarted;
		private Action<MonsterKillEvent> _onMonsterKill;
		private Action<RiftLifeChangedEvent> _onLifeChanged;
		private Action<RiftGaugeChangedEvent> _onGaugeChanged;

		private int _killed;
		private int _lastRemainSecond = -1;

		// 마지막으로 그린 균열 스킬 — 게이지 알림마다 아이콘을 다시 걸지 않는다.
		private int _iconSkillId;
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		protected override void OnBind(VisualElement root)
		{
			_waveText = root.Q<Label>("wave");
			_remainTimeText = root.Q<Label>("time");
			_lifeText = root.Q<Label>("life");
			_killText = root.Q<Label>("kill");
			_skillButton = root.Q<Button>("skill");
			_skillIcon = root.Q("skill-icon");
			_gaugeShade = root.Q("skill-shade");
			_gaugeText = root.Q<Label>("skill-gauge");
			_stackText = root.Q<Label>("skill-stack");
			_skillButton.SetEnabled(false);

			_onWaveStarted = onWaveStarted;
			_onMonsterKill = onMonsterKill;
			_onLifeChanged = onLifeChanged;
			_onGaugeChanged = onGaugeChanged;
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Subscribe<MonsterKillEvent>(_onMonsterKill);
			EventManager.Instance.Subscribe<RiftLifeChangedEvent>(_onLifeChanged);
			EventManager.Instance.Subscribe<RiftGaugeChangedEvent>(_onGaugeChanged);

			_skillButton.clicked += onSkillClicked;

			renderKill();
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onWaveStarted);
				EventManager.Instance.Unsubscribe<MonsterKillEvent>(_onMonsterKill);
				EventManager.Instance.Unsubscribe<RiftLifeChangedEvent>(_onLifeChanged);
				EventManager.Instance.Unsubscribe<RiftGaugeChangedEvent>(_onGaugeChanged);
				_skillButton.clicked -= onSkillClicked;
			}

			_icons.ReleaseAll();
			base.OnDestroy();
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

			_gaugeShade.style.height = Length.Percent((1f - ratio) * 100f);
			_gaugeText.text = Mathf.FloorToInt(ratio * 100f).ToString() + "%";
			_stackText.text = stack.ToString();

			// 게이지 덮개와 %는 스택과 무관하게 항상 보인다 — 다음 1회분 충전률(슬라이더와 같은 값)을 계속 알린다.
			_skillButton.SetEnabled(stack > 0);
		}

		private void onSkillClicked()
		{
			EventManager.Instance.Publish(new RiftSkillUseRequestedEvent());
		}

		private void renderKill()
		{
			_killText.text = _killed.ToString();
		}

		// ── 스킬 아이콘 ───────────────────────────────────────────────

		private void applySkillIcon(int riftSkillId)
		{
			Table_RiftSkill.Row rift = Table_RiftSkill.Get(riftSkillId);
			Table_Skill.Row skill = (rift != null) ? Table_Skill.Get(rift.SkillID) : null;
			string address = (skill != null) ? skill.Icon : string.Empty;

			_icons.SetAsync(_skillIcon, address, this.GetCancellationTokenOnDestroy()).Forget();
		}
	}
}
