using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Mastery;
using ProjectOne.Resources;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.Boot
{
	// 개발/테스트용 객체 (MonoSingleton, 부트씬 1개 배치).
	// - 데이터 로드 완료 후 인스펙터 구성값으로 Account 를 덮어써 개발 데이터를 주입한다("사용안함" 체크 시 생략).
	// - 게임 진행 중에는 Account 의 아이템/재화/캐릭터 레벨을 주기적으로 인스펙터에 표시한다(읽기 전용 뷰).
	// 모든 Awake 는 Start 보다 먼저 실행되므로 GameBootstrapper.Start(흐름 시작) 이전에 구독이 끝난다.
	public class DevTester : MonoSingleton<DevTester>
	{
		// 장착 슬롯 1칸 설정 — 장비 인스턴스 1개를 만들어 그 슬롯에 끼운다.
		// slot 은 검증용이 아니라 표기용이다. 실제 착용 슬롯은 Equipment 테이블이 소유한다.
		[System.Serializable]
		public struct DevSlot
		{
			public EquipSlotTypes slot;
			public int itemId;
			public ItemGradeType grade;
			public int level;
			public EquipPurity purity;
			public int quality;
		}

		// 보유 스택 아이템 1종 — 소모품·재료. 장비는 인스턴스라 DevSlot 이 따로 담당한다.
		[System.Serializable]
		public struct DevItem
		{
			public int itemId;
			public int count;
		}

		// 보유 재화 1종. 같은 종류를 여러 줄에 적으면 합산한다(스택 아이템과 같은 규칙).
		[System.Serializable]
		public struct DevCurrency
		{
			public EDT.Currency type;
			public int amount;
		}

		// 마스터리 1종의 개발 진행도 — 레벨만 지정하면 누적 경험치를 역산해 넣는다.
		[System.Serializable]
		public struct DevMastery
		{
			public WeaponMastery mastery;
			public int level;
		}

		// ── 인스펙터 표시용 뷰 항목 (읽기 전용) ──────────────────────
		[System.Serializable]
		public struct ItemView
		{
			public int itemId;
			public string name;
			public int count;
		}

		[System.Serializable]
		public struct EquipmentView
		{
			public long uid;
			public string name;
			public ItemGradeType grade;
			public int level;
			public EquipSlotTypes equippedSlot;
		}

		[System.Serializable]
		public struct CurrencyView
		{
			public EDT.Currency type;
			public int amount;
		}

		[Header("사용안함 — 체크 시 서버/로컬 데이터 사용")]
		[SerializeField] private bool _disabled;

		[Header("캐릭터")]
		[SerializeField] private int _characterLevel = 1;

		[Header("장착 슬롯 (슬롯 종류 + 아이템 ID)")]
		[SerializeField] private List<DevSlot> _equipSlots = new List<DevSlot>();

		// 장착하지 않고 인벤토리에만 두는 장비 — 장비 화면의 목록/정렬을 보려면 이쪽에 넣는다.
		// slot 은 _equipSlots 와 마찬가지로 표기용이다(실제 착용 부위는 Equipment 테이블 소유).
		[Header("보유 장비 (미장착 — 인벤토리에만 존재)")]
		[SerializeField] private List<DevSlot> _ownedEquipments = new List<DevSlot>();

		[Header("보유 아이템 (소모품·재료 — 아이템 ID + 개수)")]
		[SerializeField] private List<DevItem> _ownedItems = new List<DevItem>();

		[Header("보유 재화 (재화 종류 + 수량)")]
		[SerializeField] private List<DevCurrency> _currencies = new List<DevCurrency>();

		[Header("마스터리 진행도 (무기 + 레벨)")]
		[SerializeField] private List<DevMastery> _masteries = new List<DevMastery>();

		[Header("코스튬 보유 ID 목록")]
		[SerializeField] private List<int> _ownedCostumes = new List<int>();

		[Header("코스튬 착용 (0 = 미착용, 바디는 기본 코스튬)")]
		[SerializeField] private int _equippedBodyCostume;
		[SerializeField] private int _equippedWeaponCostume;

		[Header("임시 — 체크 시 이동 중에도 공격")]
		[SerializeField] private bool _attackWhileMoving;

		[Header("임시 — 체크 시 히어로에게 자석펫을 붙인다")]
		[SerializeField] private bool _spawnPet;

		[Header("런타임 조회 (읽기 전용)")]
		[SerializeField] private float _viewRefreshInterval = 0.5f;
		[SerializeField] private int _viewCharacterLevel;
		[SerializeField] private int _viewCharacterExp;
		[SerializeField] private List<CurrencyView> _viewCurrencies = new List<CurrencyView>();
		[SerializeField] private List<ItemView> _viewItems = new List<ItemView>();
		[SerializeField] private List<EquipmentView> _viewEquipments = new List<EquipmentView>();

		private EDT.Currency[] _currencyTypes;
		private float _viewTimer;
		private System.Action<DataLoadedEvent> _onDataLoaded;

		// [임시] 자석펫 지급용 — 정식 펫 콜렉션이 들어오면 아래 펫 관련 멤버를 통째로 걷어낸다
		private const string PetAddress = "Prefab_Pet";
		private System.Action<UnitSpawnedEvent> _onUnitSpawned;
		private GameObject _petInstance;
		private bool _isPetHandleHeld;

		protected override void Awake()
		{
			base.Awake();
			if (this != Instance)
			{
				return;
			}

			_currencyTypes = (EDT.Currency[])System.Enum.GetValues(typeof(EDT.Currency));

			// 서버 없이 실행할 때 개발 데이터를 넣는 용도. 데이터 로드 완료(DataLoadedEvent) 후
			// Account 를 직접 오버라이드한다(인메모리, 비영속).
			_onDataLoaded = onDataLoaded;
			EventManager.Instance.Subscribe<DataLoadedEvent>(_onDataLoaded);

			// [임시] 히어로가 생길 때마다 자석펫을 붙인다 (씬마다 히어로가 새로 만들어진다)
			_onUnitSpawned = onUnitSpawned;
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(_onUnitSpawned);
		}

		protected override void OnDestroy()
		{
			if (this == Instance && _onDataLoaded != null)
			{
				EventManager.Instance.Unsubscribe<DataLoadedEvent>(_onDataLoaded);
			}

			if (this == Instance && _onUnitSpawned != null)
			{
				EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(_onUnitSpawned);
			}

			releasePet();

			base.OnDestroy();
		}

		private void Update()
		{
			// [임시] 이동 중 공격 스위치 — 플레이 중 체크를 바로 반영하려고 주기 갱신보다 앞에 둔다
			ProjectOne.Unit.AI.HeroAutoBehavior.AllowAttackWhileMoving = _attackWhileMoving;

			_viewTimer += Time.unscaledDeltaTime;
			if (_viewTimer < _viewRefreshInterval)
			{
				return;
			}

			_viewTimer = 0f;
			rebuildViews();
		}

		// ── [임시] 자석펫 지급 ───────
		//
		// 펫 콜렉션/보유 데이터가 아직 없어서 "가지고 있다"고 가정하고 붙여 준다.
		// 정식 시스템이 들어오면 이 구역과 _spawnPet / PetAddress / _onUnitSpawned / _petInstance 를 함께 지운다.

		private void onUnitSpawned(UnitSpawnedEvent evt)
		{
			if (_spawnPet == false || evt.UnitType != ProjectOne.Unit.UnitType.Hero)
			{
				return;
			}

			spawnPetAsync(evt.Unit, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private async UniTask spawnPetAsync(ProjectOne.Unit.UnitBase hero, CancellationToken ct)
		{
			// 히어로가 새로 만들어졌으므로 이전 펫은 버린다 (씬 전환 시엔 씬과 함께 이미 사라져 있다)
			releasePet();

			(bool canceled, GameObject prefab) = await ResourceManager.Instance.AcquireAsync<GameObject>(PetAddress, ct).SuppressCancellationThrow();
			if (canceled == true)
			{
				return;
			}

			if (prefab == null)
			{
				Debug.LogError($"[DevTester] 펫 프리팹 로드 실패: {PetAddress}");
				return;
			}

			_isPetHandleHeld = true;

			// await 사이에 히어로가 사라졌으면(씬 전환 등) 붙일 대상이 없다 — 핸들만 돌려준다
			if (hero == null)
			{
				releasePet();
				return;
			}

			// 부모를 두지 않는다 — 활성 씬에 속하므로 씬 전환 시 함께 파괴된다
			_petInstance = Instantiate(prefab);

			ProjectOne.Unit.Pet pet = _petInstance.GetComponent<ProjectOne.Unit.Pet>();
			if (pet == null)
			{
				Debug.LogError($"[DevTester] {PetAddress} 에 Pet 컴포넌트가 없다 — 펫이 따라다니지 않는다.");
				return;
			}

			pet.SetOwner(hero);
		}

		// 펫 인스턴스 파괴 + 프리팹 핸들 반납. 재스폰과 종료 양쪽에서 쓴다.
		private void releasePet()
		{
			if (_petInstance != null)
			{
				Destroy(_petInstance);
				_petInstance = null;
			}

			// 종료 중에는 ResourceManager 가 먼저 사라져 있을 수 있다
			if (_isPetHandleHeld == false || ResourceManager.HasInstance == false)
			{
				return;
			}

			_isPetHandleHeld = false;
			ResourceManager.Instance.Release(PetAddress);
		}

		// ── 개발 데이터 주입 (데이터 로드 후 Account 오버라이드) ───────

		// DataLoadState 가 로드/시작데이터 보정을 마친 뒤 호출된다.
		// 켜져 있으면 인스펙터 구성 데이터로 Account 를 덮어쓴다(메모리만 — save 미호출, Backnd 비오염).
		private void onDataLoaded(DataLoadedEvent evt)
		{
			if (_disabled == true)
			{
				return;
			}

			Account.Instance.SetInventory(buildInventory());
			Account.Instance.SetLoadout(buildLoadout());
			Account.Instance.SetMastery(buildMastery());
			Account.Instance.SetCostume(buildCostume());
			Account.Instance.SetCurrency(buildCurrency());
			Debug.Log("[DevTester] 개발 데이터 오버라이드 — Level:" + _characterLevel
				+ ", 장착:" + _equipSlots.Count + "칸, 보유장비:" + _ownedEquipments.Count + "개"
				+ ", 보유아이템:" + _ownedItems.Count + "종, 코스튬:" + _ownedCostumes.Count + "종"
				+ ", 재화:" + _currencies.Count + "종");
		}

		// 코스튬 개발 데이터 — 보유 목록과 착용 ID.
		//
		// CostumeBook 은 보유하지 않은 착용 ID 를 버리므로(서버 데이터 불일치 방어),
		// 여기서는 착용으로 지정한 것을 보유에 자동으로 넣는다. 목록에 두 번 적지 않아도 되게 하려는 것이다.
		private CostumeDto buildCostume()
		{
			CostumeDto dto = new CostumeDto();

			for (int i = 0; i < _ownedCostumes.Count; i++)
			{
				int id = _ownedCostumes[i];
				if (id > 0 && dto.owned.Contains(id) == false)
				{
					dto.owned.Add(id);
				}
			}

			if (_equippedBodyCostume > 0 && dto.owned.Contains(_equippedBodyCostume) == false)
			{
				dto.owned.Add(_equippedBodyCostume);
			}

			if (_equippedWeaponCostume > 0 && dto.owned.Contains(_equippedWeaponCostume) == false)
			{
				dto.owned.Add(_equippedWeaponCostume);
			}

			dto.equippedBodyId = _equippedBodyCostume;
			dto.equippedWeaponId = _equippedWeaponCostume;
			return dto;
		}

		// 보유 재화 개발 데이터.
		//
		// Account.SetCurrency 는 Wallet 을 통째로 새로 만들므로 ResourceChangeEvent 가 나가지 않는다.
		// 부팅 시점이라 HUD 가 아직 없어 문제되지 않는다 — 런타임에 바꾸려면 CurrencyManager.SetAmount 를 써야 한다.
		private CurrencyDto buildCurrency()
		{
			CurrencyDto dto = new CurrencyDto();
			for (int i = 0; i < _currencies.Count; i++)
			{
				DevCurrency src = _currencies[i];
				if (src.type == EDT.Currency.None || src.amount < 0)
				{
					continue;
				}

				CurrencyAmountDto existing = findCurrency(dto, src.type);
				if (existing != null)
				{
					existing.amount += src.amount;
					continue;
				}

				CurrencyAmountDto entry = new CurrencyAmountDto();
				entry.currencyId = (int)src.type;
				entry.amount = src.amount;
				dto.amounts.Add(entry);
			}

			return dto;
		}

		private static CurrencyAmountDto findCurrency(CurrencyDto dto, EDT.Currency type)
		{
			for (int i = 0; i < dto.amounts.Count; i++)
			{
				if (dto.amounts[i].currencyId == (int)type)
				{
					return dto.amounts[i];
				}
			}

			return null;
		}

		// 마스터리 레벨은 누적 경험치에서 파생되므로, 원하는 레벨의 누적값을 역으로 넣는다.
		private MasteryDto buildMastery()
		{
			MasteryDto dto = new MasteryDto();
			for (int i = 0; i < _masteries.Count; i++)
			{
				DevMastery src = _masteries[i];
				if (src.mastery == WeaponMastery.None || src.level <= 0)
				{
					continue;
				}

				MasteryProgressDto entry = new MasteryProgressDto();
				entry.masteryId = (int)src.mastery;
				entry.level = src.level;
				entry.totalExp = MasteryCatalog.GetMasteryTotalExp(src.level);
				dto.masteries.Add(entry);
			}

			return dto;
		}

		private InventoryDto buildInventory()
		{
			InventoryDto inventory = new InventoryDto();

			// 장착분이 UID 앞자리를 쓰고(buildLoadout 이 같은 인덱스로 UID 를 계산한다),
			// 보유분은 그 뒤로 이어붙인다 — 두 목록의 UID 가 겹치면 인스턴스가 서로를 덮어쓴다.
			addEquipments(inventory, _equipSlots, true, 0);
			addEquipments(inventory, _ownedEquipments, false, _equipSlots.Count);
			addItems(inventory);

			inventory.nextEquipmentUid = devUid(_equipSlots.Count + _ownedEquipments.Count);
			return inventory;
		}

		// 장비 목록을 인벤토리에 담는다. equipped 면 테이블이 정한 부위에 착용시키고, 아니면 보유만 한다.
		private void addEquipments(InventoryDto inventory, List<DevSlot> sources, bool equipped, int uidOffset)
		{
			for (int i = 0; i < sources.Count; i++)
			{
				DevSlot src = sources[i];
				if (src.itemId <= 0)
				{
					continue;
				}

				Table_Equipment.Row row = Table_Equipment.Get(src.itemId);
				if (row == null)
				{
					Debug.LogWarning("[DevTester] Equipment 행이 없는 아이템 — 건너뜁니다: " + src.itemId);
					continue;
				}

				EquipmentInstanceDto dto = new EquipmentInstanceDto();
				dto.uid = devUid(uidOffset + i);
				dto.itemId = src.itemId;
				dto.grade = (int)(src.grade != ItemGradeType.None ? src.grade : ItemGradeType.Normal);
				dto.level = src.level > 0 ? src.level : 1;
				dto.purity = (int)(src.purity != EquipPurity.None ? src.purity : EquipPurity.Purity_3);
				dto.quality = src.quality;
				dto.equippedSlot = equipped ? (int)row.EquipSlotType : 0;
				inventory.equipments.Add(dto);
			}
		}

		// 스택 아이템을 인벤토리에 담는다. 같은 ID 를 여러 줄에 적으면 개수를 합친다.
		private void addItems(InventoryDto inventory)
		{
			for (int i = 0; i < _ownedItems.Count; i++)
			{
				DevItem src = _ownedItems[i];
				if (src.itemId <= 0 || src.count <= 0)
				{
					continue;
				}

				if (Table_Item.Get(src.itemId) == null)
				{
					Debug.LogWarning("[DevTester] Item 행이 없는 아이템 — 건너뜁니다: " + src.itemId);
					continue;
				}

				OwnedItemDto existing = findItem(inventory, src.itemId);
				if (existing != null)
				{
					existing.count += src.count;
					continue;
				}

				OwnedItemDto dto = new OwnedItemDto();
				dto.itemId = src.itemId;
				dto.count = src.count;
				inventory.items.Add(dto);
			}
		}

		private static OwnedItemDto findItem(InventoryDto inventory, int itemId)
		{
			for (int i = 0; i < inventory.items.Count; i++)
			{
				if (inventory.items[i].itemId == itemId)
				{
					return inventory.items[i];
				}
			}

			return null;
		}

		private LoadoutDto buildLoadout()
		{
			LoadoutDto dto = new LoadoutDto();
			dto.level = _characterLevel > 0 ? _characterLevel : 1;
			dto.exp = 0;
			for (int i = 0; i < _equipSlots.Count; i++)
			{
				DevSlot src = _equipSlots[i];
				if (src.itemId <= 0)
				{
					continue;
				}

				// 착용 슬롯은 인스펙터 값이 아니라 테이블이 정한다 — 오타로 엉뚱한 칸에 끼는 것을 막는다.
				Table_Equipment.Row row = Table_Equipment.Get(src.itemId);
				if (row == null || row.EquipSlotType == EquipSlotTypes.None)
				{
					continue;
				}

				if ((int)row.EquipSlotType >= LoadoutDto.SlotCount)
				{
					continue;
				}

				dto.slots[(int)row.EquipSlotType] = devUid(i);
			}

			return dto;
		}

		// 개발 데이터 UID — 인스펙터 순서에 1:1 대응시켜 재현 가능하게 둔다.
		private static long devUid(int index)
		{
			return index + 1;
		}

		// ── 런타임 조회 뷰 갱신 ───────────────────────────────────────

		private void rebuildViews()
		{
			rebuildCharacter();
			rebuildCurrencies();
			rebuildInventory();
		}

		private void rebuildCharacter()
		{
			Loadout loadout = Account.Instance.Loadout;
			_viewCharacterLevel = loadout.Level;
			_viewCharacterExp = loadout.Exp;
		}

		private void rebuildCurrencies()
		{
			_viewCurrencies.Clear();
			if (CurrencyManager.HasInstance == false)
			{
				return;
			}

			for (int i = 0; i < _currencyTypes.Length; i++)
			{
				EDT.Currency type = _currencyTypes[i];
				if (type == EDT.Currency.None)
				{
					continue;
				}

				CurrencyView view = new CurrencyView();
				view.type = type;
				view.amount = CurrencyManager.Instance.GetAmount(type);
				_viewCurrencies.Add(view);
			}
		}

		private void rebuildInventory()
		{
			Inventory inventory = Account.Instance.Inventory;

			_viewItems.Clear();
			IReadOnlyList<OwnedItem> items = inventory.GetAllItems();
			for (int i = 0; i < items.Count; i++)
			{
				OwnedItem owned = items[i];
				if (owned == null || owned.itemId <= 0)
				{
					continue;
				}

				Table_Item.Row row = Table_Item.Get(owned.itemId);

				ItemView view = new ItemView();
				view.itemId = owned.itemId;
				view.name = (row != null) ? row.Name : string.Empty;
				view.count = owned.count;
				_viewItems.Add(view);
			}

			_viewEquipments.Clear();
			IReadOnlyList<EquipmentInstance> equipments = inventory.GetAllEquipments();
			for (int i = 0; i < equipments.Count; i++)
			{
				EquipmentInstance instance = equipments[i];
				Table_Item.Row row = instance.Item;

				EquipmentView view = new EquipmentView();
				view.uid = instance.uid;
				view.name = (row != null) ? row.Name : string.Empty;
				view.grade = instance.grade;
				view.level = instance.level;
				view.equippedSlot = instance.equippedSlot;
				_viewEquipments.Add(view);
			}
		}
	}
}
