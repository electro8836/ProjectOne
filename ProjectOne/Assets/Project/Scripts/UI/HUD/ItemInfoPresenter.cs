using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using ProjectOne.Skill;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 옵션 1줄 렌더 데이터 — 아이콘 주소 + 제목 + 설명.
	public struct ItemOptionEntry
	{
		public string iconAddress;
		public string title;
		public string desc;
	}

	// 아이템 정보 팝업 Presenter — 표시 데이터 계산과 장착/해제 결정(Loadout 조작)을 담당한다.
	// 대상은 아이템 ID 가 아니라 **장비 인스턴스 UID** 다. 같은 아이템이라도 등급·강화·순도·품질이 다르다.
	// 장착 변경은 Loadout 이 dirty 로 누적하고, 실제 서버 저장은 장비 화면 닫힘 시 1회 flush 된다.
	public sealed class ItemInfoPresenter : Presenter<ItemInfoPopup>
	{
		private long _uid;
		private EquipSlotTypes _slot = EquipSlotTypes.None;

		private readonly List<EquipmentOptionCalculator.Resolved> _options = new List<EquipmentOptionCalculator.Resolved>(8);
		private readonly List<ItemOptionEntry> _entries = new List<ItemOptionEntry>(8);

		protected override void OnInitialize()
		{
			view.OnEquipToggleClicked += onEquipToggleClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnEquipToggleClicked -= onEquipToggleClicked;
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

			// 등급은 아이템 테이블이 아니라 인스턴스가 소유한다.
			view.SetInfo(row, instance.grade);
			view.SetEquipInteractable(_slot != EquipSlotTypes.None);
			view.SetEquipLabel(equipLabel());

			// 아이콘 로드가 끝난 뒤 한 번에 표시
			await view.BuildOptionsAsync(buildOptions(instance), ct);
			await view.BindItemSlotAsync(row, instance, ct);
			view.Reveal();

			await view.WaitForCloseAsync(ct);
		}

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

		// 기본 옵션은 최종값만, 해금 옵션은 최종값 + (Min ~ Max) 구간을 함께 보여준다 (아이템 설계 8장).
		private List<ItemOptionEntry> buildOptions(EquipmentInstance instance)
		{
			EquipmentOptionCalculator.Collect(instance, _options);

			_entries.Clear();
			for (int i = 0; i < _options.Count; i++)
			{
				EquipmentOptionCalculator.Resolved resolved = _options[i];

				OptionCatalog.Entry option;
				if (OptionCatalog.TryGet(resolved.option, out option) == false)
				{
					continue;
				}

				// Stat 외 타입(SkillGrant/SkillLevel/Modifier)은 문구 규칙이 아직 없다 — STEP 7 에서 붙인다.
				if (option.type != OptionTypes.Stat)
				{
					continue;
				}

				ItemOptionEntry entry;
				entry.iconAddress = string.Empty;
				entry.title = SkillTextNames.FormatStatDetail(option.statDetail, resolved.value);
				entry.desc = resolved.isUnlock ? formatRange(option.statDetail, resolved) : string.Empty;
				_entries.Add(entry);
			}

			return _entries;
		}

		private static string formatRange(StatDetail detail, EquipmentOptionCalculator.Resolved resolved)
		{
			string min = SkillTextNames.FormatStatDetail(detail, resolved.minValue);
			string max = SkillTextNames.FormatStatDetail(detail, resolved.maxValue);
			return "(" + min + " ~ " + max + ")";
		}
	}
}
