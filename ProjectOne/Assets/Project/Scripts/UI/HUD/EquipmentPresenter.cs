using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Network;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 그리드 슬롯 1칸 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	// 장비는 인스턴스 단위라 인스턴스를 그대로 넘기고, 소모품은 인스턴스가 없어 테이블 행 + 보유 개수를 넘긴다.
	// instance 가 null 이면 스택 아이템이다.
	public struct ItemSlotData
	{
		public EquipmentInstance instance;
		public Table_Item.Row row;
		public int count;
		public bool equipped;
	}

	// 장착 슬롯 렌더 데이터 — instance 가 null 이면 빈 슬롯.
	public struct EquippedSlotData
	{
		public EquipSlotTypes type;
		public EquipmentInstance instance;
	}

	// 장비 화면 Presenter — 분류 탭 상태와 Model(Account) 조회를 담당하고, View 에 렌더 데이터를 넘긴다.
	// 화면 닫힘 시 장착 변경(dirty)을 서버에 1회 flush 한다.
	public sealed class EquipmentPresenter : Presenter<EquipmentUI>
	{
		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";
		private const string CONSUMABLE_POPUP_ADDRESS = "UIPrefab_ConsumablePopup";

		// 분류 탭 인덱스 — 프리펩 TabMenu_Middle 의 Hierarchy 순서와 일대일로 맞춘다.
		private const int TAB_ALL = 0;
		private const int TAB_WEAPON = 1;
		private const int TAB_ARMOR = 2;
		private const int TAB_ACCESSORY = 3;
		private const int TAB_RELIC = 4;
		private const int TAB_CONSUMABLE = 5;

		private int _currentTab = TAB_ALL;

		private readonly List<ItemSlotData> _gridData = new List<ItemSlotData>();
		private readonly List<ItemSlotData> _equipBuffer = new List<ItemSlotData>();
		private readonly List<ItemSlotData> _itemBuffer = new List<ItemSlotData>();
		private readonly List<EquippedSlotData> _equippedData = new List<EquippedSlotData>();

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnSlotClicked += onSlotClicked;

			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Subscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Subscribe<PresetChangeEvent>(onPresetChanged);
		}

		protected override void OnDispose()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
				_rebuildCts = null;
			}

			view.OnTabSelected -= onTabSelected;
			view.OnSlotClicked -= onSlotClicked;

			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Unsubscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
		}

		// 아이콘은 부트에서 아틀라스로 상주하므로 프리로드 대기 없이 곧바로 슬롯을 만든다.
		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_currentTab = TAB_ALL;
			view.SelectTab(TAB_ALL);	// Select 는 OnTabChanged 를 발행하지 않으므로 직접 rebuild
			view.RenderCharacterLevel(Account.Instance.Loadout.Level);
			rebuild();
			return UniTask.CompletedTask;
		}

		// 화면 닫힘 — 장착 변경(dirty)이 있으면 서버에 1회 저장(패킷 절약).
		public override UniTask OnCloseAsync()
		{
			NetworkManager.Instance.FlushLoadoutIfDirty();
			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			_currentTab = index;
			rebuild();
		}

		// 슬롯 클릭 — 정보 팝업을 상위 캔버스에 연다(네비게이션 결정은 Presenter).
		// uid 가 0 이면 인스턴스가 없는 스택 아이템(소모품)이라 아이템 ID 로 여는 전용 팝업을 쓴다.
		private void onSlotClicked(long uid, int itemId)
		{
			if (uid == 0)
			{
				UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, itemId, false, view.GetDestroyToken()).Forget();
				return;
			}

			UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, uid, view.GetDestroyToken()).Forget();
		}

		private void onEquipmentChanged(EquipmentChangeEvent e)
		{
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

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 현재 탭의 아이템 전체를 정렬해 슬롯 데이터로 만들고 View 에 렌더를 지시한다(이전 rebuild 는 취소).
		private void rebuild()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
			}

			_rebuildCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			rebuildAsync(_rebuildCts.Token).Forget();
		}

		private async UniTaskVoid rebuildAsync(CancellationToken ct)
		{
			buildGridData();
			await view.RenderGridAsync(_gridData, ct);

			buildEquippedData();
			await view.RenderEquippedAsync(_equippedData, ct);
		}

		// 그리드는 보유한 것만 나열한다. 장비는 인스턴스, 소모품은 스택으로 소스가 달라 따로 모은 뒤
		// 각각 정렬해 이어붙인다 (전체 탭에서 장비가 앞, 소모품이 뒤).
		private void buildGridData()
		{
			_gridData.Clear();

			if (_currentTab != TAB_CONSUMABLE)
			{
				collectEquipments();
				_equipBuffer.Sort(compareEquipment);

				for (int i = 0; i < _equipBuffer.Count; i++)
				{
					_gridData.Add(_equipBuffer[i]);
				}
			}

			if (_currentTab == TAB_ALL || _currentTab == TAB_CONSUMABLE)
			{
				collectConsumables();
				_itemBuffer.Sort(compareItem);

				for (int i = 0; i < _itemBuffer.Count; i++)
				{
					_gridData.Add(_itemBuffer[i]);
				}
			}
		}

		// 보유 장비 인스턴스 중 현재 탭의 착용 부위에 해당하는 것만 모은다.
		private void collectEquipments()
		{
			Loadout loadout = Account.Instance.Loadout;
			IReadOnlyList<EquipmentInstance> all = Account.Instance.Inventory.GetAllEquipments();

			_equipBuffer.Clear();
			for (int i = 0; i < all.Count; i++)
			{
				EquipmentInstance instance = all[i];
				Table_Equipment.Row equip = instance.Equipment;
				if (equip == null)
				{
					continue;
				}

				if (matchesTab(equip.EquipSlotType) == false)
				{
					continue;
				}

				ItemSlotData data;
				data.instance = instance;
				data.row = instance.Item;
				data.count = 0;
				data.equipped = loadout.GetSlot(equip.EquipSlotType) == instance.uid;
				_equipBuffer.Add(data);
			}
		}

		// 보유 스택 아이템 중 소모품만 모은다 (재료·수집품은 이 화면에 나오지 않는다).
		private void collectConsumables()
		{
			IReadOnlyList<OwnedItem> all = Account.Instance.Inventory.GetAllItems();

			_itemBuffer.Clear();
			for (int i = 0; i < all.Count; i++)
			{
				OwnedItem owned = all[i];
				Table_Item.Row row = Table_Item.Get(owned.itemId);
				if (row == null || row.MainCategory != ItemMainCategory.Consumable)
				{
					continue;
				}

				ItemSlotData data;
				data.instance = null;
				data.row = row;
				data.count = owned.count;
				data.equipped = false;
				_itemBuffer.Add(data);
			}
		}

		// 착용 부위 → 분류 탭. 방어구·장신구는 여러 부위를 묶는다.
		// ItemSubCategory 가 아니라 EquipSlotTypes 로 판정하는 이유 — 장착 슬롯 8칸과 같은 축이라 어긋날 수 없다.
		private bool matchesTab(EquipSlotTypes slot)
		{
			switch (_currentTab)
			{
			case TAB_ALL:
				return true;

			case TAB_WEAPON:
				return slot == EquipSlotTypes.Weapon;

			case TAB_ARMOR:
				return slot == EquipSlotTypes.Helmet || slot == EquipSlotTypes.Armor
					|| slot == EquipSlotTypes.Gloves || slot == EquipSlotTypes.Boots;

			case TAB_ACCESSORY:
				return slot == EquipSlotTypes.Ring || slot == EquipSlotTypes.Amulet;

			case TAB_RELIC:
				return slot == EquipSlotTypes.Relic;
			}

			return false;
		}

		// 등급 내림차순 → 동급은 강화 레벨 내림차순 → 그래도 같으면 UID 오름차순.
		private int compareEquipment(ItemSlotData a, ItemSlotData b)
		{
			int ga = (int)a.instance.grade;
			int gb = (int)b.instance.grade;
			if (ga != gb)
			{
				return gb.CompareTo(ga);
			}

			if (a.instance.level != b.instance.level)
			{
				return b.instance.level.CompareTo(a.instance.level);
			}

			return a.instance.uid.CompareTo(b.instance.uid);
		}

		// 등급 내림차순 → 동급은 아이템 ID 오름차순.
		private int compareItem(ItemSlotData a, ItemSlotData b)
		{
			int ga = (int)a.row.Grade;
			int gb = (int)b.row.Grade;
			if (ga != gb)
			{
				return gb.CompareTo(ga);
			}

			return a.row.ID.CompareTo(b.row.ID);
		}

		private void buildEquippedData()
		{
			Loadout loadout = Account.Instance.Loadout;

			_equippedData.Clear();
			for (int i = 1; i < ProjectOne.Shared.LoadoutDto.SlotCount; i++)
			{
				EquippedSlotData data;
				data.type = (EquipSlotTypes)i;
				data.instance = loadout.GetEquipped(data.type);
				_equippedData.Add(data);
			}
		}
	}
}
