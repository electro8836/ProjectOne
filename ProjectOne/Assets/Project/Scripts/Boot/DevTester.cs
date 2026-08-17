using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Mastery;
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

		[Header("마스터리 진행도 (무기 + 레벨)")]
		[SerializeField] private List<DevMastery> _masteries = new List<DevMastery>();

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
		}

		protected override void OnDestroy()
		{
			if (this == Instance && _onDataLoaded != null)
			{
				EventManager.Instance.Unsubscribe<DataLoadedEvent>(_onDataLoaded);
			}

			base.OnDestroy();
		}

		private void Update()
		{
			_viewTimer += Time.unscaledDeltaTime;
			if (_viewTimer < _viewRefreshInterval)
			{
				return;
			}

			_viewTimer = 0f;
			rebuildViews();
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
			Debug.Log("[DevTester] 개발 데이터 오버라이드 — Level:" + _characterLevel + ", 장착:" + _equipSlots.Count + "칸");
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
			for (int i = 0; i < _equipSlots.Count; i++)
			{
				DevSlot src = _equipSlots[i];
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
				dto.uid = devUid(i);
				dto.itemId = src.itemId;
				dto.grade = (int)(src.grade != ItemGradeType.None ? src.grade : ItemGradeType.Normal);
				dto.level = src.level > 0 ? src.level : 1;
				dto.purity = (int)(src.purity != EquipPurity.None ? src.purity : EquipPurity.Purity_3);
				dto.quality = src.quality;
				dto.equippedSlot = (int)row.EquipSlotType;
				inventory.equipments.Add(dto);
			}

			inventory.nextEquipmentUid = devUid(_equipSlots.Count);
			return inventory;
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
