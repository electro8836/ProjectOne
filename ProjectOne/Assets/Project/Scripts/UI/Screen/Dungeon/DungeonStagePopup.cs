using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 던전 정보 1회분 렌더 데이터 — 단계를 바꿔도 변하지 않는 부분이다.
	public struct DungeonInfoData
	{
		public string name;
		public string thumbnailAddress;
	}

	// 던전 단계 칸의 상태. 칸 색과 글자가 여기서 갈린다.
	public enum DungeonStageState
	{
		Locked = 0,		// 아직 못 여는 단계
		Current,		// 지금 도전할 수 있는 단계
		Cleared			// 이미 깬 단계
	}

	// 던전 단계 칸 1개 렌더 데이터.
	public struct DungeonStageSlotData
	{
		public int stage;
		public DungeonStageState state;
	}

	// 단계를 골라 들어가는 던전 팝업(골드·미궁·유적)의 공용 View. 레이아웃은 UIToolkit/Popups/Dungeon/DungeonStagePopup.uxml.
	// 단계 칸은 가로 스크롤 한 줄 — 누른 칸이 선택된다. 처음에는 지금 도전할 단계(없으면 첫 칸)를 고르고 그 칸이 보이게 민다.
	// 잠긴 단계도 고를 수 있다(입장 가능 여부는 Presenter 가 SetEnterInteractable 로 정한다).
	public abstract class DungeonStagePopup : DungeonPopupBase
	{
		private const string CellClass = "dungeon-stage";
		private const string SelectedClass = "dungeon-stage--selected";

		public event Action<int> OnStageSelected;	// 선택된 단계 번호

		private readonly List<Button> _cells = new List<Button>();
		private readonly List<int> _cellStages = new List<int>();

		private Label _maxStage;
		private Label _enterCount;
		private Label _reward;
		private VisualElement _rewardRow;
		private ScrollView _stages;

		protected override void BindContent(VisualElement root)
		{
			_maxStage = root.Q<Label>("max-stage");
			_enterCount = root.Q<Label>("enter-count");
			_reward = root.Q<Label>("reward");
			_rewardRow = root.Q("reward-row");
			_stages = root.Q<ScrollView>("stages");

			_stages.AddManipulator(new ScrollDragManipulator(_stages));
			_rewardRow.style.display = DisplayStyle.None;
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public UniTask RenderInfoAsync(DungeonInfoData data, CancellationToken ct)
		{
			SetName(data.name);
			SetThumbnail(data.thumbnailAddress);
			return UniTask.CompletedTask;
		}

		// 팝업이 떠 있는 동안 변하지 않는 현황 — 입장하면 팝업이 닫히므로 한 번만 그린다.
		public void RenderStatus(string maxStage, string enterCount)
		{
			_maxStage.text = maxStage;
			_enterCount.text = enterCount;
		}

		// 선택 단계의 보상(미궁의 상자 수). 부르는 팝업에만 줄이 보인다.
		public void SetReward(string text)
		{
			_rewardRow.style.display = DisplayStyle.Flex;
			_reward.text = text;
		}

		public void RenderStages(IReadOnlyList<DungeonStageSlotData> data)
		{
			_stages.Clear();
			_cells.Clear();
			_cellStages.Clear();

			int first = 0;
			for (int i = 0; i < data.Count; i++)
			{
				Button cell = new Button();
				cell.AddToClassList(CellClass);
				cell.AddToClassList(stateClass(data[i].state));
				cell.text = data[i].stage.ToString();
				cell.userData = i;
				cell.RegisterCallback<ClickEvent>(onCellClicked);
				_stages.Add(cell);

				_cells.Add(cell);
				_cellStages.Add(data[i].stage);

				if (data[i].state == DungeonStageState.Current)
				{
					first = i;
				}
			}

			select(first);

			// 칸이 배치된 다음에야 스크롤 위치를 잡을 수 있다.
			if (first < _cells.Count)
			{
				_stages.schedule.Execute(scrollToSelected);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private static string stateClass(DungeonStageState state)
		{
			switch (state)
			{
				case DungeonStageState.Cleared:
					return "dungeon-stage--cleared";
				case DungeonStageState.Current:
					return "dungeon-stage--current";
				default:
					return "dungeon-stage--locked";
			}
		}

		// 끌어서 스크롤한 뒤의 클릭은 ScrollDragManipulator 가 삼키므로 여기까지 오지 않는다.
		private void onCellClicked(ClickEvent evt)
		{
			VisualElement cell = (VisualElement)evt.currentTarget;
			select((int)cell.userData);
		}

		private void select(int index)
		{
			for (int i = 0; i < _cells.Count; i++)
			{
				_cells[i].EnableInClassList(SelectedClass, i == index);
			}

			if (index < 0 || index >= _cellStages.Count)
			{
				return;
			}

			if (OnStageSelected != null)
			{
				OnStageSelected.Invoke(_cellStages[index]);
			}
		}

		private void scrollToSelected()
		{
			for (int i = 0; i < _cells.Count; i++)
			{
				if (_cells[i].ClassListContains(SelectedClass) == true)
				{
					_stages.ScrollTo(_cells[i]);
					return;
				}
			}
		}
	}
}
