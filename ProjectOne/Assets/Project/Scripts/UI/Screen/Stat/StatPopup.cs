using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectOne.UI
{
	// 내 캐릭터 능력치 팝업의 View(MVP). UIManager.ShowStatPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 공격/방어/유틸 세 그룹으로 나뉘고, 각 그룹의 Grid 에 StatSlot 을 필요한 만큼만 깐다.
	// 무엇을 어떤 문구로 깔지는 StatPopupPresenter 가 정한다.
	public class StatPopup : UIScreen, IView
	{
		[Header("그룹별 목록")]
		[SerializeField] private RectTransform _offensiveGrid;	// OffensiveStatGroup/.../Grid
		[SerializeField] private RectTransform _defensiveGrid;	// DefensiveStatGroup/.../Grid
		[SerializeField] private RectTransform _utilityGrid;		// UtilityStatGroup/.../Grid
		[SerializeField] private StatSlot _slotPrefab;			// UIPrefab_StatSlot

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;	// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;	// Dim

		private readonly StatPopupPresenter _presenter = new StatPopupPresenter();

		private readonly List<StatSlot> _offensiveSlots = new List<StatSlot>();
		private readonly List<StatSlot> _defensiveSlots = new List<StatSlot>();
		private readonly List<StatSlot> _utilitySlots = new List<StatSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

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

		// 세 그룹을 한 번에 그린다 — 아이콘 로드가 await 라 한꺼번에 던지고 마지막에 모아서 기다린다.
		public async UniTask RenderAsync(IReadOnlyList<StatSlotData> offensive, IReadOnlyList<StatSlotData> defensive,
			IReadOnlyList<StatSlotData> utility, CancellationToken ct)
		{
			_bindTasks.Clear();

			renderGroup(_offensiveSlots, _offensiveGrid, offensive, ct);
			renderGroup(_defensiveSlots, _defensiveGrid, defensive, ct);
			renderGroup(_utilitySlots, _utilityGrid, utility, ct);

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
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

		// 슬롯은 파괴하지 않고 재사용한다 — 남는 칸은 끄기만 한다.
		private void renderGroup(List<StatSlot> slots, RectTransform parent, IReadOnlyList<StatSlotData> data, CancellationToken ct)
		{
			for (int i = 0; i < data.Count; i++)
			{
				StatSlot slot = getOrCreateSlot(slots, parent, i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < slots.Count; i++)
			{
				slots[i].gameObject.SetActive(false);
			}
		}

		private StatSlot getOrCreateSlot(List<StatSlot> slots, RectTransform parent, int index)
		{
			if (index < slots.Count)
			{
				return slots[index];
			}

			StatSlot slot = Instantiate(_slotPrefab, parent);
			slots.Add(slot);

			return slot;
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
