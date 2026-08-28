using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;

namespace ProjectOne.UI
{
	// 소모품 정보 팝업의 View(MVP). UIManager.ShowConsumablePopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 정보 표시와 입력 전달만 담당하고, 수량 계산·사용/파괴 결정은 ConsumableInfoPresenter 가 한다.
	public class ConsumableInfoPopup : UIScreen, IView
	{
		[Header("정보 텍스트")]
		[SerializeField] private TMP_Text _nameText;	// NameText
		[SerializeField] private TMP_Text _gradeText;	// GradeText
		[SerializeField] private TMP_Text _descText;	// DescText
		[SerializeField] private TMP_Text _countText;	// CountText — 선택 수량

		[Header("등급 색상 대상")]
		[SerializeField] private Image _topBg;	// TopBg — 등급 bg
		[SerializeField] private Image _deco2;	// Deco2 — 등급 border

		[Header("슬롯")]
		[SerializeField] private Transform _itemSlotRoot;	// ItemSlotRoot

		[Header("버튼")]
		[SerializeField] private UIButton _plusButton;		// PlusButton
		[SerializeField] private UIButton _minusButton;		// MinusButton
		[SerializeField] private UIButton _useButton;		// UseButton
		[SerializeField] private UIButton _deleteButton;	// DeleteButton

		// 닫기는 ExitButton 과 Dimmed 두 경로다.
		// 본문 영역은 ItemInfo/Bg 가 레이캐스트를 흡수하므로, Dimmed 까지 내려오는 클릭은
		// 곧 "팝업 밖을 눌렀다"는 뜻이다.
		[SerializeField] private UIButton _exitButton;
		[SerializeField] private UIButton _dimmedButton;

		[Header("프리펩 / 데이터")]
		[SerializeField] private ItemSlot _slotPrefab;				// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnPlusClicked;
		public event Action OnMinusClicked;
		public event Action OnUseClicked;
		public event Action OnDeleteClicked;
		public event Action OnExitClicked;

		private readonly ConsumableInfoPresenter _presenter = new ConsumableInfoPresenter();
		private UniTaskCompletionSource<bool> _tcs;

		// ItemSlotRoot 에 만든 슬롯 — 팝업 1회 표시에 하나만 쓴다.
		private ItemSlot _itemSlot;

		// 첫 렌더(아이콘 로드)가 끝날 때까지 숨겼다가 한 번에 보여주기 위한 그룹
		private CanvasGroup _canvasGroup;

		private void Awake()
		{
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			// 로드 완료 전까지 숨김 — Reveal 에서 보여준다.
			setVisible(false);

			_plusButton.OnClickEvent += onPlusClicked;
			_minusButton.OnClickEvent += onMinusClicked;
			_useButton.OnClickEvent += onUseClicked;
			_deleteButton.OnClickEvent += onDeleteClicked;
			_exitButton.OnClickEvent += onExitClicked;
			_dimmedButton.OnClickEvent += onExitClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_plusButton.OnClickEvent -= onPlusClicked;
			_minusButton.OnClickEvent -= onMinusClicked;
			_useButton.OnClickEvent -= onUseClicked;
			_deleteButton.OnClickEvent -= onDeleteClicked;
			_exitButton.OnClickEvent -= onExitClicked;
			_dimmedButton.OnClickEvent -= onExitClicked;
		}

		// UIManager 가 인스턴스화 직후 호출해 팝업이 닫힐 때까지 기다린다.
		public UniTask ShowAsync(int itemId, CancellationToken ct)
		{
			return _presenter.ShowAsync(itemId, ct);
		}

		// Presenter 가 첫 렌더(아이콘 로드)를 끝낸 뒤 호출 — 채워진 상태로 한 번에 표시.
		public void Reveal()
		{
			setVisible(true);
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// 소모품은 인스턴스가 없어 이름·등급·설명이 전부 아이템 테이블 값이다.
		public void SetInfo(Table_Item.Row row)
		{
			applyGradeColor(row.Grade);
			_nameText.text = row.Name;
			_gradeText.text = ItemGradeNames.Get(row.Grade);
			_descText.text = row.Desc;
		}

		// ItemSlotRoot 에 아이템 슬롯을 만들어 등급/아이콘/보유 수량을 표시한다.
		// 슬롯에 찍히는 개수는 팝업 하단의 선택 수량이 아니라 인벤토리와 같은 보유 수량이다.
		public async UniTask BindItemSlotAsync(Table_Item.Row row, int ownedCount, CancellationToken ct)
		{
			if (_itemSlot == null)
			{
				_itemSlot = Instantiate(_slotPrefab, _itemSlotRoot);
			}

			await _itemSlot.BindItemAsync(row, ownedCount, _gradeColors, ct);
		}

		// 선택 수량 표시.
		public void SetCount(int count)
		{
			_countText.text = count.ToString();
		}

		// 조작 버튼 활성/비활성. 숨기지 않고 interactable 만 끈다(회색 틴트는 UIButton 이 처리).
		public void SetControlsInteractable(bool plus, bool minus, bool use, bool delete)
		{
			_plusButton.interactable = plus;
			_minusButton.interactable = minus;
			_useButton.interactable = use;
			_deleteButton.interactable = delete;
		}

		// 닫힘 대기 — Presenter 의 ShowAsync 가 마지막에 await 한다.
		public async UniTask WaitForCloseAsync(CancellationToken ct)
		{
			_tcs = new UniTaskCompletionSource<bool>();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 입력(Exit)으로 닫힘을 확정한다.
		public void CloseFromInput()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(true);
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void applyGradeColor(ItemGradeType grade)
		{
			if (_gradeColors == null)
			{
				return;
			}

			ItemGradeColorTable.GradeColor gc = _gradeColors.Get(grade);
			_topBg.color = gc.bg;
			_deco2.color = gc.border;
			_gradeText.color = gc.text;
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
	}
}
