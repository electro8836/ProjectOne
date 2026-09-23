using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 퀘스트 목록 팝업의 View(MVP). UIManager.ShowQuestListPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 퀘스트 체인 전체를 ID 순으로 깔기만 한다 — 어떤 칸이 진행 중이고 어디까지 깼는지는
	// QuestListPopupPresenter 가 정한다. 수령은 이 팝업의 일이 아니다(QuestInfo 가 맡는다).
	public class QuestListPopup : UIScreen, IView
	{
		[Header("목록")]
		[SerializeField] private ScrollRect _scrollRect;			// Frame/ScrollRect
		[SerializeField] private RectTransform _grid;				// Frame/ScrollRect/Viewport/Content/Grid
		[SerializeField] private QuestSlot _slotPrefab;				// UIPrefab_QuestSlot
		[SerializeField] private ItemSlot _itemSlotPrefab;			// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("닫기")]
		[SerializeField] private UIButton _exitButton;				// Frame/ExitButton
		[SerializeField] private UIButton _dimButton;				// Dim

		private readonly QuestListPopupPresenter _presenter = new QuestListPopupPresenter();
		private readonly List<QuestSlot> _slots = new List<QuestSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

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

		// 슬롯은 파괴하지 않고 재사용한다 — 다시 열 때마다 Instantiate 하면 GC 가 튄다.
		// 스크롤은 건드리지 않는다 — 위치를 맞추는 것은 FocusSlot 의 몫이다.
		public async UniTask RenderAsync(IReadOnlyList<QuestSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < data.Count; i++)
			{
				QuestSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], _itemSlotPrefab, _gradeColors, ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// index 칸을 뷰포트 맨 위로 올린다(진행 중인 퀘스트). 없으면 -1 — 목록 맨 위에서 시작한다.
		//
		// 팝업을 처음 열 때 한 번만 부른다. 떠 있는 동안에는 사용자가 잡아 둔 스크롤 위치를
		// 건드리지 않는다 — 몬스터를 잡을 때마다 목록이 다시 그려져도 화면이 튀지 않아야 한다.
		//
		// Grid 가 Content 아래 한 단계 더 들어가 있어 anchoredPosition 만으로는 위치를 낼 수 없다 —
		// 슬롯 윗변을 월드 좌표로 옮겼다가 Content 로컬로 되돌린다. Content pivot 이 (0.5, 1) 이라
		// 맨 위가 0, 아래로 갈수록 음수다.
		public void FocusSlot(int index)
		{
			if (_scrollRect == null)
			{
				return;
			}

			if (index < 0 || index >= _slots.Count)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
				return;
			}

			// 방금 켠 칸은 레이아웃이 아직 확정되지 않았다 — 크기를 확정해야 위치를 잴 수 있다.
			Canvas.ForceUpdateCanvases();

			RectTransform content = _scrollRect.content;
			RectTransform viewport = _scrollRect.viewport;
			if (content == null || viewport == null)
			{
				return;
			}

			float scrollable = content.rect.height - viewport.rect.height;
			if (scrollable <= 0f)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
				return;
			}

			// 첫 칸을 기준으로 삼는다 — Content 의 위쪽 패딩만큼 통째로 밀리는 것을 상쇄해,
			// 0번을 겨냥하면 정확히 목록 맨 위(1f)가 되고 그 아래 칸들도 같은 여백을 유지한다.
			float offset = contentTopOf(_slots[index]) - contentTopOf(_slots[0]);

			_scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - (offset / scrollable));
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

		private QuestSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			QuestSlot slot = Instantiate(_slotPrefab, _grid);
			_slots.Add(slot);

			return slot;
		}

		// 슬롯 윗변이 Content 위쪽 끝에서 얼마나 내려와 있는지.
		private float contentTopOf(QuestSlot slot)
		{
			RectTransform rect = slot.transform as RectTransform;
			Vector3 worldTop = rect.TransformPoint(new Vector3(0f, rect.rect.yMax, 0f));

			return -_scrollRect.content.InverseTransformPoint(worldTop).y;
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
