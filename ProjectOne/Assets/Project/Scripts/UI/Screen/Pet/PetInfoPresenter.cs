using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.Pets;
using ProjectOne.Shared;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 펫 목록 창 Presenter — 무엇을 어떤 순서·색·문구로 깔지 정하고, 고른 펫의 장착/강화/승급을 판정·실행한다.
	//
	// 판정은 전부 PetBook 의 Block enum 하나만 본다. 버튼을 잠그는 규칙과 실제로 막는 규칙이
	// 같은 함수에서 나와야 둘이 조용히 갈라지지 않는다.
	public sealed class PetInfoPresenter : Presenter<PetInfoUI>
	{
		// 정렬 기준. 인덱스를 PlayerPrefs 에 저장하므로 순서를 바꾸면 저장된 값의 의미가 달라진다.
		private enum SortModes
		{
			Owned = 0,	// 보유 먼저 (기본)
			Grade,		// 등급 높은 순
			Level		// 강화도 높은 순
		}

		private const int SORT_MODE_COUNT = 3;
		private const string SORT_MODE_PREF_KEY = "PetSortMode";

		// 미보유 펫의 획득처 — 펫 테이블에 획득처 열이 없어 공통 문구 하나를 쓴다(펫은 상점·필드 보스 보상으로만 들어온다).
		private const string ACQUIRE_TEXT = "상점·필드 보스에서 획득";

		// 미보유 카드의 색. 등급색을 쓰지 않으므로 여기서만 산다.
		private static readonly Color LockedBg = new Color32(0x2A, 0x24, 0x47, 0xFF);

		private SortModes _sortMode = SortModes.Owned;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)
		private CancellationTokenSource _detailCts;	// 무대 그림 로드 취소 — 연달아 고를 때 늦게 끝난 그림이 덮지 않게

		// 무대에 보이는 펫. 0 이면 아직 고르지 않았다 — 렌더 때 장착 펫(없으면 목록 첫 칸)으로 정한다.
		private int _selectedId;

		// 승급 서버 응답 대기 중 — 응답 전 재실행을 막는다(입력은 네트워크 차단막도 막는다).
		private bool _pendingPromote;

		private readonly List<Table_Pet.Row> _rows = new List<Table_Pet.Row>();
		private readonly List<PetSlotData> _slotData = new List<PetSlotData>();

		protected override void OnInitialize()
		{
			view.OnSlotClicked += onSlotClicked;
			view.OnSortClicked += onSortClicked;
			view.OnReturnClicked += onReturnClicked;
			view.OnEquipClicked += onEquipClicked;
			view.OnLevelUpClicked += onLevelUpClicked;
			view.OnLevelUpHeld += onLevelUpClicked;
			view.OnGradeUpClicked += onGradeUpClicked;

			EventManager.Instance.Subscribe<PetChangeEvent>(onPetChanged);

			// 재화가 줄면 다음 강화의 도달 여부가 바뀐다 — 펫이 안 바뀌어도 버튼을 다시 판정해야 한다.
			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);

			loadSortMode();
		}

		protected override void OnDispose()
		{
			// 모아 둔 강화를 창이 닫힐 때 바로 보낸다.
			PetEnhanceBatcher.Instance.Flush();

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			if (_detailCts != null)
			{
				_detailCts.Cancel();
				_detailCts.Dispose();
				_detailCts = null;
			}

			view.OnSlotClicked -= onSlotClicked;
			view.OnSortClicked -= onSortClicked;
			view.OnReturnClicked -= onReturnClicked;
			view.OnEquipClicked -= onEquipClicked;
			view.OnLevelUpClicked -= onLevelUpClicked;
			view.OnLevelUpHeld -= onLevelUpClicked;
			view.OnGradeUpClicked -= onGradeUpClicked;

			EventManager.Instance.Unsubscribe<PetChangeEvent>(onPetChanged);
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			view.SetSortLabel(getSortLabel(_sortMode));
			render();

			return UniTask.CompletedTask;
		}

		// 화면 닫힘 — 펫 장착 변경(dirty)이 있으면 서버에 1회 저장한다(코스튬 착용과 함께).
		public override UniTask OnCloseAsync()
		{
			NetworkManager.Instance.FlushAppearanceIfDirty();
			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		// 칸을 누르면 그 펫을 무대에 올린다 — 미보유 펫도 무엇을 얻을지 보이도록 고를 수 있다.
		private void onSlotClicked(int petId)
		{
			if (petId == _selectedId)
			{
				return;
			}

			_selectedId = petId;
			view.SetSelectedPet(petId);
			renderDetail();
		}

		// 장착/해제 — 저장은 창을 닫을 때 한 번(OnCloseAsync).
		private void onEquipClicked()
		{
			PetBook book = Account.Instance.Pet;
			if (book.IsOwned(_selectedId) == false)
			{
				return;
			}

			if (book.IsEquipped(_selectedId) == true)
			{
				book.Unequip();
				return;
			}

			book.TryEquip(_selectedId);
		}

		// 강화는 즉시 적용하고 묶어 보낸다(PetEnhanceBatcher) — 연타·누른 채 반복 공용.
		private void onLevelUpClicked()
		{
			PetEnhanceBatcher.Instance.TryEnhance(_selectedId);
		}

		// 승급은 서버에 요청하고, 차감·변경은 응답에서 반영한다.
		private void onGradeUpClicked()
		{
			if (_pendingPromote == true || Account.Instance.Pet.GetPromoteBlock(_selectedId) != PetPromoteBlock.None)
			{
				return;
			}

			PetPromoteRequest request = new PetPromoteRequest();
			request.petId = _selectedId;
			_pendingPromote = true;
			NetworkManager.Instance.RequestPetPromote(request, onPromoteResponse);
		}

		// 계정 반영은 창이 닫혔어도 한다 — 서버에는 이미 저장됐다. 화면 갱신은 PetChangeEvent 가 한다.
		private void onPromoteResponse(bool success, PetGrowthResponse data, string error)
		{
			_pendingPromote = false;

			if (success == false || data == null)
			{
				Debug.LogWarning($"[PetInfoPresenter] 펫 승급 실패: {error}");
				return;
			}

			Account.Instance.Pet.ApplyGrowthResponse(data);
		}

		// 정렬 버튼 — 기준을 다음 것으로 넘기고 라벨을 갱신한 뒤 다시 그린다.
		private void onSortClicked()
		{
			_sortMode = (SortModes)(((int)_sortMode + 1) % SORT_MODE_COUNT);
			PlayerPrefs.SetInt(SORT_MODE_PREF_KEY, (int)_sortMode);
			PlayerPrefs.Save();

			view.SetSortLabel(getSortLabel(_sortMode));
			render();
		}

		// 창을 닫는다. 아래에 장비창이 그대로 살아 있으므로 그것이 다시 보인다.
		private void onReturnClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onPetChanged(PetChangeEvent e)
		{
			render();
		}

		private void onResourceChanged(ResourceChangeEvent e)
		{
			renderDetail();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 아이콘 로드가 await 라 연속 호출이 겹칠 수 있다 — 직전 렌더는 취소한다.
		private void render()
		{
			PetBook book = Account.Instance.Pet;

			view.SetTitle(book.OwnedCount, PetCatalog.Count);

			buildSlotData(book);

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderListAsync(_slotData, _renderCts.Token).Forget();

			renderDetail();
		}

		// 무대·정보 판·비용 버튼. 미보유면 비용은 쓰이지 않는다(버튼 줄이 숨는다).
		private void renderDetail()
		{
			PetBook book = Account.Instance.Pet;
			Table_Pet.Row row = PetCatalog.Get(_selectedId);
			if (row == null)
			{
				return;
			}

			PetEntry entry = book.Find(_selectedId);
			bool owned = entry != null;

			PetDetailData data;
			data.owned = owned;
			data.equipped = book.IsEquipped(_selectedId);
			data.imageAddress = row.Image;
			data.name = row.Name;
			data.bonusText = buildBonusText(row, owned ? entry.Level : 0, owned);
			data.equipText = row.EquipText;
			data.acquireText = ACQUIRE_TEXT;

			if (owned == true)
			{
				ItemGradeColorTable.GradeColor color = view.GradeColors.Get(entry.Grade);
				data.gradeText = $"<color=#{ColorUtility.ToHtmlStringRGB(color.text)}>{ItemGradeNames.Get(entry.Grade)}</color>";
				data.levelText = $"Lv.{entry.Level} / {entry.MaxLevel}";
				data.levelRatio = (entry.MaxLevel > 0) ? (float)entry.Level / entry.MaxLevel : 0f;
				data.glowColor = color.border;

				view.SetLevelUpCost(buildEnhanceCost(book, entry));
				view.SetGradeUpCost(buildPromoteCost(book, entry));
			}
			else
			{
				data.gradeText = string.Empty;
				data.levelText = string.Empty;
				data.levelRatio = 0f;
				data.glowColor = LockedBg;	// 미보유 무대는 USS 의 흐린 빛을 쓴다 — 쓰이지 않는다
			}

			if (_detailCts != null)
			{
				_detailCts.Cancel();
				_detailCts.Dispose();
			}

			_detailCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.SetDetailAsync(data, _detailCts.Token).Forget();
		}

		private void buildSlotData(PetBook book)
		{
			_rows.Clear();

			IReadOnlyList<Table_Pet.Row> all = PetCatalog.AllSorted();
			for (int i = 0; i < all.Count; i++)
			{
				_rows.Add(all[i]);
			}

			sortRows(book);
			ensureSelection(book);

			_slotData.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				_slotData.Add(buildSlot(book, _rows[i]));
			}
		}

		// 아직 고르지 않았거나 고른 펫이 목록에 없으면 장착 펫, 없으면 목록 첫 칸을 고른다.
		private void ensureSelection(PetBook book)
		{
			for (int i = 0; i < _rows.Count; i++)
			{
				if (_rows[i].ID == _selectedId)
				{
					return;
				}
			}

			_selectedId = book.Equipped;
			if (PetCatalog.Get(_selectedId) == null && _rows.Count > 0)
			{
				_selectedId = _rows[0].ID;
			}
		}

		private PetSlotData buildSlot(PetBook book, Table_Pet.Row row)
		{
			bool owned = book.IsOwned(row.ID);

			PetSlotData data;
			data.pet = row.ID;
			data.owned = owned;
			data.imageAddress = row.Image;
			data.name = row.Name;
			data.level = book.GetLevel(row.ID);
			data.equipped = book.IsEquipped(row.ID);
			data.selected = row.ID == _selectedId;

			if (owned == true)
			{
				ItemGradeColorTable.GradeColor color = view.GradeColors.Get(book.GetGrade(row.ID));
				data.bgColor = color.bg;
				data.gradientColor = color.border;
			}
			else
			{
				data.bgColor = LockedBg;
				data.gradientColor = LockedBg;	// 미보유 칸은 USS 잠금 색을 쓴다 — 쓰이지 않는다
			}

			return data;
		}

		// 보유는 수치까지, 미보유는 무엇이 오르는지만 — 잠긴 칸에 정확한 수치를 보여 줄 이유가 없다.
		private static string buildBonusText(Table_Pet.Row row, int level, bool owned)
		{
			StatDetail detail;
			if (StatOptionText.TryGetStatDetail(row.Opt_ID, out detail) == false)
			{
				return string.Empty;
			}

			if (owned == false)
			{
				return StatOptionText.FormatIncrease(detail);
			}

			return StatOptionText.FormatStat(detail, PetEntry.GetOptionValue(row, level), false);
		}

		// 비용 표시는 Block 하나로 갈린다 — 버튼을 잠그는 조건이 곧 실행을 막는 조건이다.
		private PetCostData buildEnhanceCost(PetBook book, PetEntry entry)
		{
			PetEnhanceBlock block = book.GetEnhanceBlock(_selectedId);

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
			PetPromoteBlock block = book.GetPromoteBlock(_selectedId);

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

		// ── 정렬 ──────────────────────────────────────────────────────────

		private void sortRows(PetBook book)
		{
			// 비교자가 book 을 봐야 해서 필드에 걸어 둔다 (람다를 쓰지 않는 대신).
			_sortBook = book;

			switch (_sortMode)
			{
				case SortModes.Grade:
					_rows.Sort(compareByGrade);
					break;

				case SortModes.Level:
					_rows.Sort(compareByLevel);
					break;

				default:
					_rows.Sort(compareByOwned);
					break;
			}

			_sortBook = null;
		}

		private PetBook _sortBook;

		// 어느 기준이든 같은 값이면 테이블 순서로 떨어뜨린다 — 목록이 매번 뒤바뀌지 않게.
		private int compareByOwned(Table_Pet.Row a, Table_Pet.Row b)
		{
			int ownedA = _sortBook.IsOwned(a.ID) ? 1 : 0;
			int ownedB = _sortBook.IsOwned(b.ID) ? 1 : 0;
			if (ownedA != ownedB)
			{
				return ownedB.CompareTo(ownedA);
			}

			return ((int)a.ID).CompareTo((int)b.ID);
		}

		private int compareByGrade(Table_Pet.Row a, Table_Pet.Row b)
		{
			int gradeA = (int)_sortBook.GetGrade(a.ID);
			int gradeB = (int)_sortBook.GetGrade(b.ID);
			if (gradeA != gradeB)
			{
				return gradeB.CompareTo(gradeA);
			}

			return compareByOwned(a, b);
		}

		private int compareByLevel(Table_Pet.Row a, Table_Pet.Row b)
		{
			int levelA = _sortBook.GetLevel(a.ID);
			int levelB = _sortBook.GetLevel(b.ID);
			if (levelA != levelB)
			{
				return levelB.CompareTo(levelA);
			}

			return compareByOwned(a, b);
		}

		private void loadSortMode()
		{
			int saved = PlayerPrefs.GetInt(SORT_MODE_PREF_KEY, (int)SortModes.Owned);
			if (saved < 0 || saved >= SORT_MODE_COUNT)
			{
				saved = (int)SortModes.Owned;
			}

			_sortMode = (SortModes)saved;
		}

		private static string getSortLabel(SortModes mode)
		{
			switch (mode)
			{
				case SortModes.Grade:
					return "등급순";

				case SortModes.Level:
					return "강화도순";

				default:
					return "보유순";
			}
		}
	}
}
