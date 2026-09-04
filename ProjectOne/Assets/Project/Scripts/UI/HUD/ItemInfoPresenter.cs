using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
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

		private readonly List<OptionLine> _basicLines = new List<OptionLine>(4);
		private readonly List<GradeOptionLine> _gradeLines = new List<GradeOptionLine>(6);

		protected override void OnInitialize()
		{
			view.OnEquipToggleClicked += onEquipToggleClicked;
			view.OnEnchantClicked += onEnchantClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnEquipToggleClicked -= onEquipToggleClicked;
			view.OnEnchantClicked -= onEnchantClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 팝업 표시 — 데이터 계산 후 View 에 그리기 지시, 닫힘까지 대기.
		public async UniTask ShowAsync(long uid, CancellationToken ct)
		{
			EquipmentInstance instance = Account.Instance.Inventory.GetEquipment(uid);
			Table_Item.Row row = (instance != null) ? instance.Item : null;
			if (instance == null || row == null)
			{
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				return;
			}

			_uid = uid;

			Table_Equipment.Row equip = instance.Equipment;
			_slot = (equip != null) ? equip.EquipSlotType : EquipSlotTypes.None;

			// 등급·레벨·품질은 아이템 테이블이 아니라 인스턴스가 소유한다.
			view.SetInfo(row, instance.grade, instance.level, EquipmentCatalog.GetMaxLevel(instance.grade), instance.quality);
			view.SetEquipInteractable(_slot != EquipSlotTypes.None);
			view.SetEquipLabel(equipLabel());

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

			view.SetEquipLabel(equipLabel());
			view.SetSlotEquipped(isEquipped());
		}

		private void onEnchantClicked()
		{
			UnityEngine.Debug.Log("[ItemInfoPopup] 강화 버튼 — 미연결 uid=" + _uid);
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		private bool isEquipped()
		{
			if (_slot == EquipSlotTypes.None)
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

			float purityMult = getPurityMultiplier(instance.purity);
			addBasic(row.Opt1_ID, row.Opt1_Val, row.Opt1_Step, instance.level, purityMult);
			addBasic(row.Opt2_ID, row.Opt2_Val, row.Opt2_Step, instance.level, purityMult);
			addBasic(row.Opt3_ID, row.Opt3_Val, row.Opt3_Step, instance.level, purityMult);
			addBasic(row.Opt4_ID, row.Opt4_Val, row.Opt4_Step, instance.level, purityMult);
		}

		private void addBasic(Option option, float val, float step, int level, float purityMult)
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
			float value = (val + step * level) * purityMult;

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
					float value = row.UnlockOpt_MinVal + (row.UnlockOpt_MaxVal - row.UnlockOpt_MinVal) * (instance.quality / 100f);

					line.hasOption = true;
					line.text = StatOptionText.FormatStat(detail, value, line.unlocked == false);
					line.rangeText = StatOptionText.FormatRange(detail, row.UnlockOpt_MinVal, row.UnlockOpt_MaxVal);
					line.hasRange = true;	// 범위는 능력치 옵션일 때만 — 여기 도달했다는 것이 곧 스탯이라는 뜻이다
				}

				_gradeLines.Add(line);
			}
		}

		// 순도 배율. 데이터가 없으면 1.0 으로 두어 옵션이 통째로 0이 되는 사고를 막는다.
		private static float getPurityMultiplier(EquipPurity purity)
		{
			Table_EquipPurity.Row row = Table_EquipPurity.Get(purity);
			if (row == null || row.OptionMultiplier <= 0f)
			{
				return 1f;
			}

			return row.OptionMultiplier;
		}
	}
}
