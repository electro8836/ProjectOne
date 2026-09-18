using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Pets;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 펫 강화 팝업 Presenter — 장착/강화/승급의 판정과 실행을 담당한다.
	//
	// 판정은 전부 PetBook 의 Block enum 하나만 본다. 버튼을 잠그는 규칙과 실제로 막는 규칙이
	// 같은 함수에서 나와야 둘이 조용히 갈라지지 않는다.
	public sealed class PetEnhancePopupPresenter : Presenter<PetEnhancePopup>
	{
		private EDT.Pet _petId = EDT.Pet.None;

		private CancellationTokenSource _renderCts;

		protected override void OnInitialize()
		{
			view.OnEquipClicked += onEquipClicked;
			view.OnLevelUpClicked += onLevelUpClicked;
			view.OnGradeUpClicked += onGradeUpClicked;

			EventManager.Instance.Subscribe<PetChangeEvent>(onPetChanged);

			// 재화가 줄면 다음 강화의 도달 여부가 바뀐다 — 펫이 안 바뀌어도 버튼을 다시 판정해야 한다.
			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnEquipClicked -= onEquipClicked;
			view.OnLevelUpClicked -= onLevelUpClicked;
			view.OnGradeUpClicked -= onGradeUpClicked;

			EventManager.Instance.Unsubscribe<PetChangeEvent>(onPetChanged);
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		// View 가 인스턴스화 직후 부른다. 표시를 채우고 돌아온다(닫힘 대기는 View 가 한다).
		public UniTask ShowAsync(EDT.Pet petId, CancellationToken ct)
		{
			_petId = petId;

			// 보유하지 않은 펫은 열 것이 없다 — 슬롯이 버튼을 잠가 두지만 방어한다.
			if (Account.Instance.Pet.IsOwned(petId) == false)
			{
				view.Close();
				return UniTask.CompletedTask;
			}

			render();

			Table_Pet.Row row = PetCatalog.Get(_petId);
			return view.SetPetImageAsync((row != null) ? row.Image : string.Empty, ct);
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		// 장착/해제 후에도 팝업은 닫지 않는다 — 이어서 강화하는 흐름이 자연스럽다.
		private void onEquipClicked()
		{
			PetBook book = Account.Instance.Pet;

			if (book.IsEquipped(_petId) == true)
			{
				book.Unequip();
				return;
			}

			book.TryEquip(_petId);
		}

		private void onLevelUpClicked()
		{
			Account.Instance.Pet.TryEnhance(_petId);
		}

		private void onGradeUpClicked()
		{
			Account.Instance.Pet.TryPromote(_petId);
		}

		private void onPetChanged(PetChangeEvent e)
		{
			render();
		}

		private void onResourceChanged(ResourceChangeEvent e)
		{
			render();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		private void render()
		{
			PetBook book = Account.Instance.Pet;

			PetEntry entry = book.Find(_petId);
			Table_Pet.Row row = PetCatalog.Get(_petId);
			if (entry == null || row == null)
			{
				return;
			}

			view.SetInfo(buildInfo(book, entry, row));
			view.SetLevelUpCost(buildEnhanceCost(book, entry));
			view.SetGradeUpCost(buildPromoteCost(book, entry));
		}

		private PetPopupData buildInfo(PetBook book, PetEntry entry, Table_Pet.Row row)
		{
			string gradeName = ItemGradeNames.Get(entry.Grade);

			PetPopupData data;
			data.name = row.Name;
			// 레벨 줄에는 등급을 넣지 않는다 — 바로 옆 Grade 줄이 이미 보여준다.
			data.levelText = $"레벨 : {entry.Level}/{entry.MaxLevel}";
			data.gradeText = $"등급 : {colorize(gradeName, entry.Grade)}";
			data.bonusText = buildBonusText(row, entry.Level);
			data.equipText = $"장착 효과 : {row.EquipText}";
			data.equipped = book.IsEquipped(_petId);

			// 팝업은 보유한 펫만 열리므로 미보유 분기가 없다 — 슬롯과 같은 등급색을 그대로 칠한다.
			ItemGradeColorTable table = view.GradeColors;
			data.bgColor = (table != null) ? table.Get(entry.Grade).bg : Color.white;
			data.gradientColor = (table != null) ? table.Get(entry.Grade).border : Color.white;

			return data;
		}

		// 접두사는 기본색으로 두고 등급명만 색을 입힌다 — 한 줄에 두 색이라 TMP_Text.color 로는 안 된다.
		// 색은 ItemGradeColorTable 이 소유한다 (ItemInfoPopup 이 등급명에 쓰는 것과 같은 값).
		private string colorize(string text, ItemGradeType grade)
		{
			ItemGradeColorTable table = view.GradeColors;
			if (table == null)
			{
				return text;
			}

			return "<color=#" + ColorUtility.ToHtmlStringRGB(table.Get(grade).text) + ">" + text + "</color>";
		}

		private static string buildBonusText(Table_Pet.Row row, int level)
		{
			StatDetail detail;
			if (StatOptionText.TryGetStatDetail(row.Opt_ID, out detail) == false)
			{
				return string.Empty;
			}

			return "보유 효과 : " + StatOptionText.FormatStat(detail, PetEntry.GetOptionValue(row, level), false);
		}

		// 비용 표시는 Block 하나로 갈린다 — 버튼을 잠그는 조건이 곧 실행을 막는 조건이다.
		private PetCostData buildEnhanceCost(PetBook book, PetEntry entry)
		{
			PetEnhanceBlock block = book.GetEnhanceBlock(_petId);

			PetCostData cost;
			cost.available = block == PetEnhanceBlock.None;
			cost.notEnough = block == PetEnhanceBlock.NotEnough;
			cost.limitText = "최대레벨";
			cost.iconAddress = string.Empty;
			cost.amountText = string.Empty;

			EDT.Currency currency;
			int amount;
			cost.hasCost = block != PetEnhanceBlock.MaxLevel
				&& PetCatalog.TryGetEnhanceCost(entry.Level, out currency, out amount);

			if (cost.hasCost == true)
			{
				PetCatalog.TryGetEnhanceCost(entry.Level, out currency, out amount);
				cost.iconAddress = getCurrencyIcon(currency);
				cost.amountText = amount.ToString("N0");
			}

			return cost;
		}

		private PetCostData buildPromoteCost(PetBook book, PetEntry entry)
		{
			PetPromoteBlock block = book.GetPromoteBlock(_petId);

			PetCostData cost;
			cost.available = block == PetPromoteBlock.None;
			cost.notEnough = block == PetPromoteBlock.NotEnough;
			cost.limitText = "최대등급";
			cost.iconAddress = string.Empty;
			cost.amountText = string.Empty;

			Table_PetPromotion.Row row = PetCatalog.GetPromotion(entry.Grade);
			cost.hasCost = block != PetPromoteBlock.MaxGrade && row != null;

			if (cost.hasCost == true)
			{
				cost.iconAddress = getCurrencyIcon(row.CostCurrencyID);
				cost.amountText = row.CostCurrencyValue.ToString("N0");
			}

			return cost;
		}

		private static string getCurrencyIcon(EDT.Currency currency)
		{
			Table_Currency.Row row = Table_Currency.Get(currency);
			return (row != null) ? row.Icon : string.Empty;
		}
	}
}
