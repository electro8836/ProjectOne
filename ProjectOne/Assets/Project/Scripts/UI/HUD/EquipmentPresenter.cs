using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 그리드 슬롯 1칸 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct EquipmentSlotData
	{
		public Table_Equipment.Row row;
		public bool owned;
		public int count;
		public int level;
		public bool equipped;
	}

	// 장착 슬롯(Weapon/Armor/Acc) 렌더 데이터 — row 가 null 이면 빈 슬롯.
	public struct EquippedSlotData
	{
		public EquipmentTypes type;
		public Table_Equipment.Row row;
		public int count;
		public int level;
	}

	// 선택(주캐릭터) 캐릭터 헤더 렌더 데이터.
	public struct SelectedCharacterData
	{
		public string iconAddress;
		public string name;
		public int level;
		public CharacterGrade grade;
	}

	// 장비 화면 Presenter — 정렬/필터 상태와 Model(Account) 조회를 담당하고, View 에 렌더 데이터를 넘긴다.
	// 화면 닫힘 시 장착 변경(dirty)을 서버에 1회 flush 한다.
	public sealed class EquipmentPresenter : Presenter<EquipmentUI>
	{
		private const string ITEM_INFO_POPUP_ADDRESS = "Prefab_ItemInfoPopup";

		private EquipmentTypes _currentType = EquipmentTypes.Weapon;
		private bool _descending = true;	// true=전설→노말 (기본)

		private readonly List<EquipmentSlotData> _gridData = new List<EquipmentSlotData>();
		private readonly List<EquippedSlotData> _equippedData = new List<EquippedSlotData>();

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		protected override void OnInitialize()
		{
			view.OnCloseRequested += onCloseRequested;
			view.OnTabSelected += onTabSelected;
			view.OnSortToggled += onSortToggled;
			view.OnSlotClicked += onSlotClicked;

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

			view.OnCloseRequested -= onCloseRequested;
			view.OnTabSelected -= onTabSelected;
			view.OnSortToggled -= onSortToggled;
			view.OnSlotClicked -= onSlotClicked;

			EventManager.Instance.Unsubscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
		}

		// 아이콘은 부트에서 아틀라스(IconAtlasCache)로 상주하므로 프리로드 대기 없이 곧바로 슬롯을 만든다.
		// → 슬롯 프리펩이 즉시 생성되고, 슬롯이 동기 아틀라스 조회로 아이콘까지 같은 프레임에 채운다.
		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_currentType = EquipmentTypes.Weapon;
			view.SelectTab(0);	// Select 는 OnTabChanged 를 발행하지 않으므로 직접 rebuild
			renderSelectedCharacter(ct);
			rebuild();
			return UniTask.CompletedTask;
		}

		// 현재 선택된 주캐릭터의 아이콘/이름/레벨/등급을 헤더에 1회 표시한다.
		// (장비 화면에는 캐릭터 선택 UI 가 없어 열린 동안 선택은 고정이므로 탭/정렬 rebuild 와 분리.)
		private void renderSelectedCharacter(CancellationToken ct)
		{
			int selected = Account.Instance.Loadout.Selected;
			Table_Character.Row row = Table_Character.Get(selected);
			if (row == null)
			{
				return;
			}

			SelectedCharacterData data;
			data.iconAddress = row.Icon;
			data.name = row.Name;
			data.grade = row.Grade;
			OwnedCharacter oc = Account.Instance.Loadout.GetOwned(selected);
			data.level = oc != null ? oc.level : 0;
			view.RenderSelectedCharacterAsync(data, ct).Forget();
		}

		// 화면 닫힘 — 장착 변경(dirty)이 있으면 서버에 1회 저장(패킷 절약).
		public override UniTask OnCloseAsync()
		{
			NetworkManager.Instance.FlushLoadoutIfDirty();
			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onCloseRequested()
		{
			UIManager.Instance.CloseOverlayAsync().Forget();
		}

		private void onTabSelected(int index)
		{
			_currentType = indexToType(index);
			rebuild();
		}

		private void onSortToggled()
		{
			_descending = !_descending;
			rebuild();
		}

		// 슬롯 클릭 → 아이템 정보 팝업을 상위 캔버스에 연다(네비게이션 결정은 Presenter).
		private void onSlotClicked(int itemId)
		{
			UIManager.Instance.ShowItemInfoPopupAsync(ITEM_INFO_POPUP_ADDRESS, itemId, view.GetDestroyToken()).Forget();
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

		// 현재 타입의 장비 전체를 정렬해 슬롯 데이터로 만들고 View 에 렌더를 지시한다(이전 rebuild 는 취소).
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

		private void buildGridData()
		{
			Inventory inventory = Account.Instance.Inventory;
			Loadout loadout = Account.Instance.Loadout;
			int equippedId = loadout.GetSlot(loadout.Selected, _currentType);

			_gridData.Clear();
			List<Table_Equipment.Row> all = new List<Table_Equipment.Row>(Table_Equipment.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].EquipmentType != _currentType)
				{
					continue;
				}

				_gridData.Add(makeSlotData(all[i], inventory, equippedId));
			}

			_gridData.Sort(compareData);
		}

		private EquipmentSlotData makeSlotData(Table_Equipment.Row row, Inventory inventory, int equippedId)
		{
			EquipmentSlotData data;
			data.row = row;
			data.owned = inventory.Has(row.ID);
			data.count = inventory.GetCount(row.ID);
			data.level = inventory.GetEnhanceLevel(row.ID);
			data.equipped = data.owned && row.ID == equippedId;
			return data;
		}

		// 등급 정렬(방향 적용) 후 동급은 ID 오름차순.
		private int compareData(EquipmentSlotData a, EquipmentSlotData b)
		{
			int ga = (int)a.row.Grade;
			int gb = (int)b.row.Grade;
			if (ga != gb)
			{
				return _descending ? gb.CompareTo(ga) : ga.CompareTo(gb);
			}

			return a.row.ID.CompareTo(b.row.ID);
		}

		private void buildEquippedData()
		{
			Loadout loadout = Account.Instance.Loadout;
			Inventory inventory = Account.Instance.Inventory;
			int selected = loadout.Selected;

			_equippedData.Clear();
			addEquipped(EquipmentTypes.Weapon, loadout, inventory, selected);
			addEquipped(EquipmentTypes.Armor, loadout, inventory, selected);
			addEquipped(EquipmentTypes.Accessory, loadout, inventory, selected);
		}

		private void addEquipped(EquipmentTypes type, Loadout loadout, Inventory inventory, int selected)
		{
			int itemId = loadout.GetSlot(selected, type);
			bool hasItem = itemId > 0 && inventory.Has(itemId);

			EquippedSlotData data;
			data.type = type;
			data.row = hasItem ? Table_Equipment.Get(itemId) : null;
			data.count = hasItem ? inventory.GetCount(itemId) : 0;
			data.level = hasItem ? inventory.GetEnhanceLevel(itemId) : 0;
			_equippedData.Add(data);
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
	}
}
