using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.DailyBonuses;
using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 출석 팝업의 View(MVP). UIManager.ShowDailyBonusPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	//
	// 주간(7칸)과 월간(30칸)은 같은 슬롯 프리펩을 쓰지만 배치가 달라 그룹을 통째로 바꿔 끼운다.
	// 어느 칸이 수령 완료이고 어느 칸이 오늘인지는 DailyBonusPopupPresenter 가 정한다.
	public class DailyBonusPopup : UIScreen, IView
	{
		// 표시 종류별 Frame 높이. 월간은 30칸 격자가 들어가 더 길다.
		private const float WEEK_FRAME_HEIGHT = 1100f;
		private const float MONTH_FRAME_HEIGHT = 1330f;

		[Header("틀")]
		[SerializeField] private RectTransform _frame;		// Frame
		[SerializeField] private TMP_Text _titleText;		// Frame/Title/TitleText
		[SerializeField] private TMP_Text _timeText;		// Frame/TimeText

		[Header("탭")]
		[SerializeField] private TabGroup _tabGroup;		// TabButtons
		// 탭 인덱스는 계층 순서라 인스펙터에서 순서를 바꾸면 주간/월간이 뒤집힌다 —
		// 버튼을 직접 참조해 인덱스를 런타임에 찾는다.
		[SerializeField] private UITabButton _weekTab;		// TabButtons/Button_WeekReward

		[Header("주간")]
		[SerializeField] private GameObject _weekGroup;		// Frame/WeekGroup — 자식 순서가 곧 일차다

		[Header("월간")]
		[SerializeField] private GameObject _monthGroup;	// Frame/MonthGroup
		[SerializeField] private RectTransform _monthGrid;	// Frame/MonthGroup/Grid — 자식 순서가 곧 일차다

		[Header("닫기")]
		[SerializeField] private UIButton _dimButton;		// Dimmed — 이 팝업엔 별도 닫기 버튼이 없다

		private readonly DailyBonusPopupPresenter _presenter = new DailyBonusPopupPresenter();
		private readonly List<DailyBonusSlot> _weekSlots = new List<DailyBonusSlot>(7);
		private readonly List<DailyBonusSlot> _monthSlots = new List<DailyBonusSlot>(30);
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		private CanvasGroup _canvasGroup;
		private UniTaskCompletionSource _tcs;

		// _weekTab 이 TabGroup 안에서 몇 번째인가. 나머지 하나가 월간이다.
		private int _weekTabIndex;

		// 남은 시간 갱신 틱. 구독자는 Presenter 다.
		public event Action OnSecondTick;

		// 어느 출석을 보겠다고 눌렀는가. 인덱스 해석은 View 가 끝낸다.
		public event Action<DailyBonusType> OnTabSelected;

		// 받을 수 있는 칸을 눌렀다 — 몇 일차인지만 넘긴다(어느 종류인지는 Presenter 가 안다).
		public event Action<int> OnClaimRequested;

		private void Awake()
		{
			// 첫 렌더가 끝나기 전 빈 칸이 비치지 않도록 숨겨 두고 시작한다.
			_canvasGroup = GetComponent<CanvasGroup>();
			if (_canvasGroup == null)
			{
				_canvasGroup = gameObject.AddComponent<CanvasGroup>();
			}

			setVisible(false);
			collectWeekSlots();
			collectMonthSlots();
			resolveWeekTabIndex();

			bindSlotClicks(_weekSlots);
			bindSlotClicks(_monthSlots);

			// Frame 은 Bg 가 레이캐스트를 흡수하므로, Dimmed 까지 내려오는 클릭은 곧 "팝업 밖을 눌렀다"는 뜻이다.
			if (_dimButton != null)
			{
				_dimButton.OnClickEvent += onCloseClicked;
			}

			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged += onTabChanged;
			}

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			if (_dimButton != null)
			{
				_dimButton.OnClickEvent -= onCloseClicked;
			}

			if (_tabGroup != null)
			{
				_tabGroup.OnTabChanged -= onTabChanged;
			}

			unbindSlotClicks(_weekSlots);
			unbindSlotClicks(_monthSlots);

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

		// 종류에 맞는 그룹만 켜고 그 칸들을 채운다. data 는 1일차부터 순서대로다.
		public async UniTask RenderAsync(DailyBonusType type, IReadOnlyList<DailyBonusSlotData> data, CancellationToken ct)
		{
			applyLayout(type);

			List<DailyBonusSlot> slots = (type == DailyBonusType.Month) ? _monthSlots : _weekSlots;

			_bindTasks.Clear();

			for (int i = 0; i < slots.Count; i++)
			{
				DailyBonusSlot slot = slots[i];
				if (slot == null)
				{
					continue;
				}

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

		public void SetTimeText(string text)
		{
			if (_timeText != null)
			{
				_timeText.text = text;
			}
		}

		// 주간 탭의 선택 표시만 바꾼다 — TabGroup.Select 는 OnTabChanged 를 쏘지 않으므로
		// 첫 렌더는 호출부가 직접 해야 한다.
		public void SelectWeekTab()
		{
			if (_tabGroup != null)
			{
				_tabGroup.Select(_weekTabIndex);
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

		// 주간은 Reward_1~6 이 Reward_Days 아래, Reward_LastDay 가 WeekGroup 직속이다.
		// 깊이 우선 순회가 그대로 1~7일차 순서라 계층에서 모으면 된다.
		private void collectWeekSlots()
		{
			_weekSlots.Clear();

			if (_weekGroup == null)
			{
				return;
			}

			_weekGroup.GetComponentsInChildren<DailyBonusSlot>(true, _weekSlots);
			warnIfShort(DailyBonusType.Week, _weekSlots.Count);
		}

		// 월간 격자는 자식 순서가 곧 일차다(0번째 = 1일차). 이름이나 글자에 기대지 않는다.
		private void collectMonthSlots()
		{
			_monthSlots.Clear();

			if (_monthGrid == null)
			{
				return;
			}

			for (int i = 0; i < _monthGrid.childCount; i++)
			{
				DailyBonusSlot slot = _monthGrid.GetChild(i).GetComponent<DailyBonusSlot>();
				if (slot == null)
				{
					Debug.LogWarning($"[DailyBonusPopup] 월간 격자 {i}번째 자식에 DailyBonusSlot 이 없다 — 일차가 밀린다.");
					continue;
				}

				_monthSlots.Add(slot);
			}

			warnIfShort(DailyBonusType.Month, _monthSlots.Count);
		}

		// 칸이 모자라면 뒷일차가 통째로 안 보인다 — 배선이 어긋난 것이라 조용히 넘기지 않는다.
		private void warnIfShort(DailyBonusType type, int collected)
		{
			int expected = DailyBonusCatalog.GetCycleLength(type);
			if (expected <= 0 || collected == expected)
			{
				return;
			}

			Debug.LogWarning($"[DailyBonusPopup] {type} 칸이 {collected}개다 — 테이블은 {expected}일차까지 있다. 프리펩 배선을 확인한다.");
		}

		// TabGroup 이 탭을 모으는 방식과 같은 API 를 써야 인덱스가 어긋나지 않는다.
		private void resolveWeekTabIndex()
		{
			_weekTabIndex = 0;

			if (_tabGroup == null || _weekTab == null)
			{
				Debug.LogWarning("[DailyBonusPopup] 주간 탭이 연결되지 않았다 — 탭이 뒤바뀐다.");
				return;
			}

			UITabButton[] tabs = _tabGroup.GetComponentsInChildren<UITabButton>(true);
			for (int i = 0; i < tabs.Length; i++)
			{
				if (tabs[i] == _weekTab)
				{
					_weekTabIndex = i;
					return;
				}
			}

			Debug.LogWarning("[DailyBonusPopup] 주간 탭이 TabGroup 아래에 없다 — 탭이 뒤바뀐다.");
		}

		private void bindSlotClicks(List<DailyBonusSlot> slots)
		{
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] != null)
				{
					slots[i].OnClaimClicked += onSlotClaimClicked;
				}
			}
		}

		private void unbindSlotClicks(List<DailyBonusSlot> slots)
		{
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] != null)
				{
					slots[i].OnClaimClicked -= onSlotClaimClicked;
				}
			}
		}

		private void applyLayout(DailyBonusType type)
		{
			bool isMonth = (type == DailyBonusType.Month);

			if (_weekGroup != null)
			{
				_weekGroup.SetActive(isMonth == false);
			}

			if (_monthGroup != null)
			{
				_monthGroup.SetActive(isMonth);
			}

			if (_titleText != null)
			{
				_titleText.text = isMonth ? "월간 출석" : "주간 출석";
			}

			if (_frame != null)
			{
				Vector2 size = _frame.sizeDelta;
				size.y = isMonth ? MONTH_FRAME_HEIGHT : WEEK_FRAME_HEIGHT;
				_frame.sizeDelta = size;
			}
		}

		private void setVisible(bool visible)
		{
			_canvasGroup.alpha = visible ? 1f : 0f;
			_canvasGroup.interactable = visible;
			_canvasGroup.blocksRaycasts = visible;
		}

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null)
			{
				OnTabSelected.Invoke((index == _weekTabIndex) ? DailyBonusType.Week : DailyBonusType.Month);
			}
		}

		private void onSlotClaimClicked(DailyBonusSlot slot)
		{
			if (OnClaimRequested != null)
			{
				OnClaimRequested.Invoke(slot.DayCount);
			}
		}

		private void onCloseClicked()
		{
			Close();
		}
	}
}
