using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 장비 화면의 View(MVP). UIManager.OpenOverlayAsync 로 오버레이 캔버스에 열린다.
	// 표시(슬롯 풀/아이콘 캐시/그리기)와 입력 전달만 담당하고, 데이터 조회·정렬·장착 결정은 EquipmentPresenter 가 한다.
	// (클래스명을 EquipmentUI 로 유지 — 프리펩이 이 스크립트 GUID 를 참조하므로 이름 변경 시 missing script 발생.)
	public class EquipmentUI : UIScreen, IView
	{
		[SerializeField] private UIButton _closeButton;	// 닫기(뒤로가기) 버튼

		[Header("탭 / 정렬 / 리스트")]
		[SerializeField] private TabGroup _tabGroup;			// TabMenu_Middle (Weapon/Armor/Acc)
		[SerializeField] private UIButton _sortButton;			// 등급 정렬 토글
		[SerializeField] private RectTransform _gridParent;		// GridLayout_Equipment
		[SerializeField] private EquipmentSlot _slotPrefab;		// 슬롯 프리펩
		[SerializeField] private GradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("장착 슬롯")]
		[SerializeField] private EquippedSlotView[] _equippedSlots;	// Weapon/Armor/Acc 장착 표시

		[Header("선택 캐릭터")]
		[SerializeField] private Image _characterIcon;	// Character
		[SerializeField] private TMP_Text _nameText;	// NameText
		[SerializeField] private TMP_Text _levelText;	// LevelText
		[SerializeField] private Image[] _stars;		// Grade_Stars/Star×5 (등급만큼 활성)

		[System.Serializable]
		private class EquippedSlotView
		{
			public EquipmentTypes type;
			public Transform root;          // Slot_Weapon/Armor/Acc 컨테이너
			public GameObject emptyFrame;   // ItemFrame_Square_02_Empty (장착 중 숨김)
			[System.NonSerialized] public EquipmentSlot instance;
			[System.NonSerialized] public int itemId;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action OnCloseRequested;
		public event Action<int> OnTabSelected;
		public event Action OnSortToggled;
		public event Action<int> OnSlotClicked;

		private readonly EquipmentPresenter _presenter = new EquipmentPresenter();

		private readonly List<EquipmentSlot> _slots = new List<EquipmentSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		// 선택 캐릭터 아이콘 주소 캐시 (Acquire/Release 짝 맞춤용)
		private string _charIconAddr;

		private void Awake()
		{
			_closeButton.OnClickEvent += onCloseClicked;
			_sortButton.OnClickEvent += onSortClicked;
			_tabGroup.OnTabChanged += onTabChanged;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_closeButton.OnClickEvent -= onCloseClicked;
			_sortButton.OnClickEvent -= onSortClicked;
			_tabGroup.OnTabChanged -= onTabChanged;

			releaseCharacterIcon();
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}

		public override UniTask OnCloseAsync()
		{
			return _presenter.OnCloseAsync();
		}

		// ── Presenter 가 호출하는 표시 API ─────────────────────────────────

		// MonoBehaviour 의 파괴 토큰을 Presenter 에 제공(연속 rebuild·팝업 열기의 취소 기준).
		public CancellationToken GetDestroyToken()
		{
			return this.GetCancellationTokenOnDestroy();
		}

		// 탭만 선택(OnTabChanged 를 발행하지 않음 — 초기 표시용).
		public void SelectTab(int index)
		{
			_tabGroup.Select(index);
		}

		// 그리드 슬롯 렌더 — 데이터 개수만큼 슬롯을 켜 바인딩, 남는 슬롯은 비활성화(풀 재사용).
		public async UniTask RenderGridAsync(IReadOnlyList<EquipmentSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				EquipmentSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				EquipmentSlotData d = data[i];
				_bindTasks.Add(slot.Bind(d.row, d.owned, d.count, d.level, d.equipped, _gradeColors, ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// 현재 탭 아이콘 전부 로드 완료를 한 번에 대기 → 캐시 히트면 즉시, 미스면 일괄 표시
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 현재 선택 캐릭터의 장착 아이템을 Slot_Weapon/Armor/Acc 에 표시한다.
		public async UniTask RenderEquippedAsync(IReadOnlyList<EquippedSlotData> data, CancellationToken ct)
		{
			if (_equippedSlots == null)
			{
				return;
			}

			_bindTasks.Clear();
			for (int i = 0; i < _equippedSlots.Length; i++)
			{
				EquippedSlotView slotView = _equippedSlots[i];
				EquippedSlotData d = findData(data, slotView.type);
				if (d.row == null)
				{
					clearEquippedSlot(slotView);
					continue;
				}

				_bindTasks.Add(updateEquippedSlot(slotView, d, ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 현재 선택(주캐릭터)된 캐릭터의 아이콘/이름/레벨/등급별을 상단에 표시한다.
		public async UniTask RenderSelectedCharacterAsync(SelectedCharacterData data, CancellationToken ct)
		{
			_nameText.text = data.name;
			_levelText.text = "LV." + data.level;
			applyStars(data.grade);
			await setCharacterIcon(data.iconAddress, ct);
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onCloseClicked()
		{
			if (OnCloseRequested != null) { OnCloseRequested.Invoke(); }
		}

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null) { OnTabSelected.Invoke(index); }
		}

		private void onSortClicked()
		{
			if (OnSortToggled != null) { OnSortToggled.Invoke(); }
		}

		private void onSlotClicked(int itemId)
		{
			if (OnSlotClicked != null) { OnSlotClicked.Invoke(itemId); }
		}

		// ── 내부: 슬롯 풀 / 아이콘 ──────────────────────────────────────────

		private EquipmentSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			EquipmentSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);
			return slot;
		}

		private async UniTask updateEquippedSlot(EquippedSlotView slotView, EquippedSlotData d, CancellationToken ct)
		{
			if (slotView.emptyFrame != null)
			{
				slotView.emptyFrame.SetActive(false);
			}

			if (slotView.instance == null)
			{
				slotView.instance = Instantiate(_slotPrefab, slotView.root);
				stretchToParent(slotView.instance.transform as RectTransform);
				slotView.instance.OnClicked += onSlotClicked;
			}

			slotView.itemId = d.row.ID;
			await slotView.instance.Bind(d.row, true, d.count, d.level, false, _gradeColors, ct);
			slotView.instance.HideStatusObjects();	// 장착 슬롯은 미보유/Focus 표시 숨김
			slotView.instance.HideCount();			// 장착 슬롯은 수량 표시 숨김
		}

		private void clearEquippedSlot(EquippedSlotView slotView)
		{
			if (slotView.instance != null)
			{
				slotView.instance.OnClicked -= onSlotClicked;
				Destroy(slotView.instance.gameObject);
				slotView.instance = null;
			}

			slotView.itemId = 0;
			if (slotView.emptyFrame != null)
			{
				slotView.emptyFrame.SetActive(true);
			}
		}

		private EquippedSlotData findData(IReadOnlyList<EquippedSlotData> data, EquipmentTypes type)
		{
			for (int i = 0; i < data.Count; i++)
			{
				if (data[i].type == type)
				{
					return data[i];
				}
			}

			return default;
		}

		private void stretchToParent(RectTransform rt)
		{
			if (rt == null)
			{
				return;
			}

			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		// ── 내부: 선택 캐릭터 아이콘 / 별 ───────────────────────────────────

		// 등급만큼 별을 활성화한다.
		private void applyStars(CharacterGrade grade)
		{
			int count = (int)grade;
			for (int i = 0; i < _stars.Length; i++)
			{
				_stars[i].gameObject.SetActive(i < count);
			}
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		private async UniTask setCharacterIcon(string address, CancellationToken ct)
		{
			if (_charIconAddr == address)
			{
				return;
			}

			releaseCharacterIcon();
			_charIconAddr = address;

			if (string.IsNullOrEmpty(address))
			{
				_characterIcon.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 캐시를 비운다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_characterIcon.sprite = atlasSprite;
				_charIconAddr = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 렌더되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_charIconAddr != address)
			{
				return;
			}

			if (icon != null)
			{
				_characterIcon.sprite = icon;
			}
		}

		private void releaseCharacterIcon()
		{
			// 앱/플레이 종료 시엔 ResourceManager 가 먼저 파괴됐을 수 있어 null 가드.
			if (!string.IsNullOrEmpty(_charIconAddr) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_charIconAddr);
				_charIconAddr = null;
			}
		}
	}
}
