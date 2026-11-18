using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 펫 강화 팝업 1회분 표시 데이터. 문구·색은 Presenter 가 끝낸다.
	public struct PetPopupData
	{
		public string name;
		public string levelText;	// "레벨 : 1/20 일반"
		public string gradeText;	// "등급 : 일반"
		public string bonusText;
		public string equipText;	// "장착 효과 : ..."
		public bool equipped;
		public Color bgColor;		// 등급 bg
		public Color gradientColor;	// 등급 border
	}

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

	// 펫 강화 팝업(UIPrefab_PetEnhancePopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Pet/PetEnhancePopup.uxml.
	// 펫 그림·이름·레벨·등급·효과와 장착 버튼, 아래에 강화(누른 채 반복)·등급업 버튼과 비용을 둔다.
	public class PetEnhancePopup : ToolkitScreen, IView
	{
		private static readonly Color LackColor = new Color(1f, 0.35f, 0.35f);

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action OnEquipClicked;
		public event Action OnLevelUpClicked;
		public event Action OnLevelUpHeld;	// 강화 버튼을 누른 채 반복
		public event Action OnGradeUpClicked;

		// 버튼 하나의 비용 표시 요소.
		private sealed class CostButton
		{
			public Button button;
			public VisualElement icon;
			public Label amount;
		}

		private readonly PetEnhancePopupPresenter _presenter = new PetEnhancePopupPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private VisualElement _stage;
		private VisualElement _petImage;
		private Label _name;
		private Label _equipStat;
		private Button _equip;
		private Label _level;
		private Label _grade;
		private Label _bonus;
		private Label _equipText;
		private CostButton _levelUp;
		private CostButton _gradeUp;
		private HoldRepeatManipulator _hold;
		private UniTaskCompletionSource _tcs;

		public ItemGradeColorTable GradeColors
		{
			get { return _gradeColors; }
		}

		protected override void OnBind(VisualElement root)
		{
			_stage = root.Q("stage");
			_petImage = root.Q("pet-image");
			_name = root.Q<Label>("pet-name");
			_equipStat = root.Q<Label>("equip-stat");
			_equip = root.Q<Button>("equip");
			_equip.clicked += onEquipClicked;
			_level = root.Q<Label>("level");
			_grade = root.Q<Label>("grade");
			_bonus = root.Q<Label>("bonus");
			_equipText = root.Q<Label>("equip-text");

			_levelUp = bindCostButton(root, "level-up");
			_levelUp.button.clicked += onLevelUpClicked;
			_hold = new HoldRepeatManipulator(onLevelUpHeld);
			_levelUp.button.AddManipulator(_hold);

			_gradeUp = bindCostButton(root, "grade-up");
			_gradeUp.button.clicked += onGradeUpClicked;

			root.Q<Button>("close").clicked += Close;
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);

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

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		public async UniTask ShowAsync(int petId, CancellationToken ct)
		{
			await _presenter.ShowAsync(petId, ct);

			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void SetInfo(PetPopupData data)
		{
			_name.text = data.name;
			_level.text = data.levelText;
			_grade.text = data.gradeText;
			_bonus.text = data.bonusText;
			_equipText.text = data.equipText;

			_stage.style.backgroundColor = data.bgColor;
			_stage.style.unityBackgroundImageTintColor = data.gradientColor;

			_equipStat.style.display = data.equipped ? DisplayStyle.Flex : DisplayStyle.None;
			_equip.text = data.equipped ? "해제" : "장착";
		}

		public void SetLevelUpCost(PetCostData cost)
		{
			applyCost(_levelUp, cost);
		}

		public void SetGradeUpCost(PetCostData cost)
		{
			applyCost(_gradeUp, cost);
		}

		public UniTask SetPetImageAsync(string address, CancellationToken ct)
		{
			return _icons.SetAsync(_petImage, address, ct);
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ─────────────────────────────────────────────────────────

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

		private void onEquipClicked()
		{
			if (OnEquipClicked != null) { OnEquipClicked.Invoke(); }
		}

		private void onLevelUpClicked()
		{
			// 누른 채 반복이 나간 누름이면 뗄 때의 클릭은 건너뛴다.
			if (_hold.HoldFired == true)
			{
				return;
			}

			if (OnLevelUpClicked != null) { OnLevelUpClicked.Invoke(); }
		}

		private void onLevelUpHeld()
		{
			if (OnLevelUpHeld != null) { OnLevelUpHeld.Invoke(); }
		}

		private void onGradeUpClicked()
		{
			if (OnGradeUpClicked != null) { OnGradeUpClicked.Invoke(); }
		}

		private void onDimClicked(ClickEvent evt)
		{
			Close();
		}
	}
}
