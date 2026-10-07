using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using ProjectOne.Upgrade;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 기본 옵션 1줄 — 부모 스탯 아이콘 + 색이 입혀진 문구.
	public struct OptionLine
	{
		public string iconAddress;
		public string text;
	}

	// 등급 해금 옵션 1줄 — 등급 칸 하나에 대응한다.
	// hasOption 이 false 면 그 등급엔 표시할 해금 옵션이 없다(칸을 통째로 감춘다).
	public struct GradeOptionLine
	{
		public ItemGradeType grade;
		public bool unlocked;
		public bool hasOption;
		public string text;
		public string rangeText;
		public bool hasRange;
	}

	// 아이템 정보 팝업 Presenter — 표시 데이터 계산과 장착/해제 결정(Loadout 조작)을 담당한다.
	// 대상은 아이템 ID 가 아니라 **장비 인스턴스 UID** 다. 같은 아이템이라도 등급·강화·순도·품질이 다르다.
	// 장착 변경은 Loadout 이 dirty 로 누적하고, 실제 서버 저장은 장비 화면 닫힘 시 1회 flush 된다.
	public sealed class ItemInfoPresenter : Presenter<ItemInfoPopup>
	{
		private long _uid;
		private EquipSlotTypes _slot = EquipSlotTypes.None;

		// 강화 버튼이 열 제작 모드. _canCraft 가 false 면 강화·승급 모두 불가(최대치)다.
		private CraftMode _craftMode = CraftMode.Enhance;
		private bool _canCraft;

		private readonly List<OptionLine> _basicLines = new List<OptionLine>(4);
		private readonly List<GradeOptionLine> _gradeLines = new List<GradeOptionLine>(6);

		protected override void OnInitialize()
		{
			view.OnEquipToggleClicked += onEquipToggleClicked;
			view.OnEnchantClicked += onEnchantClicked;
			view.OnStashClicked += onStashClicked;
			view.OnDecompositionClicked += onDecompositionClicked;
			view.OnLockClicked += onLockClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnEquipToggleClicked -= onEquipToggleClicked;
			view.OnEnchantClicked -= onEnchantClicked;
			view.OnStashClicked -= onStashClicked;
			view.OnDecompositionClicked -= onDecompositionClicked;
			view.OnLockClicked -= onLockClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 인벤토리 경로 — 내 장비를 UID 로 찾아 연다. 장착·강화가 살아 있다.
		// stashMode = true 면 보관함↔인벤토리 이동 버튼을 함께 보여준다.
		public UniTask ShowAsync(long uid, bool stashMode, CancellationToken ct)
		{
			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(uid);
			return showAsync(instance, uid, false, stashMode, ct);
		}

		// 디스플레이 경로 — 결과창·상점처럼 내 것이 아닌 목록에서 연다.
		//
		// 인벤토리를 조회하지 않고 인스턴스를 그대로 받는다. 보유하지 않은 장비(보상 미리보기·
		// 상점 진열)도 등급·품질을 정확히 보여줘야 하는데, UID 는 인벤토리에 들어가야 생긴다.
		public UniTask ShowAsync(EquipmentInstance instance, CancellationToken ct)
		{
			return showAsync(instance, 0, true, false, ct);
		}

		// 팝업 표시 — 데이터 계산 후 View 에 그리기 지시, 닫힘까지 대기.
		private async UniTask showAsync(EquipmentInstance instance, long uid, bool readOnly, bool stashMode, CancellationToken ct)
		{
			// 프리펩 기본 상태와 무관하게, 조건이 맞을 때만 아래에서 켠다.
			view.SetStashVisible(false);
			view.SetDecompositionVisible(false);
			view.SetLockVisible(false);

			Table_Item.Row row = (instance != null) ? instance.Item : null;
			if (instance == null || row == null)
			{
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				return;
			}

			_uid = uid;
			view.SetReadOnly(readOnly);

			Table_Equipment.Row equip = instance.Equipment;
			_slot = (equip != null) ? equip.EquipSlotType : EquipSlotTypes.None;

			// 등급·레벨·품질은 아이템 테이블이 아니라 인스턴스가 소유한다.
			view.SetInfo(row, instance.grade, instance.level, EquipmentUpgrade.GetMaxLevel(instance), instance.quality);
			view.SetEquipInteractable(_slot != EquipSlotTypes.None);
			view.SetEquipLabel(equipLabel());

			if (stashMode == true)
			{
				refreshStashButton(instance);
			}

			// 읽기 전용이면 버튼 묶음이 통째로 숨겨지므로 판단할 필요가 없다.
			if (readOnly == false)
			{
				refreshEnchantButton(instance);

				// 잠금·분해 버튼은 버튼 묶음 밖에 있어 내 장비일 때만 따로 켠다.
				// 착용 중이거나 잠근 장비는 분해할 수 없어 분해 버튼을 감춘다.
				view.SetLockVisible(true);
				view.SetLocked(instance.locked);
				view.SetDecompositionVisible(EquipmentDecompose.CanDecompose(instance));
			}

			buildBasicOptions(instance, equip);
			view.RenderBasicOptions(_basicLines);

			buildGradeOptions(instance, equip);
			view.RenderGradeOptions(_gradeLines);

			// 아이콘 로드가 끝난 뒤 한 번에 표시
			await view.BindItemSlotAsync(instance, isEquipped(), ct);
			view.Reveal();

			await view.WaitForCloseAsync(ct);
		}

		// ── 입력 ──────────────────────────────────────────────────────────

		// 장착/해제 토글 — 현재 장착 중이면 해제, 아니면 장착.
		private void onEquipToggleClicked()
		{
			Loadout loadout = Account.Instance.Loadout;

			if (isEquipped() == true)
			{
				loadout.Unequip(_slot);
			}
			else
			{
				loadout.TryEquip(_uid);
			}

			// 장착·해제는 목록으로 돌아가 결과를 확인하는 흐름이라 팝업을 닫는다.
			// 닫히는 마당에 라벨·슬롯 표시를 갱신할 이유가 없다.
			view.CloseFromInput();
		}

		// 보관함↔인벤토리 이동 — 지금 있는 곳의 반대편으로 보낸다. 목록에서 결과를 확인하는 흐름이라 팝업을 닫는다.
		private void onStashClicked()
		{
			Inventory inventory = Account.Instance.Inventory;
			EquipmentInstance instance = inventory.GetEquipment(_uid);
			if (instance == null)
			{
				return;
			}

			inventory.SetStash(_uid, instance.inStash == false);
			view.CloseFromInput();
		}

		// 잠금 토글 — 팝업을 닫지 않고 버튼 색·슬롯 표시·분해 버튼만 갱신한다.
		// 서버 저장은 장비 화면을 닫을 때 한 번에 된다(NetworkManager.FlushLockIfDirty).
		private void onLockClicked()
		{
			Inventory inventory = Account.Instance.Inventory;
			EquipmentInstance instance = inventory.GetEquipment(_uid);
			if (instance == null)
			{
				return;
			}

			inventory.SetLock(_uid, instance.locked == false);
			view.SetLocked(instance.locked);
			view.SetDecompositionVisible(EquipmentDecompose.CanDecompose(instance));
		}

		private void onDecompositionClicked()
		{
			decomposeAsync(view.GetDestroyToken()).Forget();
		}

		// 장비가 사라져 되돌릴 수 없으므로 한 번 더 확인받는다.
		private async UniTaskVoid decomposeAsync(CancellationToken ct)
		{
			CommonPopupData data;
			data.title = "분해";
			data.desc = "아이템분해시 소모한 등급과 강화에 따라 일부 재화를 얻을 수 있습니다.";
			data.button1Text = "취소";
			data.button2Text = "분해";

			// 취소·닫기·Dim 은 확인 팝업만 닫는다.
			(bool cancelled, CommonPopupResult result) = await UIManager.Instance.ShowCommonPopupAsync(data, ct).SuppressCancellationThrow();
			if (cancelled == true || result != CommonPopupResult.Button2)
			{
				return;
			}

			// 확인 팝업이 떠 있는 사이 상태가 바뀌었을 수 있어 다시 본다.
			if (EquipmentDecompose.CanDecompose(Account.Instance.Inventory.GetEquipment(_uid)) == false)
			{
				return;
			}

			// 결과(획득 재화)는 시스템 로그가 보여준다 — 응답을 기다리지 않고 이 팝업도 닫는다.
			EquipmentDecompose.Request(_uid);
			view.CloseFromInput();
		}

		// 장착 여부와 무관하게 옮길 수 있다 — 장착한 채로 보관함에 둘 수 있다.
		// 보관함이 가득 차면 넣는 쪽만 막는다 — 꺼내는 쪽은 인벤토리가 넘쳐도 허용한다.
		private void refreshStashButton(EquipmentInstance instance)
		{
			Inventory inventory = Account.Instance.Inventory;

			view.SetStashVisible(true);
			view.SetStashLabel(instance.inStash == true ? "인벤토리로 이동" : "보관함으로 이동");
			view.SetStashInteractable(instance.inStash == true || inventory.StashCount < inventory.StashCapacity);
		}

		// 강화·승급 — 팝업을 닫고 제작 창을 해당 모드로 열어 이 장비를 등록해 둔다.
		private void onEnchantClicked()
		{
			if (_canCraft == false)
			{
				return;
			}

			view.CloseFromInput();
			openCraftAsync(_craftMode, _uid).Forget();
		}

		// 팝업은 닫히며 파괴되므로 view 토큰을 쓰지 않는다 — 창 열기는 팝업 수명과 무관하게 끝까지 가야 한다.
		// 네비게이션 바의 제작 탭으로 이동하는 것과 같다 — 장비 창은 교체되어 닫히고 탭 선택도 제작으로 옮겨간다.
		private static async UniTaskVoid openCraftAsync(CraftMode mode, long uid)
		{
			UIScreen screen = await UIManager.Instance.OpenTabAsync(UIScreenId.Craft, CancellationToken.None);
			CraftUI craft = screen as CraftUI;
			if (craft == null)
			{
				return;
			}

			// OpenWindowAsync 는 OnOpenAsync(초기화)를 끝낸 뒤 돌아오므로 등록이 초기화에 덮이지 않는다.
			craft.Preselect(mode, uid);
		}

		// 강화가 가능하면 "강화", 승급이 가능하면 "승급", 둘 다 아니면 "최대치"(비활성).
		// 재화는 보지 않는다 — 부족분은 제작 창이 보여준다.
		// 두 조건은 배타적이다: 승급은 최대 레벨을 요구하고 강화는 최대 레벨 미만을 요구한다.
		private void refreshEnchantButton(EquipmentInstance instance)
		{
			if (EquipmentUpgrade.CanEnhance(instance) == true)
			{
				_craftMode = CraftMode.Enhance;
				_canCraft = true;
				view.SetEnchantLabel("강화");
			}
			else if (EquipmentUpgrade.CanPromote(instance) == true)
			{
				_craftMode = CraftMode.Promote;
				_canCraft = true;
				view.SetEnchantLabel("승급");
			}
			else
			{
				_canCraft = false;
				view.SetEnchantLabel("최대치");
			}

			view.SetEnchantInteractable(_canCraft);
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		private bool isEquipped()
		{
			// 디스플레이 경로(결과창·상점)는 인벤토리에 없는 장비라 _uid 가 0 이다.
			// 빈 슬롯의 GetSlot 도 0 을 돌려주므로, 가드가 없으면 그 부위를 비워 둔 것만으로
			// 보상 장비가 "장착중" 으로 보인다.
			if (_uid <= 0 || _slot == EquipSlotTypes.None)
			{
				return false;
			}

			return Account.Instance.Loadout.GetSlot(_slot) == _uid;
		}

		private string equipLabel()
		{
			return isEquipped() ? "장착해제" : "장착";
		}

		// ── 기본 옵션 ─────────────────────────────────────────────────────

		// 현재 등급 행의 Opt1~4 를 칸 순서 그대로 만든다.
		// EquipmentOptionCalculator.Collect 를 쓰지 않는 이유 — 그쪽은 해금 옵션까지 한 배열에 섞어 주므로
		// "칸 번호 = Opt 번호" 대응이 깨진다.
		private void buildBasicOptions(EquipmentInstance instance, Table_Equipment.Row equip)
		{
			_basicLines.Clear();
			if (equip == null)
			{
				return;
			}

			Table_EquipOption.Row row = EquipmentCatalog.GetOption(equip.EquipOptionGroupID, instance.grade);
			if (row == null)
			{
				return;
			}

			addBasic(row.Opt1_ID, row.Opt1_Val, row.Opt1_Step, instance.level);
			addBasic(row.Opt2_ID, row.Opt2_Val, row.Opt2_Step, instance.level);
			addBasic(row.Opt3_ID, row.Opt3_Val, row.Opt3_Step, instance.level);
			addBasic(row.Opt4_ID, row.Opt4_Val, row.Opt4_Step, instance.level);
		}

		private void addBasic(Option option, float val, float step, int level)
		{
			if (option == Option.None)
			{
				return;
			}

			StatDetail detail;
			if (StatOptionText.TryGetStatDetail(option, out detail) == false)
			{
				return;
			}

			// Val 은 0레벨 기준값이다 (아이템 설계 5.1 — 캐릭터 스탯과 규칙이 반대).
			float value = val + step * level;

			OptionLine line;
			line.iconAddress = StatOptionText.GetStatIcon(detail);
			line.text = StatOptionText.FormatStat(detail, value, false);
			_basicLines.Add(line);
		}

		// ── 등급 해금 옵션 ────────────────────────────────────────────────

		// Normal~Mythic 6칸 고정. 아이템 등급보다 높은 등급은 미해금으로 표시한다.
		private void buildGradeOptions(EquipmentInstance instance, Table_Equipment.Row equip)
		{
			_gradeLines.Clear();
			if (equip == null)
			{
				return;
			}

			for (int g = (int)ItemGradeType.Normal; g <= (int)ItemGradeType.Mythic; g++)
			{
				ItemGradeType grade = (ItemGradeType)g;

				GradeOptionLine line;
				line.grade = grade;
				line.unlocked = (int)instance.grade >= g;
				line.hasOption = false;
				line.text = string.Empty;
				line.rangeText = string.Empty;
				line.hasRange = false;

				Table_EquipOption.Row row = EquipmentCatalog.GetOption(equip.EquipOptionGroupID, grade);
				StatDetail detail;
				if (row != null && row.UnlockOpt_ID != Option.None && StatOptionText.TryGetStatDetail(row.UnlockOpt_ID, out detail) == true)
				{
					// 품질이 구간 어디에 있느냐로 최종값이 정해진다 (아이템 설계 5장).
					float value = row.UnlockOpt_MinVal + (row.UnlockOpt_MaxVal - row.UnlockOpt_MinVal) * ProjectOne.Shared.EquipmentQuality.ToRate(instance.quality);

					line.hasOption = true;
					line.text = StatOptionText.FormatStat(detail, value, line.unlocked == false);
					line.rangeText = StatOptionText.FormatRange(detail, row.UnlockOpt_MinVal, row.UnlockOpt_MaxVal);
					line.hasRange = true;	// 범위는 능력치 옵션일 때만 — 여기 도달했다는 것이 곧 스탯이라는 뜻이다
				}

				_gradeLines.Add(line);
			}
		}
	}
}
