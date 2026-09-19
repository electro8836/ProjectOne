using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Costumes;
using ProjectOne.Mastery;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 코스튬 목록 창 Presenter — 무엇을 어떤 순서·색·문구로 깔지 정한다.
	//
	// 착용은 CostumeBook 이 곧바로 아바타를 다시 그리므로 여기서는 호출만 하고 결과를 다시 렌더한다.
	// (서버 저장은 아직 없다 — 재접속하면 착용이 초기화된다.)
	public sealed class CostumeInfoPresenter : Presenter<CostumeInfoUI>
	{
		// 탭 인덱스 — 프리펩 TabMenu_Middle 의 Hierarchy 순서와 일대일로 맞춘다.
		private const int TAB_WEAPON = 0;
		private const int TAB_ARMOR = 1;

		// 보유 칸 — 어두운 보라 계열.
		private static readonly Color OwnedBg = new Color32(0x2C, 0x20, 0x49, 0xFF);
		private static readonly Color OwnedInnerBorder = new Color32(0x4A, 0x3C, 0x6D, 0xFF);
		private static readonly Color OwnedBorder = new Color32(0x25, 0x1C, 0x3E, 0xFF);

		// 미보유 칸 — 무채색으로 빠진다.
		private static readonly Color LockedBg = new Color32(0x3A, 0x3A, 0x3A, 0xFF);
		private static readonly Color LockedInnerBorder = new Color32(0x60, 0x60, 0x60, 0xFF);
		private static readonly Color LockedBorder = new Color32(0x3A, 0x3A, 0x3A, 0xFF);

		// 장착 버튼 — 장착 중이면 해제(빨강), 아니면 장착(초록).
		private static readonly Color EquipColor = new Color32(0x27, 0xAE, 0x60, 0xFF);
		private static readonly Color UnequipColor = new Color32(0xC0, 0x39, 0x2B, 0xFF);

		// 미리보기 버튼 아이콘 — 지금 무엇을 입어보는 중인지 목록에서 알 수 있게 한다.
		private static readonly Color PreviewingIconColor = new Color32(0x2C, 0xC5, 0x00, 0xFF);
		private static readonly Color PreviewIconColor = Color.white;

		private int _currentTab = TAB_WEAPON;

		// 입어보기 중인 코스튬. -1 이면 그 부위는 실제 착용 그대로다.
		private int _previewWeaponId = -1;
		private int _previewBodyId = -1;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		private readonly List<Table_Costume.Row> _rows = new List<Table_Costume.Row>();
		private readonly List<CostumeSlotData> _slotData = new List<CostumeSlotData>();

		// 비교자가 보유 여부를 봐야 해서 필드에 걸어 둔다 (람다를 쓰지 않는 대신).
		private CostumeBook _sortBook;

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnPreviewClicked += onPreviewClicked;
			view.OnEquipClicked += onEquipClicked;
			view.OnReturnClicked += onReturnClicked;
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnTabSelected -= onTabSelected;
			view.OnPreviewClicked -= onPreviewClicked;
			view.OnEquipClicked -= onEquipClicked;
			view.OnReturnClicked -= onReturnClicked;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			_currentTab = TAB_WEAPON;
			view.SelectTab(TAB_WEAPON);	// Select 는 OnTabChanged 를 발행하지 않으므로 직접 render
			render();

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		// 탭을 옮기면 입어보기를 푼다 — 무기 탭에서 입어본 것이 바디 목록에 남아 있으면 무엇을 보는지 헷갈린다.
		private void onTabSelected(int index)
		{
			_currentTab = index;
			clearPreview();
			render();
		}

		// 보유 여부를 보지 않는다 — 미보유 코스튬도 입어볼 수 있어야 살지 말지 정할 수 있다.
		// 입어보는 중인 것을 다시 누르면 해제다(-1 을 넣어 실제 착용으로 되돌린다).
		private void onPreviewClicked(int costumeId)
		{
			Table_Costume.Row row = CostumeCatalog.Get(costumeId);
			if (row == null)
			{
				return;
			}

			if (row.CostumeType == CostumeType.Weapon)
			{
				_previewWeaponId = (_previewWeaponId == costumeId) ? -1 : costumeId;
			}
			else
			{
				_previewBodyId = (_previewBodyId == costumeId) ? -1 : costumeId;
			}

			view.SetPreviewCostume(_previewWeaponId, _previewBodyId);

			// 미리보기 아이콘 색이 슬롯에 있으므로 목록도 다시 그린다.
			render();
		}

		// 장착 중인 것을 다시 누르면 해제다(0 을 넘긴다).
		private void onEquipClicked(int costumeId)
		{
			Table_Costume.Row row = CostumeCatalog.Get(costumeId);
			if (row == null)
			{
				return;
			}

			CostumeBook book = Account.Instance.Costume;

			// 입어보기를 먼저 푼다 — 그래야 착용 직후의 외형이 실제 착용 상태로 보인다.
			clearPreview();

			if (row.CostumeType == CostumeType.Weapon)
			{
				book.EquipWeapon(book.EquippedWeaponId == costumeId ? 0 : costumeId);
			}
			else
			{
				book.EquipBody(book.EquippedBodyId == costumeId ? 0 : costumeId);
			}

			render();
		}

		// 창을 닫는다. 아래에 장비창이 그대로 살아 있으므로 그것이 다시 보인다.
		// 입어보기는 이 창과 함께 사라진다 — 프리뷰 리그가 창에 딸려 있기 때문이다.
		private void onReturnClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		private void clearPreview()
		{
			_previewWeaponId = -1;
			_previewBodyId = -1;
			view.SetPreviewCostume(-1, -1);
		}

		// 아이콘 로드가 await 라 연속 호출이 겹칠 수 있다 — 직전 렌더는 취소한다.
		private void render()
		{
			CostumeBook book = Account.Instance.Costume;

			buildSlotData(book);

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderListAsync(_slotData, _renderCts.Token).Forget();
		}

		private void buildSlotData(CostumeBook book)
		{
			CostumeType type = (_currentTab == TAB_ARMOR) ? CostumeType.Body : CostumeType.Weapon;

			_rows.Clear();

			Dictionary<int, Table_Costume.Row> all = Table_Costume.All();
			Dictionary<int, Table_Costume.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Costume.Row row = e.Current.Value;
				if (row.CostumeType != type)
				{
					continue;
				}

				// 기본 코스튬은 목록에서 뺀다 — 벗은 상태로 돌아가는 폴백일 뿐이라 입고 벗을 대상이 아니다.
				if (row.IsDefault == true)
				{
					continue;
				}

				_rows.Add(row);
			}

			sortRows(book);

			_slotData.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				_slotData.Add(buildSlot(book, _rows[i]));
			}
		}

		private CostumeSlotData buildSlot(CostumeBook book, Table_Costume.Row row)
		{
			bool owned = book.IsOwned(row.ID);
			bool equipped = (row.CostumeType == CostumeType.Weapon)
				? book.EquippedWeaponId == row.ID
				: book.EquippedBodyId == row.ID;
			bool previewing = (row.CostumeType == CostumeType.Weapon)
				? _previewWeaponId == row.ID
				: _previewBodyId == row.ID;

			CostumeSlotData data;
			data.costumeId = row.ID;
			data.owned = owned;
			data.iconAddress = row.Icon;
			data.name = row.Name;
			data.gradeText = buildGradeText(row);
			data.acquisitionText = row.Acquisition;

			data.bgColor = owned ? OwnedBg : LockedBg;
			data.innerBorderColor = owned ? OwnedInnerBorder : LockedInnerBorder;
			data.borderColor = owned ? OwnedBorder : LockedBorder;

			data.equipButtonColor = equipped ? UnequipColor : EquipColor;
			data.equipLabel = equipped ? "해제" : "장착";
			data.previewIconColor = previewing ? PreviewingIconColor : PreviewIconColor;

			return data;
		}

		// 등급명에 등급색을 입히고, 무기 코스튬이면 어느 마스터리용인지 덧붙인다 — "<color=#..>신화</color> / 쌍검".
		private string buildGradeText(Table_Costume.Row row)
		{
			ItemGradeColorTable.GradeColor color = view.GradeColors.Get(row.Grade);
			string text = $"<color=#{ColorUtility.ToHtmlStringRGB(color.text)}>{ItemGradeNames.Get(row.Grade)}</color>";

			if (row.CostumeType != CostumeType.Weapon)
			{
				return text;
			}

			Table_WeaponMastery.Row mastery = MasteryCatalog.GetByWeaponType(row.WeaponType);
			if (mastery == null)
			{
				return text;
			}

			return $"{text} / {mastery.Name}";
		}

		// ── 정렬 ──────────────────────────────────────────────────────────

		// 보유 먼저 → 등급 내림차순 → ID 오름차순. 같은 값이면 테이블 순서로 떨어뜨려 목록이 매번 뒤바뀌지 않게 한다.
		private void sortRows(CostumeBook book)
		{
			_sortBook = book;
			_rows.Sort(compareRow);
			_sortBook = null;
		}

		private int compareRow(Table_Costume.Row a, Table_Costume.Row b)
		{
			int ownedA = _sortBook.IsOwned(a.ID) ? 1 : 0;
			int ownedB = _sortBook.IsOwned(b.ID) ? 1 : 0;
			if (ownedA != ownedB)
			{
				return ownedB.CompareTo(ownedA);
			}

			int gradeA = (int)a.Grade;
			int gradeB = (int)b.Grade;
			if (gradeA != gradeB)
			{
				return gradeB.CompareTo(gradeA);
			}

			return a.ID.CompareTo(b.ID);
		}
	}
}
