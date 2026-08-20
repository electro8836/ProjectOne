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
	// 장비는 인스턴스 단위라 테이블 행이 아니라 인스턴스를 그대로 넘긴다.
	public struct EquipmentSlotData
	{
		public EquipmentInstance instance;
		public bool equipped;
	}

	// 장착 슬롯 렌더 데이터 — instance 가 null 이면 빈 슬롯.
	public struct EquippedSlotData
	{
		public EquipSlotTypes type;
		public EquipmentInstance instance;
	}

	// 장비 화면 Presenter — 정렬/필터 상태와 Model(Account) 조회를 담당하고, View 에 렌더 데이터를 넘긴다.
	// 화면 닫힘 시 장착 변경(dirty)을 서버에 1회 flush 한다.
	public sealed class EquipmentPresenter : Presenter<EquipmentUI>
	{
		private const string ITEM_INFO_POPUP_ADDRESS = "Prefab_ItemInfoPopup";

		private EquipSlotTypes _currentSlot = EquipSlotTypes.Weapon;
		private bool _descending = true;	// true=신화→일반 (기본)

		private readonly List<EquipmentSlotData> _gridData = new List<EquipmentSlotData>();
		private readonly List<EquippedSlotData> _equippedData = new List<EquippedSlotData>();
		private readonly List<EquipmentInstance> _slotBuffer = new List<EquipmentInstance>();

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		protected override void OnInitialize()
		{
			view.OnCloseRequested += onCloseRequested;
			view.OnTabSelected += onTabSelected;
			view.OnSortToggled += onSortToggled;
			view.OnSlotClicked += onSlotClicked;

			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
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

			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Unsubscribe<PresetChangeEvent>(onPresetChanged);
		}

		// 아이콘은 부트에서 아틀라스로 상주하므로 프리로드 대기 없이 곧바로 슬롯을 만든다.
		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_currentSlot = EquipSlotTypes.Weapon;
			view.SelectTab(0);	// Select 는 OnTabChanged 를 발행하지 않으므로 직접 rebuild
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

		private void onCloseRequested()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onTabSelected(int index)
		{
			_currentSlot = indexToSlot(index);
			rebuild();
		}

		private void onSortToggled()
		{
			_descending = !_descending;
			rebuild();
		}

		// 슬롯 클릭 → 아이템 정보 팝업을 상위 캔버스에 연다(네비게이션 결정은 Presenter).
		private void onSlotClicked(long uid)
		{
			UIManager.Instance.ShowItemInfoPopupAsync(ITEM_INFO_POPUP_ADDRESS, uid, view.GetDestroyToken()).Forget();
		}

		private void onEquipmentChanged(EquipmentChangeEvent e)
		{
			rebuild();
		}

		private void onPresetChanged(PresetChangeEvent e)
		{
			rebuild();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 현재 슬롯의 장비 전체를 정렬해 슬롯 데이터로 만들고 View 에 렌더를 지시한다(이전 rebuild 는 취소).
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

		// 그리드는 **보유 인스턴스**만 나열한다. 미보유 아이템을 회색으로 늘어놓던 구조는
		// 인스턴스 단위 관리로 바뀌면서 성립하지 않는다(같은 아이템의 개체가 여럿일 수 있다).
		private void buildGridData()
		{
			long equippedUid = Account.Instance.Loadout.GetSlot(_currentSlot);
			Account.Instance.Inventory.CollectBySlot(_currentSlot, _slotBuffer);

			_gridData.Clear();
			for (int i = 0; i < _slotBuffer.Count; i++)
			{
				EquipmentSlotData data;
				data.instance = _slotBuffer[i];
				data.equipped = data.instance.uid == equippedUid;
				_gridData.Add(data);
			}

			_gridData.Sort(compareData);
		}

		// 등급 정렬(방향 적용) → 동급은 강화 레벨 내림차순 → 그래도 같으면 UID 오름차순.
		private int compareData(EquipmentSlotData a, EquipmentSlotData b)
		{
			int ga = (int)a.instance.grade;
			int gb = (int)b.instance.grade;
			if (ga != gb)
			{
				return _descending ? gb.CompareTo(ga) : ga.CompareTo(gb);
			}

			if (a.instance.level != b.instance.level)
			{
				return b.instance.level.CompareTo(a.instance.level);
			}

			return a.instance.uid.CompareTo(b.instance.uid);
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

		// 탭 인덱스 → 착용 슬롯. EquipSlotTypes 는 1부터 시작하므로 +1 한다.
		private EquipSlotTypes indexToSlot(int index)
		{
			int value = index + 1;
			if (value < 1 || value >= ProjectOne.Shared.LoadoutDto.SlotCount)
			{
				return EquipSlotTypes.Weapon;
			}

			return (EquipSlotTypes)value;
		}
	}
}
