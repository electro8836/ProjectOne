using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 마스터리 화면의 View(MVP). 네비게이션 바의 마스터리 탭이 UIPrefab_Mastery 를 창 캔버스에 연다.
	// 표시(텍스트·게이지·아이콘)와 입력 전달만 담당하고, 어떤 마스터리를 보여줄지·잠글지는 MasteryPresenter 가 정한다.
	//
	// 닫기 버튼(HomeButton)이 눌리면 창을 닫고, 그때 발행되는 WindowClosedEvent 가
	// 네비게이션 바의 탭 선택까지 함께 푼다.
	public class MasteryUI : UIScreen, IView
	{
		[Header("탭")]
		[SerializeField] private TabGroup _tabGroup;			// TopMenu/TabButtonGrid
		[SerializeField] private UITabButton _currentTabButton;	// TabButton_Current — 무기 미착용 시 잠근다
		[SerializeField] private GameObject _currentMastery;	// Buttom/CurrentMastery
		[SerializeField] private GameObject _allMastery;		// Buttom/AllMastery

		[Header("현재 마스터리 정보")]
		[SerializeField] private Image _masteryIcon;		// Info/Frame/MasteryIcon
		[SerializeField] private TMP_Text _masteryName;	// Info/MasteryName
		[SerializeField] private TMP_Text _masteryLevel;	// Info/MasteryLevel
		[SerializeField] private Slider _expSlider;		// Info/ExpInfo
		[SerializeField] private TMP_Text _expText;		// Info/ExpInfo/ExpText

		[Header("전체 마스터리 정보")]
		[SerializeField] private TMP_Text _totalLevelText;			// AllMastery/Info/Frame/LevelText
		[SerializeField] private TMP_Text _masteryBonusText;		// AllMastery/Info/MasteryBonusText
		[SerializeField] private TMP_Text _totalMasteryLevelText;	// AllMastery/Info/TotalMasteryLevelText
		[SerializeField] private Slider _totalExpSlider;			// AllMastery/Info/ExpInfo
		[SerializeField] private TMP_Text _totalExpText;			// AllMastery/Info/ExpInfo/ExpText

		[Header("전체 마스터리 목록")]
		[SerializeField] private MasteryInfoSlot _masteryInfoSlotPrefab;	// UIPrefab_MastertyInfoSlot
		[SerializeField] private RectTransform _masteryListParent;		// AllMastery/MasteryList/Viewport/Content/Grid

		[Header("닫기")]
		[SerializeField] private UIButton _homeButton;		// Top/HomeButton

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnTabSelected;
		public event Action OnHomeClicked;

		private readonly MasteryPresenter _presenter = new MasteryPresenter();

		// 마스터리 목록 슬롯 풀 — 탭을 오갈 때마다 새로 찍지 않고 재사용한다.
		private readonly List<MasteryInfoSlot> _slots = new List<MasteryInfoSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_tabGroup.OnTabChanged += onTabChanged;
			_homeButton.OnClickEvent += onHomeClicked;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_tabGroup.OnTabChanged -= onTabChanged;
			_homeButton.OnClickEvent -= onHomeClicked;

			releaseIcon();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// MonoBehaviour 의 파괴 토큰을 Presenter 에 제공(연속 렌더의 취소 기준).
		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 탭만 선택(OnTabChanged 를 발행하지 않음 — 초기 표시용).
		public void SelectTab(int index)
		{
			_tabGroup.Select(index);
		}

		// 무기 미착용이면 현재 마스터리 탭을 잠근다 — Locked 는 클릭도 막고 TabGroup 의 선택 대상에서도 빠진다.
		public void SetCurrentTabLocked(bool locked)
		{
			_currentTabButton.SetState(locked ? TabState.Locked : TabState.Unselected);
		}

		// 두 페이지는 배타다 — 한쪽을 켜면 반대쪽은 꺼진다.
		public void ShowPage(bool current)
		{
			_currentMastery.SetActive(current);
			_allMastery.SetActive(current == false);
		}

		// 현재 마스터리 정보 표시. 아이콘 로드만 비동기라 나머지는 먼저 세팅한다.
		public UniTask RenderCurrentAsync(MasteryInfoData data, CancellationToken ct)
		{
			_masteryName.text = data.name;
			_masteryLevel.text = $"레벨 {data.level}";
			_expSlider.value = data.expRatio;
			_expText.text = data.isMaxLevel ? "MAX" : $"{data.expRatio * 100f:F2}%";

			return setIcon(data.iconAddress, ct);
		}

		// 전체 마스터리 정보 표시. 아이콘이 없어 전부 동기다.
		public void RenderTotal(MasteryTotalData data)
		{
			_totalLevelText.text = data.totalLevel.ToString();
			_masteryBonusText.text = data.bonusText;
			_totalMasteryLevelText.text = $"{data.totalLevel}/{data.maxTotalLevel}";

			_totalExpSlider.value = data.ratio;
			_totalExpText.text = data.isMax ? "MAX" : $"{data.ratio * 100f:F2}%";
		}

		// 마스터리 목록 렌더 — 데이터 개수만큼 슬롯을 켜 바인딩, 남는 슬롯은 비활성화(풀 재사용).
		public async UniTask RenderMasteryListAsync(IReadOnlyList<MasteryInfoSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				MasteryInfoSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(slot.BindAsync(data[i], ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// 아이콘 로드 완료를 한 번에 대기 → 캐시 히트면 즉시, 미스면 일괄 표시
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null) { OnTabSelected.Invoke(index); }
		}

		private void onHomeClicked()
		{
			if (OnHomeClicked != null) { OnHomeClicked.Invoke(); }
		}

		// ── 내부: 슬롯 풀 ──────────────────────────────────────────────────

		private MasteryInfoSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			MasteryInfoSlot slot = Instantiate(_masteryInfoSlotPrefab, _masteryListParent);
			_slots.Add(slot);
			return slot;
		}

		// ── 내부: 아이콘 ───────────────────────────────────────────────────

		// ItemSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_masteryIcon.sprite = null;
				_masteryIcon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_masteryIcon.sprite = atlasSprite;
				_masteryIcon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_masteryIcon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_masteryIcon.sprite = icon;
				_masteryIcon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}
