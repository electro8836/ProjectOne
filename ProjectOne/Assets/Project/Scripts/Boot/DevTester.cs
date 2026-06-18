using System.Collections.Generic;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.ServerData;
using ProjectOne.UserData;

namespace ProjectOne.Boot
{
	// 개발/테스트용 객체 (MonoSingleton, 부트씬 1개 배치).
	// - 최초 실행(Awake)에 인스펙터 구성값으로 메모리 Repository 를 끼워 개발 데이터를 주입한다("사용안함" 체크 시 생략).
	// - 게임 진행 중에는 Account 의 아이템/재료/재화/캐릭터 경험치를 주기적으로 인스펙터에 표시한다(읽기 전용 뷰).
	// 모든 Awake 는 Start 보다 먼저 실행되므로 GameBootstrapper.Start(흐름 시작) 이전에 주입이 끝난다.
	public class DevTester : MonoSingleton<DevTester>
	{
		// 장착 슬롯 1칸 설정 — 아이템 ID 와 강화 레벨
		[System.Serializable]
		public struct DevSlot
		{
			public int itemId;
			public int enhanceLevel;
		}

		// ── 인스펙터 표시용 뷰 항목 (읽기 전용) ──────────────────────
		[System.Serializable]
		public struct ItemView
		{
			public int itemId;
			public string name;
			public int count;
			public int enhanceLevel;
		}

		[System.Serializable]
		public struct CurrencyView
		{
			public CurrencyInfo type;
			public int amount;
		}

		[System.Serializable]
		public struct CharacterView
		{
			public int characterId;
			public string name;
			public int level;
			public int exp;
		}

		[Header("사용안함 — 체크 시 서버/로컬 데이터 사용")]
		[SerializeField] private bool _disabled;

		[Header("캐릭터")]
		[SerializeField] private int _characterId;
		[SerializeField] private int _characterLevel = 1;

		[Header("장착 슬롯")]
		[SerializeField] private DevSlot _weapon;
		[SerializeField] private DevSlot _armor;
		[SerializeField] private DevSlot _accessory;

		[Header("런타임 조회 (읽기 전용)")]
		[SerializeField] private float _viewRefreshInterval = 0.5f;
		[SerializeField] private List<CharacterView> _viewCharacters = new List<CharacterView>();
		[SerializeField] private List<CurrencyView> _viewCurrencies = new List<CurrencyView>();
		[SerializeField] private List<ItemView> _viewItems = new List<ItemView>();
		[SerializeField] private List<ItemView> _viewMaterials = new List<ItemView>();

		private CurrencyInfo[] _currencyTypes;
		private float _viewTimer;
		private System.Action<DataLoadedEvent> _onDataLoaded;

		protected override void Awake()
		{
			base.Awake();
			if (this != Instance)
			{
				return;
			}

			_currencyTypes = (CurrencyInfo[])System.Enum.GetValues(typeof(CurrencyInfo));

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

			if (_characterId <= 0)
			{
				Debug.LogWarning("[DevTester] _characterId 가 설정되지 않아 개발 데이터를 주입하지 않습니다. (서버/로컬 데이터로 진행)");
				return;
			}

			Account.Instance.SetInventory(buildInventory());
			Account.Instance.SetCharacter(buildCharacter());
			Account.Instance.SetSkill(new SkillData());
			Debug.Log("[DevTester] 개발 데이터 오버라이드 — Character:" + _characterId + ", Level:" + _characterLevel);
		}

		private InventoryData buildInventory()
		{
			InventoryData inventory = new InventoryData();
			addOwnedItem(inventory, _weapon);
			addOwnedItem(inventory, _armor);
			addOwnedItem(inventory, _accessory);
			return inventory;
		}

		private static void addOwnedItem(InventoryData inventory, DevSlot slot)
		{
			if (slot.itemId <= 0)
			{
				return;
			}

			OwnedItem item = new OwnedItem();
			item.itemId = slot.itemId;
			item.count = 1;
			item.enhanceLevel = slot.enhanceLevel;
			inventory.items.Add(item);
		}

		private CharacterData buildCharacter()
		{
			EquipPreset preset = new EquipPreset();
			preset.weaponItemId = _weapon.itemId;
			preset.armorItemId = _armor.itemId;
			preset.accessoryItemId = _accessory.itemId;

			OwnedCharacter oc = new OwnedCharacter();
			oc.characterId = _characterId;
			oc.grade = 1;
			oc.level = _characterLevel;
			oc.exp = 0;
			oc.awakenLevel = 0;
			oc.dupCount = 0;
			oc.preset = preset;

			CharacterData character = new CharacterData();
			character.characters.Add(oc);
			character.selectedCharacterId = _characterId;
			return character;
		}

		// ── 런타임 조회 뷰 갱신 ───────────────────────────────────────

		private void rebuildViews()
		{
			rebuildCharacters();
			rebuildCurrencies();
			rebuildInventory();
		}

		private void rebuildCharacters()
		{
			_viewCharacters.Clear();
			IReadOnlyList<OwnedCharacter> characters = Account.Instance.Loadout.GetAll();
			for (int i = 0; i < characters.Count; i++)
			{
				OwnedCharacter oc = characters[i];
				if (oc == null)
				{
					continue;
				}

				CharacterView view = new CharacterView();
				view.characterId = oc.characterId;
				view.name = characterName(oc.characterId);
				view.level = oc.level;
				view.exp = oc.exp;
				_viewCharacters.Add(view);
			}
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
				CurrencyInfo type = _currencyTypes[i];
				if (type == CurrencyInfo.None)
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
			_viewItems.Clear();
			_viewMaterials.Clear();
			IReadOnlyList<OwnedItem> items = Account.Instance.Inventory.GetAll();
			for (int i = 0; i < items.Count; i++)
			{
				OwnedItem owned = items[i];
				if (owned == null || owned.itemId <= 0)
				{
					continue;
				}

				Table_Material.Row material = Table_Material.Get(owned.itemId);
				if (material != null)
				{
					_viewMaterials.Add(makeItemView(owned, material.Name));
					continue;
				}

				Table_Equipment.Row equip = Table_Equipment.Get(owned.itemId);
				string name = (equip != null) ? equip.Name : string.Empty;
				_viewItems.Add(makeItemView(owned, name));
			}
		}

		private static ItemView makeItemView(OwnedItem owned, string name)
		{
			ItemView view = new ItemView();
			view.itemId = owned.itemId;
			view.name = name;
			view.count = owned.count;
			view.enhanceLevel = owned.enhanceLevel;
			return view;
		}

		private static string characterName(int characterId)
		{
			Table_Character.Row row = Table_Character.Get(characterId);
			return (row != null) ? row.Name : string.Empty;
		}
	}
}
