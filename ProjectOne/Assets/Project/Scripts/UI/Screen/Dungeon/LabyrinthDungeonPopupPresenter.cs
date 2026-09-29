using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Map;
using ProjectOne.Utils;
using UnityEngine;

namespace ProjectOne.UI
{
	// 미궁던전 팝업 Presenter — 단계 해금 판정과 입장을 담당한다.
	//
	// 단계 해금은 DungeonProgress 가 소유한다(최고 단계 + 1 까지 열린다). 여기서는 그 판정을
	// 슬롯 3상태로 옮기기만 한다.
	public sealed class LabyrinthDungeonPopupPresenter : Presenter<LabyrinthDungeonPopup>
	{
		private const EDT.Dungeon DUNGEON_TYPE = EDT.Dungeon.Labyrinth;

		// 단계 정렬 버퍼 — Dictionary 는 순서를 보장하지 않아 칸 순서가 흔들린다.
		private readonly List<Table_LabyrinthDungeon.Row> _stages = new List<Table_LabyrinthDungeon.Row>();
		private readonly List<DungeonStageSlotData> _slotData = new List<DungeonStageSlotData>();

		private CancellationTokenSource _renderCts;	// 아이콘 로드 경합 방지

		private int _selectedStage;

		// 마지막으로 그린 남은 초 — 매초 문자열을 새로 만들지 않기 위해 변화가 있을 때만 갱신한다.
		private int _lastRefreshSecond = -1;

		protected override void OnInitialize()
		{
			view.OnStageSelected += onStageSelected;
			view.OnEnterClicked += onEnterClicked;
			view.OnSecondTick += onSecondTick;
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnStageSelected -= onStageSelected;
			view.OnEnterClicked -= onEnterClicked;
			view.OnSecondTick -= onSecondTick;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			renderInfo();
			renderStatus();

			buildStages();

			// RenderStages 끝의 Refresh 가 첫 칸을 중앙에 세우며 OnStageSelected 를 발행한다 —
			// 첫 선택을 여기서 따로 맞출 필요가 없다.
			view.RenderStages(_slotData);

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onStageSelected(int stage)
		{
			_selectedStage = stage;

			// 상자 수는 맵 배치가 아니라 테이블 값이다 — 팝업 시점엔 맵이 로드돼 있지 않다.
			Table_LabyrinthDungeon.Row row = DungeonProgress.FindLabyrinthStage(stage);
			int chestCount = (row != null) ? row.ChestCount : 0;
			view.SetReward(chestCount + "개");

			// 아직 못 여는 단계를 고르면 입장을 막는다. 남은 입장 횟수도 함께 본다.
			bool unlocked = DungeonProgress.IsStageUnlocked(DUNGEON_TYPE, stage);
			view.SetEnterInteractable(unlocked == true && DungeonProgress.CanEnter(DUNGEON_TYPE) == true);
		}

		// 개발용 워프와 달리 입장 횟수를 소모하는 정식 경로다.
		private void onEnterClicked()
		{
			if (_selectedStage <= 0)
			{
				return;
			}

			if (DungeonProgress.IsStageUnlocked(DUNGEON_TYPE, _selectedStage) == false)
			{
				return;
			}

			if (DungeonProgress.TryConsumeEnter(DUNGEON_TYPE) == false)
			{
				Debug.Log("[LabyrinthDungeonPopup] 남은 입장 횟수가 없습니다.");
				return;
			}

			view.Close();
			MapNavigator.StartDungeon(DUNGEON_TYPE, _selectedStage);
		}

		// 1초마다. 남은 초가 그대로면 문자열을 새로 만들지 않는다.
		private void onSecondTick()
		{
			int remaining = (int)DailyReset.GetRemaining().TotalSeconds;
			if (remaining < 0)
			{
				remaining = 0;
			}

			if (remaining == _lastRefreshSecond)
			{
				return;
			}

			_lastRefreshSecond = remaining;
			view.SetRefreshTime(DailyReset.FormatDuration(remaining));
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 최고 스테이지와 입장 가능 횟수(남은/최대). 팝업이 떠 있는 동안 변하지 않는다.
		private void renderStatus()
		{
			int highest = DungeonProgress.GetHighestStage(DUNGEON_TYPE);
			string maxStage = (highest > 0) ? highest.ToString() : "없음";

			string enterCount = DungeonProgress.GetRemainingCount(DUNGEON_TYPE) + "/" + DungeonProgress.GetMaxCount(DUNGEON_TYPE);

			view.RenderStatus(maxStage, enterCount);
		}

		private void renderInfo()
		{
			Table_Dungeon.Row row = Table_Dungeon.Get(DUNGEON_TYPE);
			if (row == null)
			{
				Debug.LogError($"[LabyrinthDungeonPopup] Table_Dungeon.Get({DUNGEON_TYPE}) == null");
				return;
			}

			DungeonInfoData data;
			data.name = row.Name;
			data.thumbnailAddress = row.Thumbnail;

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderInfoAsync(data, _renderCts.Token).Forget();
		}

		// 이 던전의 단계를 Stage 순으로 담고 해금 상태를 붙인다.
		private void buildStages()
		{
			_stages.Clear();

			Dictionary<int, Table_LabyrinthDungeon.Row> all = Table_LabyrinthDungeon.All();
			Dictionary<int, Table_LabyrinthDungeon.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_stages.Add(e.Current.Value);
			}

			_stages.Sort(compareStage);

			int highest = DungeonProgress.GetHighestStage(DUNGEON_TYPE);

			_slotData.Clear();
			for (int i = 0; i < _stages.Count; i++)
			{
				int stage = _stages[i].Stage;

				DungeonStageSlotData data;
				data.stage = stage;

				if (stage <= highest)
				{
					data.state = DungeonStageState.Cleared;
				}
				else if (stage == highest + 1)
				{
					data.state = DungeonStageState.Current;
				}
				else
				{
					data.state = DungeonStageState.Locked;
				}

				_slotData.Add(data);
			}
		}

		private int compareStage(Table_LabyrinthDungeon.Row a, Table_LabyrinthDungeon.Row b)
		{
			return a.Stage.CompareTo(b.Stage);
		}
	}
}
