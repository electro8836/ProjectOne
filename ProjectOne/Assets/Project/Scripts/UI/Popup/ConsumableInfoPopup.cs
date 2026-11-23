using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;

namespace ProjectOne.UI
{
	// 소모품 정보 팝업(UIPrefab_ConsumablePopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/ItemInfo/ConsumablePopup.uxml.
	// 등급색 제목 줄(이름·등급 박스)과 아이템 칸·보유 수·사용 효과, 아래 조작 띠(수량 +/-·사용), 오른쪽 위 삭제를 둔다.
	// 읽기 전용이면 조작 띠와 삭제를 감춘다.
	// 무엇을 할지는 ConsumableInfoPresenter 가 정한다.
	public class ConsumableInfoPopup : ToolkitScreen, IView
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		public event Action OnPlusClicked;
		public event Action OnMinusClicked;
		public event Action OnUseClicked;
		public event Action OnDeleteClicked;
		public event Action OnExitClicked;

		private readonly ConsumableInfoPresenter _presenter = new ConsumableInfoPresenter();
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private VisualElement _head;
		private Label _name;
		private Label _grade;
		private Label _owned;
		private Label _desc;
		private Label _count;
		private VisualElement _control;
		private Button _plus;
		private Button _minus;
		private Button _use;
		private Button _delete;
		private ItemCellView _cell;

		private bool _readOnly;
		private UniTaskCompletionSource<bool> _tcs;

		protected override void OnBind(VisualElement root)
		{
			_head = root.Q("head");
			_name = root.Q<Label>("name");
			_grade = root.Q<Label>("grade");
			_owned = root.Q<Label>("owned");
			_desc = root.Q<Label>("desc");
			_count = root.Q<Label>("count");
			_control = root.Q("control");
			_cell = new ItemCellView(root.Q("cell"), _gradeColors, _icons);

			_plus = root.Q<Button>("plus");
			_plus.clicked += onPlusClicked;
			_minus = root.Q<Button>("minus");
			_minus.clicked += onMinusClicked;
			_use = root.Q<Button>("use");
			_use.clicked += onUseClicked;
			_delete = root.Q<Button>("delete");
			_delete.clicked += onDeleteClicked;

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

		public UniTask ShowAsync(int itemId, bool readOnly, CancellationToken ct)
		{
			return _presenter.ShowAsync(itemId, readOnly, ct);
		}

		public void SetReadOnly(bool readOnly)
		{
			_readOnly = readOnly;
			bool usable = (readOnly == false);
			_control.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
			_delete.style.display = usable ? DisplayStyle.Flex : DisplayStyle.None;
			applyControlsInteractable(usable, usable, usable, usable);
		}

		public void Reveal()
		{
			PlayPopupOpen();
		}

		public void SetInfo(Table_Item.Row row)
		{
			_name.text = row.Name;
			_grade.text = ItemGradeNames.Get(row.Grade);
			if (_gradeColors != null)
			{
				// 장비 정보 팝업과 같이 — 제목 판·아래 선·등급 박스를 등급색으로
				ItemGradeColorTable.GradeColor colors = _gradeColors.Get(row.Grade);
				_head.style.backgroundColor = colors.bg;
				_head.style.borderBottomColor = colors.border;
				_grade.style.backgroundColor = colors.border;
			}

			_desc.text = row.Desc;
		}

		public UniTask BindItemSlotAsync(Table_Item.Row row, int ownedCount, CancellationToken ct)
		{
			_owned.text = "보유 " + ownedCount.ToString() + "개";
			return _cell.BindItemAsync(row, ownedCount, ct);
		}

		public void SetCount(int count)
		{
			_count.text = count.ToString();
		}

		public void SetControlsInteractable(bool plus, bool minus, bool use, bool delete)
		{
			applyControlsInteractable(plus, minus, use, delete);
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

		// 읽기 전용이면 무엇이 와도 잠근다.
		private void applyControlsInteractable(bool plus, bool minus, bool use, bool delete)
		{
			bool allowed = (_readOnly == false);
			_plus.SetEnabled(plus && allowed);
			_minus.SetEnabled(minus && allowed);
			_use.SetEnabled(use && allowed);
			_delete.SetEnabled(delete && allowed);
		}

		private void onPlusClicked()
		{
			if (OnPlusClicked != null) { OnPlusClicked.Invoke(); }
		}

		private void onMinusClicked()
		{
			if (OnMinusClicked != null) { OnMinusClicked.Invoke(); }
		}

		private void onUseClicked()
		{
			if (OnUseClicked != null) { OnUseClicked.Invoke(); }
		}

		private void onDeleteClicked()
		{
			if (OnDeleteClicked != null) { OnDeleteClicked.Invoke(); }
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
