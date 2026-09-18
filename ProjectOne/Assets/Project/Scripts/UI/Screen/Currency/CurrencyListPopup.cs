using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.UI
{
	// 보유 재화 목록 팝업의 View(MVP). UIManager.ShowCurrencyListPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 장비 창 상단의 재화 버튼으로 열린다. 재화 종류는 테이블이 소유하므로
	// 무엇을 몇 칸 깔지는 CurrencyListPopupPresenter 가 정하고, 여기서는 Grid 에 깔기만 한다.
	public class CurrencyListPopup : UIScreen, IView
	{
		[Header("목록")]
		[SerializeField] private RectTransform _gridParent;		// ScrollRect/Veiwport/Content/Grid
		[SerializeField] private CurrencyInfoSlot _slotPrefab;	// UIPrefab_CurrencyInfoSlot

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;	// Frame/Top/ExitButton
		[SerializeField] private UIButton _dimButton;	// Dim

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<EDT.Currency, RectTransform> OnSlotClicked;

		private readonly CurrencyListPopupPresenter _presenter = new CurrencyListPopupPresenter();

		private readonly List<CurrencyInfoSlot> _slots = new List<CurrencyInfoSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private int _visibleCount;	// 켜져 있는 슬롯 수 — 수량 갱신 시 꺼진 칸을 건드리지 않기 위해

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			// 첫 렌더가 끝나기 전 빈 칸이 비치지 않도록 숨겨 두고 시작한다.
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			setVisible(false);

			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dim 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnClicked -= onSlotClicked;
			}

			_presenter.Dispose();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(CancellationToken ct)
		{
			await _presenter.ShowAsync(ct);

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// 슬롯은 파괴하지 않고 재사용한다 — 남는 칸은 끄기만 한다.
		public async UniTask RenderAsync(IReadOnlyList<CurrencySlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < data.Count; i++)
			{
				CurrencyInfoSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			_visibleCount = data.Count;

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 재화 변동 — 해당 재화의 칸 하나만 갈아 끼운다(다시 그리면 아이콘까지 재로드된다).
		public void UpdateAmount(EDT.Currency currency, int amount)
		{
			for (int i = 0; i < _visibleCount; i++)
			{
				if (_slots[i].TargetCurrency != currency)
				{
					continue;
				}

				_slots[i].SetAmount(amount);
				return;
			}
		}

		public void Reveal()
		{
			setVisible(true);
		}

		public void Close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private CurrencyInfoSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			CurrencyInfoSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onSlotClicked;	// 생성 시 1회만 — 해제는 OnDestroy 에서 한꺼번에
			_slots.Add(slot);

			return slot;
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void onSlotClicked(CurrencyInfoSlot sender)
		{
			if (OnSlotClicked != null)
			{
				OnSlotClicked.Invoke(sender.TargetCurrency, sender.transform as RectTransform);
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
