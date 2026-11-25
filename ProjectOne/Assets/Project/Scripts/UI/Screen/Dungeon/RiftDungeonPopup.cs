using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 균열 던전 현황 1회분 렌더 데이터 — 입장·소탕으로 바뀌므로 그때마다 다시 그린다.
	public struct RiftDungeonStatusData
	{
		public string maxWave;
		public string checkPoint;
		public string enterCount;
		public int enterReward;
		public int waveReward;
		public string rewardIconAddress;
	}

	// 균열 스킬 한 칸의 렌더 데이터
	public struct RiftSkillSlotData
	{
		public int riftSkillId;
		public string iconAddress;
	}

	// 균열던전 팝업의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Popups/Dungeon/RiftDungeonPopup.uxml.
	// UIManager.ShowRiftDungeonPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 입장·소탕 판정과 스킬 선택 상태는 RiftDungeonPopupPresenter 가 한다.
	// 고른 스킬 줄을 누르면 스킬 설명을 툴팁으로 띄운다.
	public class RiftDungeonPopup : DungeonPopupBase
	{
		private const string SkillClass = "rift-skill";
		private const string SelectedClass = "rift-skill--selected";

		public event Action<int> OnSkillSelected;	// 고른 칸의 RiftSkill.ID
		public event Action OnSkillInfoClicked;
		public event Action OnSweepClicked;

		private readonly RiftDungeonPopupPresenter _presenter = new RiftDungeonPopupPresenter();
		private readonly List<VisualElement> _skills = new List<VisualElement>();
		private readonly List<int> _skillIds = new List<int>();

		private Label _maxWave;
		private Label _checkPoint;
		private Label _enterCount;
		private Label _enterReward;
		private VisualElement _enterRewardIcon;
		private Label _waveReward;
		private VisualElement _waveRewardIcon;
		private ScrollView _skillList;
		private Button _selectRoot;
		private Label _selectName;
		private VisualElement _selectIcon;
		private Button _sweep;

		protected override void BindContent(VisualElement root)
		{
			_maxWave = root.Q<Label>("max-wave");
			_checkPoint = root.Q<Label>("check-point");
			_enterCount = root.Q<Label>("enter-count");
			_enterReward = root.Q<Label>("enter-reward");
			_enterRewardIcon = root.Q("enter-reward-icon");
			_waveReward = root.Q<Label>("wave-reward");
			_waveRewardIcon = root.Q("wave-reward-icon");
			_skillList = root.Q<ScrollView>("skills");
			_selectRoot = root.Q<Button>("skill-select");
			_selectName = root.Q<Label>("skill-name");
			_selectIcon = root.Q("skill-icon");
			_sweep = root.Q<Button>("sweep");

			_skillList.AddManipulator(new ScrollDragManipulator(_skillList));
			_selectRoot.clicked += onSelectRootClicked;
			_sweep.clicked += onSweepClicked;
		}

		protected override void InitializePresenter()
		{
			_presenter.Initialize(this);
		}

		protected override void DisposePresenter()
		{
			_presenter.Dispose();
		}

		protected override UniTask OpenPresenterAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void RenderInfo(string dungeonName, string thumbnailAddress)
		{
			SetName(dungeonName);
			SetThumbnail(thumbnailAddress);
		}

		public void RenderStatus(RiftDungeonStatusData data)
		{
			_maxWave.text = data.maxWave;
			_checkPoint.text = data.checkPoint;
			_enterCount.text = data.enterCount;
			_enterReward.text = data.enterReward.ToString();
			_waveReward.text = data.waveReward.ToString();

			icons.SetAsync(_enterRewardIcon, data.rewardIconAddress, GetDestroyToken()).Forget();
			icons.SetAsync(_waveRewardIcon, data.rewardIconAddress, GetDestroyToken()).Forget();
		}

		public void RenderSkills(IReadOnlyList<RiftSkillSlotData> data)
		{
			_skillList.Clear();
			_skills.Clear();
			_skillIds.Clear();

			for (int i = 0; i < data.Count; i++)
			{
				VisualElement cell = new VisualElement();
				cell.AddToClassList(SkillClass);
				cell.AddToClassList("sfx-click");
				cell.userData = i;
				cell.RegisterCallback<ClickEvent>(onSkillClicked);

				VisualElement icon = new VisualElement();
				icon.AddToClassList("rift-skill__icon");
				icon.pickingMode = PickingMode.Ignore;
				cell.Add(icon);
				icons.SetAsync(icon, data[i].iconAddress, GetDestroyToken()).Forget();

				_skillList.Add(cell);
				_skills.Add(cell);
				_skillIds.Add(data[i].riftSkillId);
			}
		}

		// 고른 칸 하나만 테두리를 켜고, 고른 스킬 줄에 이름과 아이콘을 띄운다.
		public void SetSelectedSkill(int riftSkillId, string skillName, string iconAddress)
		{
			for (int i = 0; i < _skills.Count; i++)
			{
				_skills[i].EnableInClassList(SelectedClass, _skillIds[i] == riftSkillId);
			}

			_selectName.text = skillName;
			icons.SetAsync(_selectIcon, iconAddress, GetDestroyToken()).Forget();
		}

		// 고른 스킬 줄 위에 설명 툴팁을 띄운다.
		public void ShowSkillInfo(string skillName, int reqGauge, int maxGauge, string desc)
		{
			string text = "게이지 " + reqGauge.ToString() + "/" + maxGauge.ToString() + "\n" + desc;
			UIManager.Instance.ShowTooltipAsync(skillName, text, _selectRoot, GetDestroyToken()).Forget();
		}

		public void SetSweepInteractable(bool value)
		{
			_sweep.SetEnabled(value);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 끌어서 스크롤한 뒤의 클릭은 ScrollDragManipulator 가 삼키므로 여기까지 오지 않는다.
		private void onSkillClicked(ClickEvent evt)
		{
			int index = (int)((VisualElement)evt.currentTarget).userData;
			if (OnSkillSelected != null)
			{
				OnSkillSelected.Invoke(_skillIds[index]);
			}
		}

		private void onSelectRootClicked()
		{
			if (OnSkillInfoClicked != null)
			{
				OnSkillInfoClicked.Invoke();
			}
		}

		private void onSweepClicked()
		{
			if (OnSweepClicked != null)
			{
				OnSweepClicked.Invoke();
			}
		}
	}
}
