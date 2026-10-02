using System;
using UnityEngine;
using TMPro;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 골드던전의 진행 표시(상태줄 — 단계·남은시간·웨이브·처치 진행).
	//
	// **MainHUD 에 상주하지 않는다.** 던전마다 필요한 위젯이 달라서, 안 쓰는 것까지 한 프리팹에 넣어 두면
	// 유지가 안 된다. MainHUD 에는 어느 던전에서나 쓰는 보스 정보만 남기고, 던전 전용 위젯은 그 던전에
	// 들어갈 때 UIManager.EnsureDungeonHudAsync 가 Canvas_Overlay 에 만들고 종료 시 파괴한다.
	//
	// 웨이브 시작·클리어 배너는 던전 공용 메시지(DungeonMessage)가 띄운다.
	//
	// **루트는 끄지 않고 상태줄을 SetActive 한다.** 루트를 끄면 Update 가 멈춰서
	// 보스 배너가 물러나도 되살아나지 못한다. 루트는 그래픽이 없는 순수 컨테이너다.
	public class GoldDungeonUI : MonoBehaviour
	{
		[SerializeField] private GameObject _state;			// State
		[SerializeField] private TMP_Text _stageText;		// State/Stage/Text
		[SerializeField] private TMP_Text _remainTimeText;	// State/Time/Text
		[SerializeField] private TMP_Text _waveText;		// State/Wave/Text
		[SerializeField] private TMP_Text _progressText;	// State/Progress/Text

		// 자리 양보 대상인 보스 배너. MainHUD 안에 살아 다른 프리팹이므로 인스펙터로 이을 수 없다 —
		// 소유자인 UIManager 에게 물어 Awake 에서 한 번만 잡는다.
		private BossUI _bossUI;

		private Action<DungeonStageStartedEvent> _onStageStarted;
		private Action<WaveStartedEvent> _onWaveStarted;
		private Action<DungeonStageClearedEvent> _onStageCleared;

		// 마지막으로 반영한 보스 배너 상태 — 바뀐 프레임에만 SetActive 를 건드린다.
		private bool _lastBossShowing;

		// 이번 웨이브에서 처치해야 하는 총 마리 수. 0 이면 웨이브 진행 중이 아니다.
		private int _requiredKills;

		// 마지막으로 그린 값 — 매 프레임 문자열을 새로 만들지 않기 위해 바뀐 프레임에만 갱신한다.
		private int _lastKilled = -1;
		private int _lastRemainSecond = -1;

		private void Awake()
		{
			if (UIManager.HasInstance == true)
			{
				_bossUI = UIManager.Instance.BossUI;
			}

			_onStageStarted = onStageStarted;
			_onWaveStarted = onWaveStarted;
			_onStageCleared = onStageCleared;
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);
			EventManager.Instance.Subscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Subscribe<DungeonStageClearedEvent>(_onStageCleared);

			applyVisibility();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
			EventManager.Instance.Unsubscribe<WaveStartedEvent>(_onWaveStarted);
			EventManager.Instance.Unsubscribe<DungeonStageClearedEvent>(_onStageCleared);
		}

		private void Update()
		{
			// 보스 배너와 자리가 겹친다 — 보스가 떠 있으면 비켜주고, 물러나면 되돌아온다.
			if (isBossShowing() != _lastBossShowing)
			{
				applyVisibility();
			}

			refreshRemainTime();

			if (_requiredKills > 0)
			{
				refreshProgress();
			}
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 재도전·다음 단계도 같은 HUD 로 다시 시작한다 — 표시를 처음 상태로 돌린다.
		private void onStageStarted(DungeonStageStartedEvent evt)
		{
			if (evt.DungeonType != EDT.Dungeon.Gold)
			{
				return;
			}

			if (_stageText != null)
			{
				_stageText.text = evt.Stage + "단계";
			}

			_requiredKills = 0;
			_lastKilled = -1;
			_lastRemainSecond = -1;
		}

		private void onWaveStarted(WaveStartedEvent evt)
		{
			_requiredKills = evt.RequiredKills;
			_lastKilled = -1;

			if (_waveText != null)
			{
				_waveText.text = "웨이브 " + evt.CurrentWave;
			}
		}

		// 던전 클리어 — 진행 게이지는 더 갱신할 이유가 없다.
		private void onStageCleared(DungeonStageClearedEvent evt)
		{
			_requiredKills = 0;
		}

		// ── 표시 ──────────────────────────────────────────────────────

		// 처치 수 전용 이벤트가 없어 살아있는 몬스터 수를 폴링한다.
		// 웨이브 몬스터 외에는 MonsterSpawnManager 에 잡히지 않으므로 이 뺄셈이 곧 처치 수다.
		private void refreshProgress()
		{
			if (MonsterSpawnManager.HasInstance == false || _progressText == null)
			{
				return;
			}

			int killed = _requiredKills - MonsterSpawnManager.Instance.ActiveCount;
			if (killed < 0)
			{
				killed = 0;
			}

			if (killed == _lastKilled)
			{
				return;
			}

			_lastKilled = killed;
			_progressText.text = killed + "/" + _requiredKills;
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

		private bool isBossShowing()
		{
			return (_bossUI != null && _bossUI.IsShowing == true);
		}

		private void applyVisibility()
		{
			bool boss = isBossShowing();
			_lastBossShowing = boss;

			if (_state != null)
			{
				_state.SetActive(boss == false);
			}
		}
	}
}
