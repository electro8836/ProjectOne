using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 레벨업 스탯 1줄 렌더 데이터.
	public struct StatRowData
	{
		public string iconAddress;
		public string name;
		public string value;
	}

	// 특성 슬롯 1칸 렌더 데이터 (슬롯이 잠김 여부를 직접 계산, 해금 시 현재 레벨 스킬 표시).
	public struct TraitSlotData
	{
		public int traitGroupId;
		public Table_CharacterTrait.Row trait;
		public int charLevel;
		public int skillId;
	}

	// 레벨업 비용 1칸 렌더 데이터 (text 는 보유/필요 색상 포함 완성형).
	public struct LevelupCostData
	{
		public string iconAddress;
		public string text;
	}

	// 캐릭터 디테일 팝업 Presenter — 표시 계산 + 레벨업 결정(서버 권위/오프라인 로컬)을 담당한다.
	public sealed class CharacterDetailPresenter : Presenter<CharacterDetailPopup>
	{
		private const string ColorEnough = "#5af887";	// 보유 충분(녹)
		private const string ColorLack = "#f85a5a";		// 보유 부족(빨)

		private int _characterId;
		private CancellationToken _ct;

		protected override void OnInitialize()
		{
			view.OnLevelupClicked += onLevelupClicked;
			view.OnReturnClicked += onReturnClicked;
			view.OnSelectClicked += onSelectClicked;
			view.OnTraitSlotClicked += onTraitSlotClicked;

			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);
			EventManager.Instance.Subscribe<InventoryChangeEvent>(onInventoryChanged);
		}

		protected override void OnDispose()
		{
			view.OnLevelupClicked -= onLevelupClicked;
			view.OnReturnClicked -= onReturnClicked;
			view.OnSelectClicked -= onSelectClicked;
			view.OnTraitSlotClicked -= onTraitSlotClicked;

			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
			EventManager.Instance.Unsubscribe<InventoryChangeEvent>(onInventoryChanged);
		}

		// 팝업 표시 — 렌더 후 닫힘까지 대기.
		public async UniTask ShowAsync(int characterId, CancellationToken ct)
		{
			_characterId = characterId;
			_ct = ct;

			await renderAsync(ct);
			await view.WaitForCloseAsync(ct);
		}

		// ── 렌더 ──────────────────────────────────────────────────────

		private async UniTask renderAsync(CancellationToken ct)
		{
			Table_Character.Row row = Table_Character.Get(_characterId);
			if (row == null)
			{
				return;
			}

			OwnedCharacter oc = Account.Instance.Loadout.GetOwned(_characterId);
			int level = oc != null ? oc.level : 1;
			int exp = oc != null ? oc.exp : 0;

			view.SetHeader(row);

			int reqExp = requiredExpForNext(level);
			view.SetLevelExp(level, exp, reqExp);

			await view.BindStatsAsync(buildStats(row, level), ct);
			view.BindTraits(buildTraits(row, level));
			await view.BindCostAsync(buildCost(level), ct);
			view.SetLevelupInteractable(canLevelup(level, exp));

			// 보유 + 미선택일 때만 선택 버튼 표시 (미보유면 숨김)
			view.SetSelectVisible(oc != null && Account.Instance.Loadout.Selected != _characterId);

			await view.SetCharacterIconAsync(row.Icon, ct);
		}

		// 레벨업 스탯 — Table_LevelupStat 의 StatID/Value 4쌍을 순회.
		private List<StatRowData> buildStats(Table_Character.Row row, int level)
		{
			List<StatRowData> list = new List<StatRowData>();
			Table_LevelupStat.Row lv = Table_LevelupStat.Get(row.LevelupStatID);
			if (lv == null)
			{
				return list;
			}

			addStat(list, lv.StatID_1, lv.StatValue_1, level);
			addStat(list, lv.StatID_2, lv.StatValue_2, level);
			addStat(list, lv.StatID_3, lv.StatValue_3, level);
			addStat(list, lv.StatID_4, lv.StatValue_4, level);
			return list;
		}

		// 값 표기 = 레벨 × 레벨별스탯, 뒤에 녹색으로 레벨업당 증가분(+레벨별스탯). 예) 250<color=#5af887> +5</color>
		private void addStat(List<StatRowData> list, StatInfo stat, float value, int level)
		{
			if (stat == StatInfo.None)
			{
				return;
			}

			Table_StatInfo.Row info = Table_StatInfo.Get(stat);
			string suffix = info != null && info.IsRatio ? "%" : "";

			float total = value * level;

			StatRowData data;
			data.iconAddress = info != null ? info.Icon : null;
			data.name = info != null ? info.Name : stat.ToString();
			data.value = total.ToString("0.##") + suffix
				+ "<color=" + ColorEnough + "> +" + value.ToString("0.##") + suffix + "</color>";
			list.Add(data);
		}

		// 특성 — 캐릭터의 TraitGroup_1~5 중 데이터가 있는 그룹만. slotIndex(1~5)로 장비 옵션 레벨을 조회.
		private List<TraitSlotData> buildTraits(Table_Character.Row row, int level)
		{
			List<TraitSlotData> list = new List<TraitSlotData>();
			addTrait(list, row.TraitGroup_1, 1, level);
			addTrait(list, row.TraitGroup_2, 2, level);
			addTrait(list, row.TraitGroup_3, 3, level);
			addTrait(list, row.TraitGroup_4, 4, level);
			addTrait(list, row.TraitGroup_5, 5, level);
			return list;
		}

		private void addTrait(List<TraitSlotData> list, int traitGroupId, int slotIndex, int level)
		{
			if (traitGroupId <= 0)
			{
				return;
			}

			Table_CharacterTrait.Row trait = Table_CharacterTrait.Get(traitGroupId);
			if (trait == null)
			{
				return;
			}

			int slotLevel = Account.Instance.Loadout.GetTraitLevel(_characterId, slotIndex);

			TraitSlotData data;
			data.traitGroupId = traitGroupId;
			data.trait = trait;
			data.charLevel = level;
			data.skillId = (int)TraitSkillResolver.SkillForLevel(trait, slotLevel);
			list.Add(data);
		}

		// 레벨업 비용 — 다음 레벨의 재료 2종 + 재화 2종.
		private List<LevelupCostData> buildCost(int level)
		{
			List<LevelupCostData> list = new List<LevelupCostData>();
			Table_LevelupCost.Row cost = Table_LevelupCost.Get(level + 1);
			if (cost == null)
			{
				return list;
			}

			addMaterialCost(list, cost.MaterialID_01, cost.MaterialValue_01);
			addMaterialCost(list, cost.MaterialID_02, cost.MaterialValue_02);
			addCurrencyCost(list, cost.CurrencyType_01, cost.CurrencyValue_01);
			addCurrencyCost(list, cost.CurrencyType_02, cost.CurrencyValue_02);
			return list;
		}

		private void addMaterialCost(List<LevelupCostData> list, int materialId, int required)
		{
			if (materialId <= 0 || required <= 0)
			{
				return;
			}

			int owned = Account.Instance.Inventory.GetCount(materialId);

			LevelupCostData data;
			data.iconAddress = materialIconAddress(materialId);
			data.text = costText(owned, required);
			list.Add(data);
		}

		private void addCurrencyCost(List<LevelupCostData> list, CurrencyInfo type, int required)
		{
			if (type == CurrencyInfo.None || required <= 0)
			{
				return;
			}

			int owned = Account.Instance.Wallet.GetAmount(type);
			Table_CurrencyInfo.Row info = Table_CurrencyInfo.Get(type);

			LevelupCostData data;
			data.iconAddress = info != null ? info.Icon : null;
			data.text = costText(owned, required);
			list.Add(data);
		}

		// 보유/필요 텍스트 — 보유가 필요 이상이면 녹색, 부족하면 빨강. 예) <color=#5af887>302K</color>/450K
		private static string costText(int owned, int required)
		{
			string color = owned >= required ? ColorEnough : ColorLack;
			return "<color=" + color + ">" + formatAmount(owned) + "</color>/" + formatAmount(required);
		}

		// ── 레벨업 ────────────────────────────────────────────────────

		private void onLevelupClicked()
		{
			OwnedCharacter oc = Account.Instance.Loadout.GetOwned(_characterId);
			if (oc == null)
			{
				return;
			}

			if (canLevelup(oc.level, oc.exp) == false)
			{
				return;
			}

			if (NetworkManager.Instance.IsLoggedIn == true)
			{
				LevelupRequest request = new LevelupRequest();
				request.characterId = _characterId;
				NetworkManager.Instance.RequestLevelup(request, onLevelupResponse);
			}
			else
			{
				// 오프라인/Dev — 로컬에서 비용 차감 + 레벨 +1
				applyLevelup(oc.level + 1, oc.exp);
			}
		}

		// 서버 응답 — 성공 시 권위 레벨/경험치로 갱신(비용 차감은 mirror).
		private void onLevelupResponse(bool success, LevelupResponse data, string error)
		{
			if (success == false || data == null)
			{
				Debug.LogWarning("[CharacterDetail] 레벨업 실패: " + error);
				return;
			}

			applyLevelup(data.newLevel, data.newExp);
		}

		// 다음 레벨의 비용을 차감하고 레벨/경험치를 갱신한다(CharacterChangeEvent → 재렌더).
		private void applyLevelup(int newLevel, int newExp)
		{
			OwnedCharacter oc = Account.Instance.Loadout.GetOwned(_characterId);
			if (oc == null)
			{
				return;
			}

			Table_LevelupCost.Row cost = Table_LevelupCost.Get(oc.level + 1);
			if (cost != null)
			{
				spendCost(cost);
			}

			Account.Instance.Loadout.ApplyLevelup(_characterId, newLevel, newExp);
		}

		private void spendCost(Table_LevelupCost.Row cost)
		{
			if (cost.MaterialID_01 > 0 && cost.MaterialValue_01 > 0)
			{
				Account.Instance.Inventory.TrySpend(cost.MaterialID_01, cost.MaterialValue_01);
			}

			if (cost.MaterialID_02 > 0 && cost.MaterialValue_02 > 0)
			{
				Account.Instance.Inventory.TrySpend(cost.MaterialID_02, cost.MaterialValue_02);
			}

			if (cost.CurrencyType_01 != CurrencyInfo.None && cost.CurrencyValue_01 > 0)
			{
				CurrencyManager.Instance.TrySpend(cost.CurrencyType_01, cost.CurrencyValue_01);
			}

			if (cost.CurrencyType_02 != CurrencyInfo.None && cost.CurrencyValue_02 > 0)
			{
				CurrencyManager.Instance.TrySpend(cost.CurrencyType_02, cost.CurrencyValue_02);
			}
		}

		// 레벨업 가능 — 다음 레벨 존재 + 경험치 충족 + 비용 보유.
		private bool canLevelup(int level, int exp)
		{
			int reqExp = requiredExpForNext(level);
			if (reqExp <= 0)
			{
				return false;	// 최대 레벨(다음 경험치 데이터 없음)
			}

			if (exp < reqExp)
			{
				return false;
			}

			return canAffordCost(level);
		}

		private bool canAffordCost(int level)
		{
			Table_LevelupCost.Row cost = Table_LevelupCost.Get(level + 1);
			if (cost == null)
			{
				return false;	// 비용 데이터 없음 — 레벨업 경로 미정의
			}

			if (cost.MaterialID_01 > 0 && Account.Instance.Inventory.GetCount(cost.MaterialID_01) < cost.MaterialValue_01)
			{
				return false;
			}

			if (cost.MaterialID_02 > 0 && Account.Instance.Inventory.GetCount(cost.MaterialID_02) < cost.MaterialValue_02)
			{
				return false;
			}

			if (cost.CurrencyType_01 != CurrencyInfo.None && Account.Instance.Wallet.GetAmount(cost.CurrencyType_01) < cost.CurrencyValue_01)
			{
				return false;
			}

			if (cost.CurrencyType_02 != CurrencyInfo.None && Account.Instance.Wallet.GetAmount(cost.CurrencyType_02) < cost.CurrencyValue_02)
			{
				return false;
			}

			return true;
		}

		// 다음 레벨 도달에 필요한 누적 경험치(Table_LevelExp). 없으면 0(최대 레벨).
		private static int requiredExpForNext(int level)
		{
			Table_LevelExp.Row next = Table_LevelExp.Get(level + 1);
			return next != null ? next.TotalExperience : 0;
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onReturnClicked()
		{
			view.CloseFromInput();
		}

		// 이 캐릭터를 대표 캐릭터로 선택 — 성공 시 CharacterChangeEvent 로 재렌더되어 버튼이 숨겨진다.
		private void onSelectClicked()
		{
			Account.Instance.Loadout.TrySelect(_characterId);
		}

		// 특성 선택 UI 는 이번 범위 제외 — 추후 특성 찍는 화면을 켜고 해당 그룹으로 스크롤할 자리.
		private void onTraitSlotClicked(int traitGroupId)
		{
			Debug.Log("[CharacterDetail] 특성 슬롯 클릭 (group=" + traitGroupId + ") — 선택 UI 미구현");
		}

		// ── 이벤트 → 재렌더 ───────────────────────────────────────────

		private void onCharacterChanged(CharacterChangeEvent e)
		{
			renderAsync(_ct).Forget();
		}

		private void onResourceChanged(ResourceChangeEvent e)
		{
			renderAsync(_ct).Forget();
		}

		private void onInventoryChanged(InventoryChangeEvent e)
		{
			renderAsync(_ct).Forget();
		}

		// ── 유틸 ──────────────────────────────────────────────────────

		// 큰 수 축약(302K, 4M 등). CurrencySlot.formatAmount 와 동일 규칙.
		private static string formatAmount(int amount)
		{
			if (amount >= 1_000_000_000) { return (amount / 1_000_000_000).ToString() + "B"; }

			if (amount >= 1_000_000) { return (amount / 1_000_000).ToString() + "M"; }

			if (amount >= 1_000) { return (amount / 1_000).ToString() + "K"; }

			return amount.ToString();
		}

		// 재료 아이콘 주소 — Table_Material.Icon(Addressable 주소).
		private static string materialIconAddress(int materialId)
		{
			Table_Material.Row row = Table_Material.Get(materialId);
			return row != null ? row.Icon : null;
		}
	}
}
