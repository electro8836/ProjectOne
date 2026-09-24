using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Items;
using ProjectOne.Upgrade;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 제작 모드 — 프리펩 TabMenu_Side 의 Hierarchy 순서와 일대일로 맞춘다.
	public enum CraftMode
	{
		Enhance,
		Promote,
		Transfer,
	}

	// 장비를 올려 두는 등록 칸.
	public enum CraftSlotRoot
	{
		Enhance,		// Enhance/ItemRoot
		PromotePrev,	// Promote/PrevItemRoot — 현재 장비
		PromoteNext,	// Promote/NextItemRoot — 승급 후 미리보기
		TransferSource,	// Transfer/SourceItemRoot
		TransferTarget,	// Transfer/TargetItemRoot
	}

	// 그리드 슬롯 1칸 렌더 데이터 — 장착 여부는 인스턴스가 아니라 Loadout 이 정하므로 함께 넘긴다.
	public struct CraftSlotData
	{
		public EquipmentInstance instance;
		public bool equipped;
	}

	// 제작 화면 Presenter — 모드·필터 탭 상태와 등록된 장비를 들고, 강화·승급·전이 로직(ProjectOne.Upgrade)을 호출한다.
	public sealed class CraftPresenter : Presenter<CraftUI>
	{
		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";

		// 필터 탭 인덱스 — 프리펩 Bottom/TabMenu_Middle 의 Hierarchy 순서와 일대일로 맞춘다.
		private const int TAB_ALL = 0;
		private const int TAB_WEAPON = 1;
		private const int TAB_ARMOR = 2;
		private const int TAB_ACCESSORY = 3;
		private const int TAB_RELIC = 4;

		private const string MSG_MAX_LEVEL = "최대 강화 레벨입니다.";
		private const string MSG_MAX_GRADE = "최대 등급입니다.";
		private const string MSG_NOT_MAX_LEVEL = "강화 레벨을 최대로 올려야 승급할 수 있습니다.";

		// 그리드 정렬 기준 — Button_Sorting 클릭 시 선언 순서대로 순환한다 (EquipmentPresenter 와 같은 규칙).
		private enum SortModes
		{
			Grade,		// 등급순(기본)
			Enhance,	// 강화도순
			Quality,	// 품질순
		}

		private const int SORT_MODE_COUNT = 3;
		private const string SORT_MODE_PREF_KEY = "CraftSortMode";

		private CraftMode _mode = CraftMode.Enhance;
		private int _filterTab = TAB_ALL;
		private SortModes _sortMode = SortModes.Grade;

		// 모드별 등록 장비 UID. 0 이면 빈 칸.
		private long _enhanceUid;
		private long _promoteUid;
		private long _sourceUid;
		private long _targetUid;

		// 이번 rebuild 끝에 실행 성공 연출을 재생할지. 실행 버튼에서만 채운다.
		private bool _pendingPop;

		private readonly List<CraftSlotData> _gridData = new List<CraftSlotData>();
		private readonly List<UpgradeCost> _costs = new List<UpgradeCost>(3);

		// 승급 후 미리보기용 가상 인스턴스. 인벤토리에 넣지 않고 표시에만 쓴다.
		private readonly EquipmentInstance _previewInstance = new EquipmentInstance();

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		protected override void OnInitialize()
		{
			view.OnModeSelected += onModeSelected;
			view.OnFilterSelected += onFilterSelected;
			view.OnGridSlotClicked += onGridSlotClicked;
			view.OnRegisteredSlotClicked += onRegisteredSlotClicked;
			view.OnActionClicked += onActionClicked;
			view.OnCancelClicked += onCancelClicked;
			view.OnHomeClicked += onHomeClicked;
			view.OnSortClicked += onSortClicked;

			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Subscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);

			loadSortMode();
		}

		protected override void OnDispose()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
				_rebuildCts = null;
			}

			view.OnModeSelected -= onModeSelected;
			view.OnFilterSelected -= onFilterSelected;
			view.OnGridSlotClicked -= onGridSlotClicked;
			view.OnRegisteredSlotClicked -= onRegisteredSlotClicked;
			view.OnActionClicked -= onActionClicked;
			view.OnCancelClicked -= onCancelClicked;
			view.OnHomeClicked -= onHomeClicked;
			view.OnSortClicked -= onSortClicked;

			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
			EventManager.Instance.Unsubscribe<InventoryChangeEvent>(onInventoryChanged);
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		// 열 때마다 강화 모드·전체 탭·빈 등록 칸에서 시작한다.
		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_mode = CraftMode.Enhance;
			_filterTab = TAB_ALL;
			clearAllRegistrations();

			view.SelectModeTab((int)_mode);	// Select 는 OnTabChanged 를 발행하지 않으므로 직접 rebuild
			view.SelectFilterTab(_filterTab);
			view.ShowMode(_mode);
			view.SetSortLabel(getSortLabel(_sortMode));

			rebuild();
			return UniTask.CompletedTask;
		}

		// 모드를 지정하고 그 모드의 등록 칸에 장비를 올린 채로 시작한다 (장비 정보 팝업의 강화/승급 버튼).
		// 전이는 원본·대상 두 칸이라 한 장비로 미리 채울 대상이 정해지지 않는다 — 강화·승급만 받는다.
		public void Preselect(CraftMode mode, long uid)
		{
			_mode = mode;
			clearAllRegistrations();

			if (mode == CraftMode.Enhance)
			{
				_enhanceUid = uid;
			}
			else if (mode == CraftMode.Promote)
			{
				_promoteUid = uid;
			}

			view.SelectModeTab((int)_mode);
			view.ShowMode(_mode);
			rebuild();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		// 모드를 바꾸면 모든 등록을 푼다 — 다른 모드에 올려 둔 장비가 그리드에서 계속 빠져 있지 않게.
		private void onModeSelected(int index)
		{
			_mode = (CraftMode)index;
			clearAllRegistrations();

			view.ShowMode(_mode);
			rebuild();
		}

		private void onFilterSelected(int index)
		{
			_filterTab = index;
			rebuild();
		}

		// 정렬 버튼 — 기준을 다음 것으로 넘기고 라벨을 갱신한 뒤 다시 그린다.
		private void onSortClicked()
		{
			_sortMode = (SortModes)(((int)_sortMode + 1) % SORT_MODE_COUNT);
			UnityEngine.PlayerPrefs.SetInt(SORT_MODE_PREF_KEY, (int)_sortMode);
			UnityEngine.PlayerPrefs.Save();

			view.SetSortLabel(getSortLabel(_sortMode));
			rebuild();
		}

		// 그리드 클릭 — 현재 모드의 등록 칸에 올린다. 이미 차 있으면 교체한다.
		// 전이는 원본이 비었으면 원본, 아니면 대상 칸에 올린다.
		private void onGridSlotClicked(long uid)
		{
			switch (_mode)
			{
			case CraftMode.Enhance:
				_enhanceUid = uid;
				break;

			case CraftMode.Promote:
				_promoteUid = uid;
				break;

			case CraftMode.Transfer:
				if (_sourceUid == 0)
				{
					_sourceUid = uid;
				}
				else
				{
					_targetUid = uid;
				}
				break;
			}

			rebuild();
		}

		// 등록 칸 클릭 — 올려 둔 장비의 정보 팝업을 읽기 전용으로 연다.
		// 읽기 전용 오버로드는 인벤토리를 조회하지 않으므로 승급 미리보기(가상 인스턴스)도 열린다.
		private void onRegisteredSlotClicked(CraftSlotRoot type)
		{
			EquipmentInstance instance = null;
			switch (type)
			{
			case CraftSlotRoot.Enhance:
				instance = findEquipment(_enhanceUid);
				break;

			case CraftSlotRoot.PromotePrev:
				instance = findEquipment(_promoteUid);
				break;

			case CraftSlotRoot.PromoteNext:
				// 미리보기 칸의 슬롯은 승급 가능할 때만 존재하므로 가상 인스턴스가 곧 표시 대상이다.
				instance = _previewInstance;
				break;

			case CraftSlotRoot.TransferSource:
				instance = findEquipment(_sourceUid);
				break;

			case CraftSlotRoot.TransferTarget:
				instance = findEquipment(_targetUid);
				break;
			}

			if (instance == null)
			{
				return;
			}

			UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, instance, view.GetDestroyToken()).Forget();
		}

		// 실행 — 성공하면 연출을 예약하고 다시 그린다.
		// 실행 중 재화·장비 이벤트로 rebuild 가 먼저 돌 수 있어, 연출은 실행이 끝난 뒤의 rebuild 에 싣는다.
		private void onActionClicked()
		{
			bool success = false;
			switch (_mode)
			{
			case CraftMode.Enhance:
				success = EquipmentUpgrade.TryEnhance(findEquipment(_enhanceUid));
				break;

			case CraftMode.Promote:
				success = EquipmentUpgrade.TryPromote(findEquipment(_promoteUid));
				break;

			case CraftMode.Transfer:
				success = EquipmentTransfer.TryTransfer(findEquipment(_sourceUid), findEquipment(_targetUid));
				break;
			}

			if (success == false)
			{
				return;
			}

			_pendingPop = true;
			rebuild();
		}

		// 현재 모드의 등록을 모두 푼다.
		private void onCancelClicked()
		{
			switch (_mode)
			{
			case CraftMode.Enhance:
				_enhanceUid = 0;
				break;

			case CraftMode.Promote:
				_promoteUid = 0;
				break;

			case CraftMode.Transfer:
				_sourceUid = 0;
				_targetUid = 0;
				break;
			}

			rebuild();
		}

		// 창을 닫는다. 마지막 창이면 WindowClosedEvent 가 발행되어 네비게이션 바의 탭 선택도 함께 풀린다.
		private void onHomeClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onEquipmentChanged(EquipmentChangeEvent e)
		{
			rebuild();
		}

		private void onInventoryChanged(InventoryChangeEvent e)
		{
			rebuild();
		}

		// 보유량이 바뀌면 비용 부족 표시와 버튼 상태가 달라진다.
		private void onResourceChanged(ResourceChangeEvent e)
		{
			rebuild();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

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
			// 사라진 장비(판매 등)가 등록 칸에 남지 않도록 먼저 정리한다.
			validateRegistrations();

			buildGridData();
			await view.RenderGridAsync(_gridData, ct);
			if (ct.IsCancellationRequested == true)
			{
				return;
			}

			switch (_mode)
			{
			case CraftMode.Enhance:
				await renderEnhanceAsync(ct);
				break;

			case CraftMode.Promote:
				await renderPromoteAsync(ct);
				break;

			case CraftMode.Transfer:
				await renderTransferAsync(ct);
				break;
			}

			if (ct.IsCancellationRequested == true)
			{
				return;
			}

			// 아이콘 바인딩이 끝난 뒤에 재생한다 — 바뀐 등급·레벨이 보이는 채로 커졌다 줄어야 한다.
			if (_pendingPop == true)
			{
				playPop();
				_pendingPop = false;
			}
		}

		private async UniTask renderEnhanceAsync(CancellationToken ct)
		{
			EquipmentInstance instance = findEquipment(_enhanceUid);
			await view.RenderRegisteredAsync(CraftSlotRoot.Enhance, instance, ct);

			string disableMessage = null;
			_costs.Clear();
			if (instance != null)
			{
				if (EquipmentUpgrade.CanEnhance(instance) == false)
				{
					disableMessage = MSG_MAX_LEVEL;
				}
				else
				{
					EquipmentUpgrade.GetEnhanceCost(instance, _costs);
				}
			}

			await view.RenderRequirementAsync(CraftMode.Enhance, instance != null, _costs, disableMessage, ct);

			bool canRun = disableMessage == null && EquipmentUpgrade.IsAffordable(_costs);
			view.SetActionInteractable(CraftMode.Enhance, instance != null && canRun);
			view.SetCancelVisible(CraftMode.Enhance, instance != null);
		}

		// 승급 가능하면 NextItemRoot 에 다음 등급 미리보기를, 불가하면 이유 문구와 빈 칸을 보여준다.
		private async UniTask renderPromoteAsync(CancellationToken ct)
		{
			EquipmentInstance instance = findEquipment(_promoteUid);
			await view.RenderRegisteredAsync(CraftSlotRoot.PromotePrev, instance, ct);

			string disableMessage = null;
			EquipmentInstance preview = null;
			_costs.Clear();
			if (instance != null)
			{
				PromoteBlock block = EquipmentUpgrade.GetPromoteBlock(instance);
				disableMessage = getPromoteMessage(block);
				if (block == PromoteBlock.None)
				{
					preview = buildPreview(instance);
					EquipmentUpgrade.GetPromoteCost(instance, _costs);
				}
			}

			await view.RenderRegisteredAsync(CraftSlotRoot.PromoteNext, preview, ct);
			await view.RenderRequirementAsync(CraftMode.Promote, instance != null, _costs, disableMessage, ct);

			bool canRun = preview != null && EquipmentUpgrade.IsAffordable(_costs);
			view.SetActionInteractable(CraftMode.Promote, canRun);
			view.SetCancelVisible(CraftMode.Promote, instance != null);
		}

		// 비용은 두 칸이 모두 찼을 때만 보인다.
		private async UniTask renderTransferAsync(CancellationToken ct)
		{
			EquipmentInstance source = findEquipment(_sourceUid);
			EquipmentInstance target = findEquipment(_targetUid);
			await view.RenderRegisteredAsync(CraftSlotRoot.TransferSource, source, ct);
			await view.RenderRegisteredAsync(CraftSlotRoot.TransferTarget, target, ct);

			bool ready = source != null && target != null;
			_costs.Clear();
			if (ready == true)
			{
				EquipmentTransfer.GetCost(source, target, _costs);
			}

			await view.RenderRequirementAsync(CraftMode.Transfer, ready, _costs, null, ct);

			view.SetActionInteractable(CraftMode.Transfer, ready == true && EquipmentTransfer.GetBlock(source, target) == TransferBlock.None);
			view.SetCancelVisible(CraftMode.Transfer, source != null || target != null);
		}

		private void playPop()
		{
			switch (_mode)
			{
			case CraftMode.Enhance:
				view.PlayPop(CraftSlotRoot.Enhance);
				break;

			case CraftMode.Promote:
				view.PlayPop(CraftSlotRoot.PromoteNext);
				break;

			case CraftMode.Transfer:
				view.PlayPop(CraftSlotRoot.TransferSource);
				view.PlayPop(CraftSlotRoot.TransferTarget);
				break;
			}
		}

		// 그리드는 보유 장비 전체(장착중 포함)에서 필터 탭에 맞고 현재 모드에 등록되지 않은 것만 나열한다.
		// 전이는 원본이 등록되면 그 원본의 대상이 될 수 있는 장비만 남긴다.
		private void buildGridData()
		{
			_gridData.Clear();

			Loadout loadout = Account.Instance.Loadout;
			IReadOnlyList<EquipmentInstance> all = Account.Instance.Inventory.GetAllEquipments();
			EquipmentInstance source = (_mode == CraftMode.Transfer) ? findEquipment(_sourceUid) : null;

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

				if (isRegistered(instance.uid) == true)
				{
					continue;
				}

				if (source != null && isTransferTarget(source, instance) == false)
				{
					continue;
				}

				CraftSlotData data;
				data.instance = instance;
				data.equipped = loadout.GetSlot(equip.EquipSlotType) == instance.uid;
				_gridData.Add(data);
			}

			sortGrid();
		}

		// 현재 정렬 기준에 맞는 비교자를 골라 그리드를 정렬한다.
		private void sortGrid()
		{
			switch (_sortMode)
			{
			case SortModes.Enhance:
				_gridData.Sort(compareByEnhance);
				break;

			case SortModes.Quality:
				_gridData.Sort(compareByQuality);
				break;

			default:
				_gridData.Sort(compareEquipment);
				break;
			}
		}

		// 대상 자격 — 같은 착용 부위, 다른 인스턴스, 교체할 차이가 있고 비용 행이 있는 것.
		// 재화 부족은 대상의 문제가 아니므로 걸러내지 않는다 (버튼만 막힌다).
		private bool isTransferTarget(EquipmentInstance source, EquipmentInstance candidate)
		{
			TransferBlock block = EquipmentTransfer.GetBlock(source, candidate);
			return block == TransferBlock.None || block == TransferBlock.NotEnough;
		}

		private bool isRegistered(long uid)
		{
			switch (_mode)
			{
			case CraftMode.Enhance:
				return uid == _enhanceUid;

			case CraftMode.Promote:
				return uid == _promoteUid;

			case CraftMode.Transfer:
				return uid == _sourceUid || uid == _targetUid;
			}

			return false;
		}

		// 착용 부위 → 필터 탭 (EquipmentPresenter.matchesTab 과 같은 매핑).
		private bool matchesTab(EquipSlotTypes slot)
		{
			switch (_filterTab)
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
		private int compareEquipment(CraftSlotData a, CraftSlotData b)
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

		// 강화 레벨 내림차순 → 동레벨은 등급 내림차순 → 그래도 같으면 UID 오름차순.
		private int compareByEnhance(CraftSlotData a, CraftSlotData b)
		{
			if (a.instance.level != b.instance.level)
			{
				return b.instance.level.CompareTo(a.instance.level);
			}

			return compareGradeThenUid(a, b);
		}

		// 품질 내림차순 → 동일 품질은 등급 내림차순 → 그래도 같으면 UID 오름차순.
		private int compareByQuality(CraftSlotData a, CraftSlotData b)
		{
			if (a.instance.quality != b.instance.quality)
			{
				return b.instance.quality.CompareTo(a.instance.quality);
			}

			return compareGradeThenUid(a, b);
		}

		// 강화도·품질 정렬의 공통 뒤순위 — 등급 내림차순 → UID 오름차순.
		private int compareGradeThenUid(CraftSlotData a, CraftSlotData b)
		{
			int ga = (int)a.instance.grade;
			int gb = (int)b.instance.grade;
			if (ga != gb)
			{
				return gb.CompareTo(ga);
			}

			return a.instance.uid.CompareTo(b.instance.uid);
		}

		// 저장된 정렬 기준을 불러온다. 범위 밖 값은 기본값(등급순)으로 되돌린다.
		private void loadSortMode()
		{
			int saved = UnityEngine.PlayerPrefs.GetInt(SORT_MODE_PREF_KEY, (int)SortModes.Grade);
			if (saved < 0 || saved >= SORT_MODE_COUNT)
			{
				saved = (int)SortModes.Grade;
			}

			_sortMode = (SortModes)saved;
		}

		// 정렬 버튼에 표시할 기준명.
		private string getSortLabel(SortModes mode)
		{
			switch (mode)
			{
			case SortModes.Enhance:
				return "강화도순";

			case SortModes.Quality:
				return "품질순";
			}

			return "등급순";
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 승급 후 모습 — 등급만 다음 것으로 바꾼다 (레벨·품질은 승급에서 유지된다).
		private EquipmentInstance buildPreview(EquipmentInstance instance)
		{
			_previewInstance.uid = 0;
			_previewInstance.itemId = instance.itemId;
			_previewInstance.grade = EquipmentUpgrade.GetNextGrade(instance);
			_previewInstance.level = instance.level;
			_previewInstance.quality = instance.quality;
			return _previewInstance;
		}

		private string getPromoteMessage(PromoteBlock block)
		{
			switch (block)
			{
			case PromoteBlock.MaxGrade:
				return MSG_MAX_GRADE;

			case PromoteBlock.NotMaxLevel:
				return MSG_NOT_MAX_LEVEL;
			}

			return null;
		}

		private EquipmentInstance findEquipment(long uid)
		{
			if (uid == 0)
			{
				return null;
			}

			return Account.Instance.Inventory.GetEquipment(uid);
		}

		private void validateRegistrations()
		{
			if (findEquipment(_enhanceUid) == null)
			{
				_enhanceUid = 0;
			}

			if (findEquipment(_promoteUid) == null)
			{
				_promoteUid = 0;
			}

			if (findEquipment(_sourceUid) == null)
			{
				_sourceUid = 0;
			}

			if (findEquipment(_targetUid) == null)
			{
				_targetUid = 0;
			}
		}

		private void clearAllRegistrations()
		{
			_enhanceUid = 0;
			_promoteUid = 0;
			_sourceUid = 0;
			_targetUid = 0;
			_pendingPop = false;
		}
	}
}
