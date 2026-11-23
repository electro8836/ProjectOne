using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 강화·등급업 버튼 한쪽의 비용 표시.
	public struct PetCostData
	{
		public bool available;		// 버튼을 누를 수 있는가
		public bool notEnough;		// 재화 부족 — 이때만 비용을 빨갛게
		public bool hasCost;		// 한계 도달이면 false — 아이콘을 감추고 한계 문구를 낸다
		public string limitText;	// "최대레벨" / "최대등급"
		public string iconAddress;
		public string amountText;
	}

	// 무대에 보일 고른 펫 1마리의 표시 데이터. 문구·색은 Presenter 가 끝낸다.
	public struct PetDetailData
	{
		public bool owned;
		public bool equipped;
		public string imageAddress;
		public string name;
		public string gradeText;	// 등급색이 입혀진 리치텍스트. 미보유는 빈 문자열
		public string levelText;	// "Lv.12 / 30"
		public float levelRatio;	// 레벨 막대 0~1
		public string bonusText;	// 보유는 수치까지, 미보유는 "공격력 증가" 처럼 간략히
		public string equipText;	// 장착 효과
		public string acquireText;	// 미보유일 때 획득처 띠 문구
		public Color glowColor;		// 무대 빛 — 등급 테두리색(보유만 쓴다)
	}

	// 펫 목록 창의 View(MVP, UI Toolkit). 레이아웃은 UIToolkit/Screens/Pet/PetScreen.uxml.
	// 장비창의 펫 버튼이 창 층에 겹쳐 연다. 위 무대에 고른 펫과 장착·강화·등급업, 아래에 4열 목록.
	// 칸을 누르면 Presenter 가 그 펫을 무대에 올린다.
	public class PetInfoUI : ToolkitScreen, IView
	{
		private const string LOCKED_CLASS = "pet--locked";
		private static readonly Color LackColor = new Color(1f, 0.35f, 0.35f);

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action<int> OnSlotClicked;
		public event Action OnSortClicked;
		public event Action OnReturnClicked;
		public event Action OnEquipClicked;
		public event Action OnLevelUpClicked;
		public event Action OnLevelUpHeld;	// 강화 버튼을 누른 채 반복
		public event Action OnGradeUpClicked;

		// 비용 버튼 하나의 표시 요소.
		private sealed class CostButton
		{
			public Button button;
			public VisualElement icon;
			public Label amount;
		}

		private readonly PetInfoPresenter _presenter = new PetInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<PetCell> _cells = new List<PetCell>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private VisualElement _screen;
		private Label _count;
		private Button _sortButton;
		private ScrollView _list;

		private VisualElement _stageGlow;
		private VisualElement _stageImage;
		private Label _detailName;
		private Label _detailGrade;
		private VisualElement _levelFill;
		private Label _levelText;
		private Label _detailBonus;
		private Label _detailEquipText;
		private Label _acquireText;
		private Button _equip;
		private CostButton _levelUp;
		private CostButton _gradeUp;
		private HoldRepeatManipulator _hold;

		// Presenter 가 등급 색을 정해야 해서 열어 둔다.
		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		public override bool HidesNavigationBar
		{
			get { return true; }
		}

		protected override void OnBind(VisualElement root)
		{
			_screen = root.Q("screen");
			_count = root.Q<Label>("count");
			_sortButton = root.Q<Button>("sort-button");
			_sortButton.clicked += onSortClicked;
			root.Q<Button>("return-button").clicked += onReturnClicked;

			_list = root.Q<ScrollView>("list");
			_list.AddManipulator(new ScrollDragManipulator(_list));

			_stageGlow = root.Q("stage-glow");
			_stageImage = root.Q("stage-image");
			_detailName = root.Q<Label>("detail-name");
			_detailGrade = root.Q<Label>("detail-grade");
			_levelFill = root.Q("level-fill");
			_levelText = root.Q<Label>("level-text");
			_detailBonus = root.Q<Label>("detail-bonus");
			_detailEquipText = root.Q<Label>("detail-equip-text");
			_acquireText = root.Q<Label>("acquire-text");

			_equip = root.Q<Button>("equip-button");
			_equip.clicked += onEquipClicked;

			_levelUp = bindCostButton(root, "level-up");
			_levelUp.button.clicked += onLevelUpClicked;
			_hold = new HoldRepeatManipulator(onLevelUpHeld);
			_levelUp.button.AddManipulator(_hold);

			_gradeUp = bindCostButton(root, "grade-up");
			_gradeUp.button.clicked += onGradeUpClicked;

			_presenter.Initialize(this);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				_presenter.Dispose();
			}

			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		public void SetTitle(int owned, int total)
		{
			_count.text = $"{owned} / {total}";
		}

		public void SetSortLabel(string label)
		{
			_sortButton.text = label;
		}

		// 무대·정보 판. 보유/미보유 갈래(버튼 줄 ↔ 획득처 띠, 레벨 막대 등)는 루트 클래스 하나로 USS 가 가른다.
		public UniTask SetDetailAsync(PetDetailData data, CancellationToken ct)
		{
			_screen.EnableInClassList(LOCKED_CLASS, data.owned == false);

			_detailName.text = data.name;
			_detailGrade.text = data.gradeText;
			_levelText.text = data.levelText;
			_levelFill.style.width = Length.Percent(Mathf.Clamp01(data.levelRatio) * 100f);
			_detailBonus.text = data.bonusText;
			_detailEquipText.text = data.equipText;
			_acquireText.text = data.acquireText;

			// 무대 빛은 보유만 등급색 — 미보유는 USS 의 흐린 색을 쓰도록 인라인 값을 지운다
			if (data.owned == true)
			{
				_stageGlow.style.unityBackgroundImageTintColor = data.glowColor;
			}
			else
			{
				_stageGlow.style.unityBackgroundImageTintColor = StyleKeyword.Null;
			}

			_equip.text = data.equipped ? "해제" : "장착";
			_equip.EnableInClassList("pet-action--unequip", data.equipped);

			return _icons.SetAsync(_stageImage, data.imageAddress, ct);
		}

		public void SetLevelUpCost(PetCostData cost)
		{
			applyCost(_levelUp, cost);
		}

		public void SetGradeUpCost(PetCostData cost)
		{
			applyCost(_gradeUp, cost);
		}

		public void SetSelectedPet(int petId)
		{
			for (int i = 0; i < _cells.Count; i++)
			{
				_cells[i].SetSelected(_cells[i].PetId == petId);
			}
		}

		public async UniTask RenderListAsync(IReadOnlyList<PetSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				PetCell cell = getOrCreateCell(i);
				cell.SetShown(true);
				_bindTasks.Add(cell.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _cells.Count; i++)
			{
				_cells[i].SetShown(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private PetCell getOrCreateCell(int index)
		{
			if (index < _cells.Count)
			{
				return _cells[index];
			}

			PetCell cell = new PetCell(_list.contentContainer, _icons);
			cell.OnClicked += onCellClicked;
			_cells.Add(cell);
			return cell;
		}

		private static CostButton bindCostButton(VisualElement root, string name)
		{
			CostButton cost = new CostButton();
			cost.button = root.Q<Button>(name);
			cost.icon = cost.button.Q(className: "pet-cost__icon");
			cost.amount = cost.button.Q<Label>(className: "pet-cost__amount");
			return cost;
		}

		// 한계 도달이면 아이콘을 감추고 한계 문구, 아니면 비용(부족하면 빨갛게).
		private static void applyCost(CostButton target, PetCostData cost)
		{
			target.button.SetEnabled(cost.available);
			target.icon.style.display = cost.hasCost ? DisplayStyle.Flex : DisplayStyle.None;

			if (cost.hasCost == false)
			{
				target.amount.text = cost.limitText;
				target.amount.style.color = StyleKeyword.Null;
				return;
			}

			target.amount.text = cost.amountText;
			target.amount.style.color = cost.notEnough ? new StyleColor(LackColor) : new StyleColor(StyleKeyword.Null);

			Sprite sprite = string.IsNullOrEmpty(cost.iconAddress) ? null : AtlasManager.Instance.Get(cost.iconAddress);
			if (sprite != null)
			{
				target.icon.style.backgroundImage = new StyleBackground(sprite);
			}
		}

		private void onCellClicked(PetCell sender)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke(sender.PetId);
			}
		}

		private void onSortClicked()
		{
			if (OnSortClicked != null)
			{
				OnSortClicked.Invoke();
			}
		}

		private void onReturnClicked()
		{
			if (OnReturnClicked != null)
			{
				OnReturnClicked.Invoke();
			}
		}

		private void onEquipClicked()
		{
			if (OnEquipClicked != null)
			{
				OnEquipClicked.Invoke();
			}
		}

		private void onLevelUpClicked()
		{
			// 누른 채 반복이 나간 누름이면 뗄 때의 클릭은 건너뛴다.
			if (_hold.HoldFired == true)
			{
				return;
			}

			if (OnLevelUpClicked != null)
			{
				OnLevelUpClicked.Invoke();
			}
		}

		private void onLevelUpHeld()
		{
			if (OnLevelUpHeld != null)
			{
				OnLevelUpHeld.Invoke();
			}
		}

		private void onGradeUpClicked()
		{
			if (OnGradeUpClicked != null)
			{
				OnGradeUpClicked.Invoke();
			}
		}
	}
}
