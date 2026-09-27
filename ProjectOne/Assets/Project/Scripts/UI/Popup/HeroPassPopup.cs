using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.HeroPasses;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 히어로패스 팝업의 View(MVP). UIManager.ShowHeroPassPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 레벨별 슬롯은 테이블 레벨 수만큼 Awake 에서 한 번 만들어 두고, 렌더마다 값만 다시 채운다.
	// 무엇을 받을 수 있는지는 HeroPassPopupPresenter 가 정한다.
	public class HeroPassPopup : UIScreen, IView
	{
		[Header("상단")]
		[SerializeField] private TMP_Text _levelText;			// PassLevel/LevelText
		[SerializeField] private Slider _startSlider;			// Slider_Start — 1레벨 도달 전 0, 이후 1
		[SerializeField] private TMP_Text _stateText;			// StateText — 패스 구매 여부
		[SerializeField] private TMP_Text _remainTimeText;		// RemainTimeText
		[SerializeField] private Image _passLineImage;			// ScrollRect/PassLine — 패스 구매 여부로 스프라이트가 바뀐다
		[SerializeField] private Sprite _passLineActiveSprite;	// BaseFrame_Convex_Tapered_01_Yellow
		[SerializeField] private Sprite _passLineInactiveSprite;	// BaseFrame_Convex_Tapered_01_Gray
		[SerializeField] private Image _bgRightImage;			// Frame/BgRight — 패스 구매 여부로 색이 바뀐다
		[SerializeField] private Color _bgRightActiveColor;		// #6437A0
		[SerializeField] private Color _bgRightInactiveColor;	// #2C213A

		[Header("진행")]
		[SerializeField] private Slider _progressSlider;		// PassProgress
		[SerializeField] private TMP_Text _progressText;		// ProgressText

		[Header("슬롯")]
		[SerializeField] private ScrollRect _scrollRect;		// Frame/ScrollRect
		[SerializeField] private RectTransform _slotContent;	// ScrollRect/Viewport/Content
		[SerializeField] private HeroPassRewardSlot _slotPrefab;	// UIPrefab_PassRewardSlot

		[Header("버튼")]
		[SerializeField] private UIButton _allReceiveButton;	// AllReceiveButton
		[SerializeField] private UIButton _exitButton;			// ExitButton
		[SerializeField] private UIButton _dimButton;			// Dim

		private readonly HeroPassPopupPresenter _presenter = new HeroPassPopupPresenter();
		private readonly List<HeroPassRewardSlot> _slots = new List<HeroPassRewardSlot>(50);
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

		// 남은 시간 갱신 틱. 구독자는 Presenter 다.
		public event Action OnSecondTick;

		// 받을 수 있는 칸을 눌렀다 — (레벨, 추가 보상인가).
		public event Action<int, bool> OnClaimRequested;

		// 모두 받기를 눌렀다.
		public event Action OnAllReceiveRequested;

		private void Awake()
		{
			// 첫 렌더가 끝나기 전 빈 칸이 비치지 않도록 숨겨 두고 시작한다.
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			setVisible(false);
			createSlots();

			if (_exitButton != null)
			{
				_exitButton.OnClickEvent += onCloseClicked;
			}

			if (_dimButton != null)
			{
				_dimButton.OnClickEvent += onCloseClicked;
			}

			if (_allReceiveButton != null)
			{
				_allReceiveButton.OnClickEvent += onAllReceiveClicked;
			}

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			if (_exitButton != null)
			{
				_exitButton.OnClickEvent -= onCloseClicked;
			}

			if (_dimButton != null)
			{
				_dimButton.OnClickEvent -= onCloseClicked;
			}

			if (_allReceiveButton != null)
			{
				_allReceiveButton.OnClickEvent -= onAllReceiveClicked;
			}

			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i] != null)
				{
					_slots[i].OnClaimClicked -= onSlotClaimClicked;
				}
			}

			_presenter.Dispose();
		}

		// 남은 시간을 1초마다 다시 그리기 위한 틱. 오브젝트가 파괴되면 코루틴도 함께 멈춘다.
		private void OnEnable()
		{
			StartCoroutine(tickRoutine());
		}

		private IEnumerator tickRoutine()
		{
			WaitForSeconds wait = new WaitForSeconds(1f);
			while (true)
			{
				if (OnSecondTick != null)
				{
					OnSecondTick.Invoke();
				}

				yield return wait;
			}
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

		public void SetHeader(int level, bool isPurchased)
		{
			if (_levelText != null)
			{
				_levelText.text = level.ToString();
			}

			if (_startSlider != null)
			{
				_startSlider.value = (level >= 1) ? 1f : 0f;
			}

			if (_stateText != null)
			{
				_stateText.text = isPurchased ? "활성화됨" : "비활성화";
			}

			if (_passLineImage != null)
			{
				_passLineImage.sprite = isPurchased ? _passLineActiveSprite : _passLineInactiveSprite;
			}

			if (_bgRightImage != null)
			{
				_bgRightImage.color = isPurchased ? _bgRightActiveColor : _bgRightInactiveColor;
			}
		}

		public void SetProgress(float ratio, string text)
		{
			if (_progressSlider != null)
			{
				_progressSlider.value = ratio;
			}

			if (_progressText != null)
			{
				_progressText.text = text;
			}
		}

		// 받을 보상이 없으면 끈다. 비활성 틴트와 클릭 차단은 UIButton 이 맡는다.
		public void SetAllReceiveInteractable(bool value)
		{
			if (_allReceiveButton != null)
			{
				_allReceiveButton.interactable = value;
			}
		}

		public void SetRemainTimeText(string text)
		{
			if (_remainTimeText != null)
			{
				_remainTimeText.text = text;
			}
		}

		// data 는 1레벨부터 순서대로다.
		public async UniTask RenderSlotsAsync(IReadOnlyList<HeroPassRewardSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();

			for (int i = 0; i < _slots.Count; i++)
			{
				HeroPassRewardSlot slot = _slots[i];
				if (i >= data.Count)
				{
					slot.gameObject.SetActive(false);
					continue;
				}

				slot.gameObject.SetActive(true);
				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// index 칸이 뷰포트 맨 위에 오도록 스크롤한다. 끝 칸이라 더 내릴 수 없으면 바닥에서 멈춘다.
		// ActListPopup.ScrollToSlot 과 같은 방식이다.
		public void ScrollToSlot(int index)
		{
			if (_scrollRect == null || index < 0 || index >= _slots.Count)
			{
				return;
			}

			// 방금 채운 슬롯은 레이아웃이 아직 안 잡혀 있다 — 위치를 읽기 전에 즉시 재배치한다.
			RectTransform content = _scrollRect.content;
			LayoutRebuilder.ForceRebuildLayoutImmediate(content);

			RectTransform viewport = _scrollRect.viewport != null ? _scrollRect.viewport : (RectTransform)_scrollRect.transform;
			float scrollable = content.rect.height - viewport.rect.height;
			if (scrollable <= 0f)
			{
				_scrollRect.verticalNormalizedPosition = 1f;
				return;
			}

			// 콘텐츠 윗변에서 슬롯 윗변까지의 거리 — 스케일이 걸려 있어도 콘텐츠 로컬 좌표로 잰다.
			RectTransform slot = (RectTransform)_slots[index].transform;
			Vector3 slotTop = content.InverseTransformPoint(slot.TransformPoint(new Vector3(0f, slot.rect.yMax, 0f)));
			float fromTop = content.rect.yMax - slotTop.y;

			_scrollRect.StopMovement();
			_scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(fromTop / scrollable);
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

		private void createSlots()
		{
			_slots.Clear();

			if (_slotContent == null || _slotPrefab == null)
			{
				Debug.LogWarning("[HeroPassPopup] 슬롯 Content 또는 슬롯 프리펩이 연결되지 않았다.");
				return;
			}

			int count = HeroPassCatalog.MaxLevel;
			for (int i = 0; i < count; i++)
			{
				HeroPassRewardSlot slot = Instantiate(_slotPrefab, _slotContent);
				slot.OnClaimClicked += onSlotClaimClicked;
				_slots.Add(slot);
			}
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void onSlotClaimClicked(HeroPassRewardSlot slot, bool advanced)
		{
			if (OnClaimRequested != null)
			{
				OnClaimRequested.Invoke(slot.Level, advanced);
			}
		}

		private void onAllReceiveClicked()
		{
			if (OnAllReceiveRequested != null)
			{
				OnAllReceiveRequested.Invoke();
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
