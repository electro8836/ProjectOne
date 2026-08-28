using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;

namespace ProjectOne.UI
{
	// 장비 화면의 View(MVP). UIManager.OpenWindowAsync 로 오버레이 캔버스에 열린다.
	// 표시(슬롯 풀/아이콘 캐시/그리기)와 입력 전달만 담당하고, 데이터 조회·정렬·장착 결정은 EquipmentPresenter 가 한다.
	// (클래스명을 EquipmentUI 로 유지 — 프리펩이 이 스크립트 GUID 를 참조하므로 이름 변경 시 missing script 발생.)
	//
	// 닫기 버튼이 없다. 이 화면은 네비게이션 바의 탭으로 열리고 같은 탭 재클릭으로 닫히므로
	// (NavigationBar.onTabChanged), 창 자체가 닫기 입력을 가질 이유가 없다.
	public class EquipmentUI : UIScreen, IView
	{
		[Header("탭 / 리스트")]
		[SerializeField] private TabGroup _tabGroup;				// TabMenu_Middle (전체/무기/방어구/장신구/유물/소모품)
		[SerializeField] private RectTransform _gridParent;		// GridLayout_Items
		[SerializeField] private ItemSlot _slotPrefab;			// Prefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		[Header("장착 슬롯")]
		[SerializeField] private EquippedSlotView[] _equippedSlots;	// Slot_Weapon ~ Slot_Boots (8칸)

		[Header("캐릭터")]
		[SerializeField] private TMP_Text _levelText;	// LevelText

		[System.Serializable]
		private class EquippedSlotView
		{
			public EquipSlotTypes type;
			public Transform root;	// Slot_XXX 컨테이너
			[System.NonSerialized] public ItemSlot instance;
			[System.NonSerialized] public long uid;
		}

		// ── 입력 이벤트 (Presenter 가 구독) ────────────────────────────────
		public event Action<int> OnTabSelected;
		public event Action<long, int> OnSlotClicked;

		private readonly EquipmentPresenter _presenter = new EquipmentPresenter();

		private readonly List<ItemSlot> _slots = new List<ItemSlot>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// 렌더 일괄 대기용

		private void Awake()
		{
			_tabGroup.OnTabChanged += onTabChanged;

			_presenter.Initialize(this);
		}

		private void OnDestroy()
		{
			_presenter.Dispose();

			_tabGroup.OnTabChanged -= onTabChanged;
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

		// MonoBehaviour 의 파괴 토큰을 Presenter 에 제공(연속 rebuild 의 취소 기준).
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
		public async UniTask RenderGridAsync(IReadOnlyList<ItemSlotData> data, CancellationToken ct)
		{
			_bindTasks.Clear();
			for (int i = 0; i < data.Count; i++)
			{
				ItemSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				_bindTasks.Add(bindSlot(slot, data[i], ct));
			}

			for (int i = data.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// 현재 탭 아이콘 전부 로드 완료를 한 번에 대기 → 캐시 히트면 즉시, 미스면 일괄 표시
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 현재 선택 캐릭터의 장착 아이템을 Slot_Weapon ~ Slot_Boots 에 표시한다.
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
				if (d.instance == null)
				{
					clearEquippedSlot(slotView);
					continue;
				}

				_bindTasks.Add(updateEquippedSlot(slotView, d, ct));
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 캐릭터 레벨을 상단에 표시한다. 캐릭터가 하나뿐이라 이름·등급·아이콘은 프리팹 기본값을 쓴다.
		public void RenderCharacterLevel(int level)
		{
			_levelText.text = "LV." + level;
		}

		// ── 내부: 입력 → 이벤트 ────────────────────────────────────────────

		private void onTabChanged(int index)
		{
			if (OnTabSelected != null) { OnTabSelected.Invoke(index); }
		}

		private void onSlotClicked(long uid, int itemId)
		{
			if (OnSlotClicked != null) { OnSlotClicked.Invoke(uid, itemId); }
		}

		// ── 내부: 슬롯 풀 / 바인딩 ──────────────────────────────────────────

		// 장비 인스턴스와 스택 아이템은 표시 요소가 달라 바인딩 경로가 갈린다.
		private UniTask bindSlot(ItemSlot slot, ItemSlotData data, CancellationToken ct)
		{
			if (data.instance != null)
			{
				return slot.BindEquipmentAsync(data.instance, data.equipped, _gradeColors, ct);
			}

			return slot.BindItemAsync(data.row, data.count, _gradeColors, ct);
		}

		private ItemSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			ItemSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);
			return slot;
		}

		// 빈 아이템칸(ItemFrame_Square_02_Empty)은 끄지 않는다 — 그 위에 슬롯을 얹어 겹쳐 보이게 한다.
		// UI 의 앞뒤는 형제 순서로 결정되므로 마지막 자식으로 보내야 빈 칸을 덮는다.
		private async UniTask updateEquippedSlot(EquippedSlotView slotView, EquippedSlotData d, CancellationToken ct)
		{
			if (slotView.instance == null)
			{
				slotView.instance = Instantiate(_slotPrefab, slotView.root);
				stretchToParent(slotView.instance.transform as RectTransform);
				slotView.instance.transform.SetAsLastSibling();
				slotView.instance.OnClicked += onSlotClicked;
			}

			slotView.uid = d.instance.uid;
			// 이 칸에 있다는 것이 곧 장착중이다.
			await slotView.instance.BindEquipmentAsync(d.instance, true, _gradeColors, ct);
		}

		private void clearEquippedSlot(EquippedSlotView slotView)
		{
			if (slotView.instance != null)
			{
				slotView.instance.OnClicked -= onSlotClicked;
				Destroy(slotView.instance.gameObject);
				slotView.instance = null;
			}

			slotView.uid = 0;
		}

		private EquippedSlotData findData(IReadOnlyList<EquippedSlotData> data, EquipSlotTypes type)
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
	}
}
