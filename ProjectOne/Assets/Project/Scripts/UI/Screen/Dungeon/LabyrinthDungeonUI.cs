using System;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Dungeon;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 미궁던전의 진행 표시(UI Toolkit, UIToolkit/HUD/Dungeon/LabyrinthDungeonHud.uxml). GoldDungeonUI 와 같이 던전 입장 시 UIManager.EnsureDungeonHudAsync 가 만들고 종료 시 파괴한다.
	//
	// 구성 (단계 시작 안내는 DungeonMessage 가 띄운다)
	//  - Wave / Time / Chest : 단계, 남은 시간, 상자 개봉 수
	//  - Progress : 캐릭터·불 위치 막대, 구간 포인트 5개, 진행률 %
	//
	// 위치 진행도는 이벤트가 아니라 모드를 폴링한다 — 매 프레임 바뀌는 값이다.
	public class LabyrinthDungeonUI : ToolkitScreen
	{
		// 구간 포인트가 켜지는 진행도 — point-0 ~ point-4 순서다.
		private static readonly float[] PointThresholds = { 0f, 0.25f, 0.5f, 0.75f, 1f };

		private const string PointReachedClass = "laby-point--reached";

		private static readonly Color ChestNormalColor = Color.white;
		private static readonly Color ChestCompleteColor = Color.green;

		private Label _waveText;
		private Label _timeText;
		private Label _chestText;
		private Label _remainMonsterText;
		private VisualElement _playerFill;
		private VisualElement _fireFill;
		private readonly VisualElement[] _points = new VisualElement[PointThresholds.Length];
		private Label _infoText;

		private Action<DungeonStageStartedEvent> _onStageStarted;
		private Action<LabyrinthChestChangedEvent> _onChestChanged;

		// 마지막으로 그린 값 — 바뀐 프레임에만 문자열·표시를 건드린다.
		private int _lastRemainSecond = -1;
		private int _lastRemainMonster = -1;
		private int _lastPercent = -1;
		private int _lastReachedCount = -1;

		protected override void OnBind(VisualElement root)
		{
			_waveText = root.Q<Label>("stage");
			_timeText = root.Q<Label>("time");
			_chestText = root.Q<Label>("chest");
			_remainMonsterText = root.Q<Label>("remain");
			_playerFill = root.Q("player-fill");
			_fireFill = root.Q("fire-fill");
			_infoText = root.Q<Label>("info");
			for (int i = 0; i < _points.Length; i++)
			{
				_points[i] = root.Q("point-" + i);
			}

			_onStageStarted = onStageStarted;
			_onChestChanged = onChestChanged;
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(_onStageStarted);
			EventManager.Instance.Subscribe<LabyrinthChestChangedEvent>(_onChestChanged);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(_onStageStarted);
				EventManager.Instance.Unsubscribe<LabyrinthChestChangedEvent>(_onChestChanged);
			}

			base.OnDestroy();
		}

		private void Update()
		{
			if (IsBound == false)
			{
				return;
			}

			refreshRemainTime();
			refreshRemainMonster();
			refreshProgress();
		}

		// ── 이벤트 ────────────────────────────────────────────────────

		// 재도전·다음 단계도 같은 HUD 로 다시 시작한다 — 표시를 처음 상태로 돌린다.
		private void onStageStarted(DungeonStageStartedEvent evt)
		{
			if (evt.DungeonType != EDT.Dungeon.Labyrinth)
			{
				return;
			}

			if (_waveText != null)
			{
				_waveText.text = evt.Stage + "단계";
			}

			_lastRemainSecond = -1;
			_lastRemainMonster = -1;
			_lastPercent = -1;
			_lastReachedCount = -1;
		}

		private void onChestChanged(LabyrinthChestChangedEvent evt)
		{
			if (_chestText == null)
			{
				return;
			}

			_chestText.text = evt.Opened + "/" + evt.Total;

			bool complete = (evt.Total > 0 && evt.Opened >= evt.Total);
			_chestText.style.color = complete ? ChestCompleteColor : ChestNormalColor;
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

		// 필드에 남은 몬스터 수. 처치 전용 이벤트가 없어 폴링한다 (GoldDungeonUI 와 같은 기준).
		private void refreshRemainMonster()
		{
			if (_remainMonsterText == null || MonsterSpawnManager.HasInstance == false)
			{
				return;
			}

			int count = MonsterSpawnManager.Instance.ActiveCount;
			if (count == _lastRemainMonster)
			{
				return;
			}

			_lastRemainMonster = count;
			_remainMonsterText.text = count.ToString();
		}

		private void refreshProgress()
		{
			if (DungeonDirector.HasInstance == false)
			{
				return;
			}

			LabyrinthDungeonMode mode = DungeonDirector.Instance.CurrentMode as LabyrinthDungeonMode;
			if (mode == null)
			{
				return;
			}

			float player = mode.PlayerProgress;

			_playerFill.style.height = Length.Percent(Mathf.Clamp01(player) * 100f);
			_fireFill.style.height = Length.Percent(Mathf.Clamp01(mode.FireProgress) * 100f);

			refreshPoints(player);

			int percent = Mathf.RoundToInt(player * 100f);
			if (percent != _lastPercent && _infoText != null)
			{
				_lastPercent = percent;
				_infoText.text = percent + "%";
			}
		}

		// 도달한 포인트 수가 바뀐 프레임에만 색을 바꾼다.
		private void refreshPoints(float progress)
		{
			int reached = 0;
			for (int i = 0; i < PointThresholds.Length; i++)
			{
				if (progress >= PointThresholds[i])
				{
					reached = i + 1;
				}
			}

			if (reached == _lastReachedCount)
			{
				return;
			}

			_lastReachedCount = reached;

			for (int i = 0; i < _points.Length; i++)
			{
				_points[i].EnableInClassList(PointReachedClass, i < reached);
			}
		}
	}
}
