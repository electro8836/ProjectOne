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
		// 수치 증감 색. 미해금 줄은 색 태그를 아예 넣지 않고 View 가 줄 전체를 회색으로 칠한다.
		private const string COLOR_INCREASE = "#5CFF5C";
		private const string COLOR_DECREASE = "#FF5C5C";

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
			if (tryGetStatDetail(option, out detail) == false)
			{
				return;
			}

			// Val 은 0레벨 기준값이다 (아이템 설계 5.1 — 캐릭터 스탯과 규칙이 반대).
			float value = (val + step * level) * purityMult;

			OptionLine line;
			line.iconAddress = getStatIcon(detail);
			line.text = formatStat(detail, value, false);
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
				if (row != null && row.UnlockOpt_ID != Option.None && tryGetStatDetail(row.UnlockOpt_ID, out detail) == true)
				{
					// 품질이 구간 어디에 있느냐로 최종값이 정해진다 (아이템 설계 5장).
					float value = row.UnlockOpt_MinVal + (row.UnlockOpt_MaxVal - row.UnlockOpt_MinVal) * (instance.quality / 100f);

					line.hasOption = true;
					line.text = formatStat(detail, value, line.unlocked == false);
					line.rangeText = formatRange(detail, row.UnlockOpt_MinVal, row.UnlockOpt_MaxVal);
					line.hasRange = true;	// 범위는 능력치 옵션일 때만 — 여기 도달했다는 것이 곧 스탯이라는 뜻이다
				}

				_gradeLines.Add(line);
			}
		}

		// ── 문구 ──────────────────────────────────────────────────────────

		// DisplayFormat 의 {0} 자리에 "부호+수치" 를 끼운다.
		// DisplayFormat 은 부호를 갖지 않는 것을 전제로 한다 (예: "공격력 {0}").
		//
		// 미해금(locked)이면 색 태그를 넣지 않는다 — 줄 전체를 회색으로 만드는 일은 View 가
		// TMP_Text.color 로 처리한다. 문자열에 바깥 태그를 한 겹 더 씌우는 것보다 단순하다.
		private static string formatStat(StatDetail detail, float value, bool locked)
		{
			// 단위(%)를 먼저 수치에 붙인 뒤 색을 씌운다 — 그래야 "+6.6%" 가 통째로 색 안에 들어간다.
			bool absorbUnit = hasPercentSuffix(detail);
			string body = signed(detail, value) + (absorbUnit ? "%" : string.Empty);

			if (locked == false)
			{
				string color = (value < 0f) ? COLOR_DECREASE : COLOR_INCREASE;
				body = "<color=" + color + ">" + body + "</color>";
			}

			return applyFormat(detail, body, absorbUnit);
		}

		// 범위는 수치만 적는다 — 스탯 이름은 같은 줄의 OptionText 가 이미 말하고 있다.
		// 예) (2% - 20%) / (250 - 500)
		private static string formatRange(StatDetail detail, float min, float max)
		{
			return "(" + rangeValue(detail, min) + " - " + rangeValue(detail, max) + ")";
		}

		private static string rangeValue(StatDetail detail, float value)
		{
			return shown(detail, value) + (isPercent(detail) ? "%" : string.Empty);
		}

		// unitAbsorbed 면 "%" 가 이미 body 에 붙어 있으므로 포맷에서 그 한 글자를 지운다(중복 방지).
		private static string applyFormat(StatDetail detail, string body, bool unitAbsorbed)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			if (row == null || string.IsNullOrEmpty(row.DisplayFormat) == true)
			{
				return detail + " " + body;
			}

			string format = row.DisplayFormat;
			if (unitAbsorbed == true)
			{
				format = format.Remove(format.IndexOf("{0}") + 3, 1);
			}

			return string.Format(format, body);
		}

		// DisplayFormat 이 "{0}%" 처럼 자리표시자 바로 뒤에 단위를 붙여 두었는가.
		// 접미사가 "" / "%" 두 가지뿐이라 한 글자만 보면 충분하다.
		private static bool hasPercentSuffix(StatDetail detail)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			if (row == null || string.IsNullOrEmpty(row.DisplayFormat) == true)
			{
				return false;
			}

			int index = row.DisplayFormat.IndexOf("{0}");
			return index >= 0 && index + 3 < row.DisplayFormat.Length && row.DisplayFormat[index + 3] == '%';
		}

		private static bool isPercent(StatDetail detail)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			return row != null && row.StatValueType == StatValueTypes.Percent;
		}

		private static string signed(StatDetail detail, float value)
		{
			return ((value < 0f) ? "-" : "+") + shown(detail, System.Math.Abs(value));
		}

		// Percent 타입은 0.05 처럼 비율로 저장되어 있다 — 표시할 때만 100 을 곱한다("%" 는 포맷이 갖는다).
		private static string shown(StatDetail detail, float value)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			float v = (row != null && row.StatValueType == StatValueTypes.Percent) ? value * 100f : value;
			return v.ToString("0.##");
		}

		// ── 조회 ──────────────────────────────────────────────────────────

		// 스탯 옵션만 문구로 만들 수 있다. 스킬/모디파이어 옵션은 문구 규칙이 아직 없다.
		private static bool tryGetStatDetail(Option option, out StatDetail detail)
		{
			detail = StatDetail.None;

			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(option, out entry) == false || entry.type != OptionTypes.Stat)
			{
				return false;
			}

			detail = entry.statDetail;
			return true;
		}

		// 아이콘은 세부 스탯이 아니라 부모 스탯이 소유한다 (공격력 Add/Ratio/Amp 가 같은 아이콘을 쓴다).
		private static string getStatIcon(StatDetail detail)
		{
			Table_StatDetail.Row detailRow = Table_StatDetail.Get(detail);
			if (detailRow == null)
			{
				return string.Empty;
			}

			Table_Stat.Row statRow = Table_Stat.Get(detailRow.StatID);
			return (statRow != null) ? statRow.Icon : string.Empty;
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
