using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 장비 화면. UIManager.OpenOverlayAsync로 오버레이 캔버스에 열린다.
	// 생명주기(로드/생성/파괴/스택)는 UIManager가 담당하고,
	// 이 클래스는 자기 내부 콘텐츠(탭/정렬/슬롯 바인딩)만 책임진다.
	public class EquipmentUI : UIScreen
	{
		[SerializeField] private UIButton _closeButton;	// 닫기(뒤로가기) 버튼

		[Header("탭 / 정렬 / 리스트")]
		[SerializeField] private TabGroup _tabGroup;			// TabMenu_Middle (Weapon/Armor/Acc)
		[SerializeField] private UIButton _sortButton;			// 등급 정렬 토글
		[SerializeField] private RectTransform _gridParent;		// GridLayout_Equipment
		[SerializeField] private EquipmentSlot _slotPrefab;		// 슬롯 프리펩
		[SerializeField] private GradeColorTable _gradeColors;	// 등급 색상 SO

		private EquipmentTypes _currentType = EquipmentTypes.Weapon;
		private bool _descending = true;	// true=전설→노말 (기본)

		private readonly List<EquipmentSlot> _slots = new List<EquipmentSlot>();
		private readonly List<Table_Equipment.Row> _rows = new List<Table_Equipment.Row>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// applyToSlotsAsync 일괄 대기용
		private readonly List<string> _preloadedIcons = new List<string>();	// 화면 동안 캐시 고정한 아이콘 주소

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		private void Awake()
		{
			_closeButton.OnClickEvent += onCloseClicked;
			_sortButton.OnClickEvent += onSortClicked;
			_tabGroup.OnTabChanged += onTabChanged;

			EventManager.Instance.Subscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(onPresetChanged);
		}

		private void OnDestroy()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
				_rebuildCts = null;
			}

			if (ResourceManager.HasInstance)
			{
				releasePreloadedIcons();
			}

			_closeButton.OnClickEvent -= onCloseClicked;
			_sortButton.OnClickEvent -= onSortClicked;
			_tabGroup.OnTabChanged -= onTabChanged;

			EventManager.Instance.Unsubscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
		}

		public override async UniTask OnOpenAsync(CancellationToken ct)
		{
			// 화면이 열린 동안 모든 장비 아이콘을 캐시에 고정한다.
			// 탭을 왔다갔다 해도 슬롯의 아이콘 로드가 캐시 히트(동기)가 되어 즉시 갱신된다.
			await preloadIconsAsync(ct);

			_currentType = EquipmentTypes.Weapon;
			_tabGroup.Select(0);	// Select는 OnTabChanged를 발행하지 않으므로 직접 rebuild
			rebuild();
		}

		public override UniTask OnCloseAsync()
		{
			releasePreloadedIcons();
			return UniTask.CompletedTask;
		}

		// 전체 장비 아이콘을 한 번 Acquire 해 캐시에 고정 (참조카운트 +1 유지).
		private async UniTask preloadIconsAsync(CancellationToken ct)
		{
			List<Table_Equipment.Row> all = new List<Table_Equipment.Row>(Table_Equipment.All().Values);
			List<UniTask> tasks = new List<UniTask>();
			for (int i = 0; i < all.Count; i++)
			{
				string address = all[i].Icon;
				if (string.IsNullOrEmpty(address) || _preloadedIcons.Contains(address))
				{
					continue;
				}

				_preloadedIcons.Add(address);
				tasks.Add(acquireIconAsync(address, ct));
			}

			await UniTask.WhenAll(tasks).SuppressCancellationThrow();
		}

		private async UniTask acquireIconAsync(string address, CancellationToken ct)
		{
			await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
		}

		// 고정해 둔 아이콘들의 참조카운트 -1 (0이 되면 실제 해제)
		private void releasePreloadedIcons()
		{
			for (int i = 0; i < _preloadedIcons.Count; i++)
			{
				ResourceManager.Instance.Release(_preloadedIcons[i]);
			}

			_preloadedIcons.Clear();
		}

		private void onCloseClicked()
		{
			UIManager.Instance.CloseOverlayAsync().Forget();
		}

		private void onTabChanged(int index)
		{
			_currentType = indexToType(index);
			rebuild();
		}

		private void onSortClicked()
		{
			_descending = !_descending;
			rebuild();
		}

		private void onInventoryChanged(InventoryChangeEvent e)
		{
			rebuild();
		}

		private void onPresetChanged(PresetChangeEvent e)
		{
			rebuild();
		}

		// 탭 인덱스 → 장비 타입 (탭 자식 순서: Weapon/Armor/Acc)
		private EquipmentTypes indexToType(int index)
		{
			switch (index)
			{
				case 1: return EquipmentTypes.Armor;
				case 2: return EquipmentTypes.Accessory;
				default: return EquipmentTypes.Weapon;
			}
		}

		// 현재 타입의 장비 전체를 정렬해 슬롯에 바인딩한다.
		// 아이콘 로드를 일괄 대기하므로 비동기로 진행하되, 이전 rebuild는 취소한다.
		private void rebuild()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
			}

			_rebuildCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
			rebuildAsync(_rebuildCts.Token).Forget();
		}

		private async UniTaskVoid rebuildAsync(CancellationToken ct)
		{
			collectRows();
			_rows.Sort(compareRows);
			await applyToSlotsAsync(ct);
		}

		private void collectRows()
		{
			_rows.Clear();

			List<Table_Equipment.Row> all = new List<Table_Equipment.Row>(Table_Equipment.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].EquipmentType == _currentType)
				{
					_rows.Add(all[i]);
				}
			}
		}

		// 등급 정렬(방향 적용) 후 동급은 ID 오름차순.
		private int compareRows(Table_Equipment.Row a, Table_Equipment.Row b)
		{
			int ga = (int)a.Grade;
			int gb = (int)b.Grade;
			if (ga != gb)
			{
				return _descending ? gb.CompareTo(ga) : ga.CompareTo(gb);
			}

			return a.ID.CompareTo(b.ID);
		}

		private async UniTask applyToSlotsAsync(CancellationToken ct)
		{
			Inventory inventory = Account.Instance.Inventory;
			Loadout loadout = Account.Instance.Loadout;
			int equippedId = loadout.GetSlot(loadout.Selected, _currentType);

			_bindTasks.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				EquipmentSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				Table_Equipment.Row row = _rows[i];
				bool owned = inventory.Has(row.ID);
				int count = inventory.GetCount(row.ID);
				int level = inventory.GetEnhanceLevel(row.ID);
				bool equipped = owned && row.ID == equippedId;

				_bindTasks.Add(slot.Bind(row, owned, count, level, equipped, _gradeColors, ct));
			}

			// 남는 슬롯은 비활성화 (풀 재사용)
			for (int i = _rows.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			// 현재 탭 아이콘 전부 로드 완료를 한 번에 대기 → 캐시 히트면 즉시, 미스면 일괄 표시
			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		private EquipmentSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			EquipmentSlot slot = Instantiate(_slotPrefab, _gridParent);
			_slots.Add(slot);
			return slot;
		}
	}
}
