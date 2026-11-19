using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Items;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 장비 정보 팝업(UIPrefab_EquipmentPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/ItemInfo/EquipmentPopup.uxml.
	// 이름·등급·레벨·품질·설명, 기본 옵션(아이콘+문구), 등급 옵션(등급 표시+문구+범위, 잠긴 줄은 회색)을 보여 주고
	// 장착·강화·보관·분해·잠금 버튼을 둔다. 읽기 전용이면 버튼 줄을 감춘다. 결정은 ItemInfoPresenter 가 한다.
	public class ItemInfoPopup : ToolkitScreen, IView
	{
		private static readonly Color LockedColor = new Color(0.42745098f, 0.42745098f, 0.42745098f, 1f);	// #6D6D6D

		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action OnEquipToggleClicked;
		public event Action OnEnchantClicked;
		public event Action OnStashClicked;
		public event Action OnDecompositionClicked;
		public event Action OnLockClicked;
		public event Action OnExitClicked;

		private sealed class BasicLine
		{
			public VisualElement root;
			public VisualElement icon;
			public Label text;
		}

		private sealed class GradeLine
		{
			public VisualElement root;
			public VisualElement mark;
			public Label text;
			public Label range;
		}

		private readonly ItemInfoPresenter _presenter = new ItemInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<BasicLine> _basicLines = new List<BasicLine>();
		private readonly List<GradeLine> _gradeLines = new List<GradeLine>();

		private Label _name;
		private Label _grade;
		private Label _level;
		private Label _quality;
		private Label _desc;
		private VisualElement _basicBox;
		private VisualElement _gradeBox;
		private VisualElement _buttons;
		private Button _equip;
		private Button _enchant;
		private Button _stash;
		private Button _decompose;
		private Button _lock;
		private ItemCellView _cell;
		private ScrollView _scroll;

		// 제목 줄 이름이 한 줄에 들어가는 폭 — 한글 1칸 기준(공백은 0.3칸, 영숫자는 0.6칸).
		// 제목 칸 폭 약 720px, 한글 1칸 ≈ 글자 크기의 0.85배. 60px 로 넘치면 48px(--medium),
		// 48px 로도 넘치면 36px(--small)로 줄인다
		private const float TITLE_WIDTH_LARGE = 13.5f;
		private const float TITLE_WIDTH_MEDIUM = 17f;
		private VisualElement _head;
		private VisualElement _window;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			_head = root.Q("head");
			_window = root.Q(null, "popup__window");
			_name = root.Q<Label>("name");
			_grade = root.Q<Label>("grade");
			_level = root.Q<Label>("level");
			_quality = root.Q<Label>("quality");
			_desc = root.Q<Label>("desc");
			_basicBox = root.Q("basic-options");
			_gradeBox = root.Q("grade-options");
			_buttons = root.Q("buttons");
			_cell = new ItemCellView(root.Q("cell"), _gradeColors, _icons);

			_equip = root.Q<Button>("equip");
			_equip.clicked += onEquipClicked;
			_enchant = root.Q<Button>("enchant");
			_enchant.clicked += onEnchantClicked;
			_stash = root.Q<Button>("stash");
			_stash.clicked += onStashClicked;
			_decompose = root.Q<Button>("decompose");
			_decompose.clicked += onDecompositionClicked;
			_lock = root.Q<Button>("lock");
			_lock.clicked += onLockClicked;

			_scroll = root.Q<ScrollView>("scroll");
			_scroll.AddManipulator(new ScrollDragManipulator(_scroll));

			root.Q<Button>("close").clicked += onExitClicked;
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

		public UniTask ShowAsync(long uid, bool stashMode, CancellationToken ct)
		{
			return _presenter.ShowAsync(uid, stashMode, ct);
		}

		public UniTask ShowAsync(EquipmentInstance instance, CancellationToken ct)
		{
			return _presenter.ShowAsync(instance, ct);
		}

		public void SetReadOnly(bool readOnly)
		{
			_buttons.style.display = readOnly ? DisplayStyle.None : DisplayStyle.Flex;
		}

		public void Reveal()
		{
			PlayPopupOpen();
		}

		public void SetInfo(Table_Item.Row row, ItemGradeType grade, int level, int maxLevel, int quality)
		{
			_name.text = row.Name;
			float titleWidth = getTitleWidth(row.Name);
			_name.EnableInClassList("item-head__name--medium", titleWidth > TITLE_WIDTH_LARGE && titleWidth <= TITLE_WIDTH_MEDIUM);
			_name.EnableInClassList("item-head__name--small", titleWidth > TITLE_WIDTH_MEDIUM);
			_grade.text = ItemGradeNames.Get(grade);
			if (_gradeColors != null)
			{
				// 제목 줄은 등급색 — 판은 진한 색, 아래 선과 등급 박스는 밝은 색
				ItemGradeColorTable.GradeColor colors = _gradeColors.Get(grade);
				_head.style.backgroundColor = colors.bg;
				_head.style.borderBottomColor = colors.border;
				_grade.style.backgroundColor = colors.border;
			}

			_level.text = "레벨: " + level + "/" + maxLevel;
			_quality.text = "품질 : " + ProjectOne.Shared.EquipmentQuality.Format(quality) + "%";
			_desc.text = row.Desc;

			// 다른 장비를 열면 정보 덩어리를 맨 위부터 보인다
			_scroll.scrollOffset = Vector2.zero;
		}

		public void RenderBasicOptions(IReadOnlyList<OptionLine> lines)
		{
			for (int i = 0; i < lines.Count; i++)
			{
				BasicLine line = getBasicLine(i);
				line.root.style.display = DisplayStyle.Flex;
				line.text.text = lines[i].text;

				Sprite icon = string.IsNullOrEmpty(lines[i].iconAddress) ? null : AtlasManager.Instance.Get(lines[i].iconAddress);
				line.icon.style.backgroundImage = (icon != null) ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
			}

			for (int i = lines.Count; i < _basicLines.Count; i++)
			{
				_basicLines[i].root.style.display = DisplayStyle.None;
			}

			_basicBox.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 등급 옵션 — 잠긴 줄은 회색, 범위가 없는 줄은 범위를 감춘다.
		public void RenderGradeOptions(IReadOnlyList<GradeOptionLine> lines)
		{
			int shown = 0;
			for (int i = 0; i < lines.Count; i++)
			{
				GradeOptionLine data = lines[i];
				if (data.hasOption == false)
				{
					continue;
				}

				GradeLine line = getGradeLine(shown);
				shown++;
				line.root.style.display = DisplayStyle.Flex;

				if (_gradeColors != null)
				{
					ItemGradeColorTable.GradeColor gc = _gradeColors.Get(data.grade);
					line.mark.style.backgroundColor = gc.bg;
					line.mark.style.borderTopColor = gc.border;
					line.mark.style.borderBottomColor = gc.border;
					line.mark.style.borderLeftColor = gc.border;
					line.mark.style.borderRightColor = gc.border;
				}

				line.text.text = data.text;
				line.text.style.color = data.unlocked ? new StyleColor(StyleKeyword.Null) : new StyleColor(LockedColor);
				line.range.style.display = data.hasRange ? DisplayStyle.Flex : DisplayStyle.None;
				line.range.text = data.rangeText;
				line.range.style.color = data.unlocked ? new StyleColor(StyleKeyword.Null) : new StyleColor(LockedColor);
			}

			for (int i = shown; i < _gradeLines.Count; i++)
			{
				_gradeLines[i].root.style.display = DisplayStyle.None;
			}

			_gradeBox.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 레벨은 위에 따로 적으므로 칸의 레벨 표시는 끈다. 장착 여부는 아래 장착/장착해제 버튼이 알려 주므로 칸의 장착 표시도 쓰지 않는다.
		public async UniTask BindItemSlotAsync(EquipmentInstance instance, CancellationToken ct)
		{
			await _cell.BindEquipmentAsync(instance, false, ct);
			_cell.HideLevel();
		}

		public void SetEquipInteractable(bool interactable)
		{
			_equip.SetEnabled(interactable);
		}

		public void SetEquipLabel(string label)
		{
			_equip.text = label;
		}

		public void SetEnchantInteractable(bool interactable)
		{
			_enchant.SetEnabled(interactable);
		}

		public void SetEnchantLabel(string label)
		{
			_enchant.text = label;
		}

		// 보관 버튼은 창 밖 아래에 붙는다 — 보일 때는 창을 그만큼 올려 덩어리 전체를 화면 가운데에 둔다.
		public void SetStashVisible(bool visible)
		{
			setShown(_stash, visible);
			_window.EnableInClassList("popup__window--stash", visible);
		}

		public void SetStashInteractable(bool interactable)
		{
			_stash.SetEnabled(interactable);
		}

		public void SetStashLabel(string label)
		{
			_stash.text = label;
		}

		public void SetDecompositionVisible(bool visible)
		{
			setShown(_decompose, visible);
		}

		public void SetLockVisible(bool visible)
		{
			setShown(_lock, visible);
		}

		public void SetLocked(bool locked)
		{
			_lock.EnableInClassList("btn--green", locked);
			_lock.EnableInClassList("btn--gray", locked == false);
			_cell.SetLocked(locked);
		}

		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// ── 내부 ─────────────────────────────────────────────────────────

		private BasicLine getBasicLine(int index)
		{
			if (index < _basicLines.Count)
			{
				return _basicLines[index];
			}

			BasicLine line = new BasicLine();
			line.root = new VisualElement();
			line.root.AddToClassList("option-line");
			line.icon = new VisualElement();
			line.icon.AddToClassList("option-line__icon");
			line.text = new Label();
			line.text.AddToClassList("option-line__text");
			line.root.Add(line.icon);
			line.root.Add(line.text);
			_basicBox.Add(line.root);

			_basicLines.Add(line);
			return line;
		}

		private GradeLine getGradeLine(int index)
		{
			if (index < _gradeLines.Count)
			{
				return _gradeLines[index];
			}

			GradeLine line = new GradeLine();
			line.root = new VisualElement();
			line.root.AddToClassList("option-line");
			line.mark = new VisualElement();
			line.mark.AddToClassList("option-line__mark");
			line.text = new Label();
			line.text.AddToClassList("option-line__text");
			line.range = new Label();
			line.range.AddToClassList("option-line__range");
			line.root.Add(line.mark);
			line.root.Add(line.text);
			line.root.Add(line.range);
			_gradeBox.Add(line.root);

			_gradeLines.Add(line);
			return line;
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// Pretendard 에서 글자가 차지하는 폭(한글 1칸 기준) — 공백은 0.3칸, 영숫자·기호는 0.6칸
		private static float getTitleWidth(string text)
		{
			float width = 0f;
			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				if (ch == ' ')
				{
					width += 0.3f;
				}
				else if (ch < 0x1100)
				{
					width += 0.6f;
				}
				else
				{
					width += 1f;
				}
			}

			return width;
		}

		private void onEquipClicked()
		{
			if (OnEquipToggleClicked != null) { OnEquipToggleClicked.Invoke(); }
		}

		private void onEnchantClicked()
		{
			if (OnEnchantClicked != null) { OnEnchantClicked.Invoke(); }
		}

		private void onStashClicked()
		{
			if (OnStashClicked != null) { OnStashClicked.Invoke(); }
		}

		private void onDecompositionClicked()
		{
			if (OnDecompositionClicked != null) { OnDecompositionClicked.Invoke(); }
		}

		private void onLockClicked()
		{
			if (OnLockClicked != null) { OnLockClicked.Invoke(); }
		}

		private void onExitClicked()
		{
			if (OnExitClicked != null) { OnExitClicked.Invoke(); }
		}

		private void onDimClicked(ClickEvent evt)
		{
			onExitClicked();
		}
	}
}
