using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;

namespace ProjectOne.UI
{
	// 액트·필드 목록 팝업의 View(MVP). UIManager.ShowActListPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 표시와 입력 전달만 담당하고, 어느 액트를 열지·어디로 갈 수 있는지는 ActListPopupPresenter 가 정한다.
	public class ActListPopup : UIScreen, IView
	{
		[Header("탭")]
		[SerializeField] private TabGroup _tabGroup;			// Frame/Top/ActTabButtons/Veiwport/Content/Grid
		[SerializeField] private UITabButton[] _actTabs;		// ActTabButton_01 ~ 05 (Hierarchy 순서와 일대일)
		[SerializeField] private TMP_Text _actNameText;		// Frame/Top/ActName

		[Header("필드 목록")]
		[SerializeField] private RectTransform _fieldGrid;	// Frame/FieldScrollRect/Viewport/Content/Grid
		[SerializeField] private FieldSlot _slotPrefab;		// UIPrefab_FieldSlot

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;		// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;		// Dim

		public event Action<int> OnTabSelected;		// 탭 인덱스
		public event Action<int> OnMoveRequested;	// 목적지 Field.ID

		private readonly ActListPopupPresenter _presenter = new ActListPopupPresenter();
		private readonly List<FieldSlot> _slots = new List<FieldSlot>();

		// 결과는 "이동했는가" 다. 이동했다면 월드 창까지 닫아야 해서 호출부가 알아야 한다.
		private UniTaskCompletionSource<bool> _tcs;

		private void Awake()
		{
			_tabGroup.OnTabChanged += onTabChanged;
			_exitButton.OnClickEvent += onCloseClicked;
			_dimButton.OnClickEvent += onCloseClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnMoveClicked -= onSlotMoveClicked;
			}

			_tabGroup.OnTabChanged -= onTabChanged;
			_exitButton.OnClickEvent -= onCloseClicked;
			_dimButton.OnClickEvent -= onCloseClicked;

			_presenter.Dispose();
		}

		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// UIManager 가 인스턴스화 직후 호출한다. 팝업이 닫힐 때까지 돌아오지 않는다.
		// 돌려주는 값은 이동으로 닫혔는지 여부다 — 취소로 끝나면 이동하지 않은 것으로 본다.
		public async UniTask<bool> ShowAsync(CancellationToken ct)
		{
			await _presenter.OnOpenAsync(ct);

			_tcs = new UniTaskCompletionSource<bool>();

			(bool cancelled, bool moved) = await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return false;
			}

			return moved;
		}

		// ── Presenter → View ──────────────────────────────────────────────

		// 데이터가 있는 액트만 남기고 나머지 탭은 끈다. 액트가 늘면 데이터만 늘리면 된다.
		public void SetTabCount(int count)
		{
			for (int i = 0; i < _actTabs.Length; i++)
			{
				_actTabs[i].gameObject.SetActive(i < count);
			}
		}

		public void SelectTab(int index)
		{
			_tabGroup.Select(index);
		}

		public void SetActName(string text)
		{
			_actNameText.text = text;
		}

		// 슬롯은 파괴하지 않고 재사용한다 — 탭을 오갈 때마다 Instantiate 하면 GC 가 튄다.
		public void RenderFields(IReadOnlyList<FieldSlotData> data)
		{
			for (int i = 0; i < data.Count; i++)
			{
				FieldSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);
				slot.Bind(data[i]);
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}
		}

		// 그냥 닫기 — 닫기 버튼·딤을 눌렀을 때.
		public void Close()
		{
			close(false);
		}

		// 이동으로 닫기 — 호출부가 월드 창까지 닫도록 결과에 실어 보낸다.
		public void CloseByMove()
		{
			close(true);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private FieldSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			FieldSlot slot = Instantiate(_slotPrefab, _fieldGrid);
			slot.OnMoveClicked += onSlotMoveClicked;
			_slots.Add(slot);

			return slot;
		}

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke(index);
			}
		}

		private void onSlotMoveClicked(FieldSlot sender, int fieldId)
		{
			if (OnMoveRequested != null)
			{
				OnMoveRequested.Invoke(fieldId);
			}
		}

		private void onCloseClicked()
		{
			Close();
		}

		private void close(bool moved)
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult(moved);
			}
		}
	}
}
