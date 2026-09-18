using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Pets;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 펫 목록 창 Presenter — 무엇을 어떤 순서·색·문구로 깔지 정한다.
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

		// 미보유 카드의 색. 등급색을 쓰지 않으므로 여기서만 산다.
		private static readonly Color LockedBg = new Color32(0x2A, 0x24, 0x47, 0xFF);
		private static readonly Color LockedText = new Color32(0x70, 0x5F, 0x9C, 0xFF);
		private static readonly Color OwnedText = Color.white;

		private SortModes _sortMode = SortModes.Owned;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		private readonly List<Table_Pet.Row> _rows = new List<Table_Pet.Row>();
		private readonly List<PetSlotData> _slotData = new List<PetSlotData>();

		protected override void OnInitialize()
		{
			view.OnSlotClicked += onSlotClicked;
			view.OnSortClicked += onSortClicked;
			view.OnReturnClicked += onReturnClicked;

			EventManager.Instance.Subscribe<PetChangeEvent>(onPetChanged);

			loadSortMode();
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnSlotClicked -= onSlotClicked;
			view.OnSortClicked -= onSortClicked;
			view.OnReturnClicked -= onReturnClicked;

			EventManager.Instance.Unsubscribe<PetChangeEvent>(onPetChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			view.SetSortLabel(getSortLabel(_sortMode));
			render();

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		// 잠긴 칸은 슬롯이 버튼을 잠가 두므로 여기까지 오지 않는다 — 와도 팝업이 스스로 되돌아 나온다.
		private void onSlotClicked(EDT.Pet petId)
		{
			UIManager.Instance.ShowPetEnhancePopupAsync(petId, view.GetDestroyToken()).Forget();
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

			_slotData.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				_slotData.Add(buildSlot(book, _rows[i]));
			}
		}

		private PetSlotData buildSlot(PetBook book, Table_Pet.Row row)
		{
			bool owned = book.IsOwned(row.ID);
			int level = book.GetLevel(row.ID);

			PetSlotData data;
			data.pet = row.ID;
			data.owned = owned;
			data.imageAddress = row.Image;
			data.name = row.Name;
			data.level = level;
			data.bonusText = buildBonusText(row, level, owned);
			data.equipped = book.IsEquipped(row.ID);
			data.textColor = owned ? OwnedText : LockedText;

			if (owned == true)
			{
				ItemGradeColorTable.GradeColor color = view.GradeColors.Get(book.GetGrade(row.ID));
				data.bgColor = color.bg;
				data.gradientColor = color.border;
			}
			else
			{
				data.bgColor = LockedBg;
				data.gradientColor = LockedBg;	// 꺼져 있어 보이지 않는다
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
