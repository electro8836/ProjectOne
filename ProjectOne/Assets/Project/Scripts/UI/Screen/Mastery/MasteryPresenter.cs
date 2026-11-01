using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.Network;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.UI
{
	// 현재 마스터리 정보 1회분 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct MasteryInfoData
	{
		public string iconAddress;
		public string name;
		public int level;
		public float expRatio;		// 현재 레벨 구간의 진행률 0~1
		public bool isMaxLevel;
	}

	// 전체 마스터리 정보 1회분 렌더 데이터.
	public struct MasteryTotalData
	{
		public int totalLevel;		// 전 마스터리 레벨 합
		public int maxTotalLevel;	// 만렙 × 마스터리 종수
		public string bonusText;	// 레벨 보너스 옵션 문구를 한 줄로 이어붙인 것
		public float ratio;			// totalLevel / maxTotalLevel
		public bool isMax;
	}

	// 전체 마스터리 목록의 슬롯 1칸 렌더 데이터.
	public struct MasteryInfoSlotData
	{
		public string iconAddress;
		public string name;
		public int level;
		public int maxLevel;
		public string bonusText;
		public bool isActive;	// 착용 무기의 마스터리
	}

	// 마스터리 화면 Presenter — 어떤 페이지를 보여줄지와 현재 마스터리 조회를 담당한다.
	//
	// 무기를 끼지 않으면 활성 마스터리가 없다(MasteryBook.CurrentMastery 가 null). 그때는 현재 마스터리 탭을
	// 잠그고 전체 탭으로 시작한다 — 빈 화면을 보여주는 대신 아예 못 들어가게 한다.
	//
	// 스킬 해금 목록·슬롯 4칸 UI 는 새로 구성한다 — 조작 API 는 MasteryBook(TrySetSlot / GetSlotBlock),
	// 목록은 MasteryCatalog.GetSkills, 저장은 창을 닫을 때 FlushMasteryIfDirty 가 묶어 보낸다.
	//
	public sealed class MasteryPresenter : Presenter<MasteryUI>
	{
		// 탭 인덱스 — 프리펩 TabButtonGrid 의 Hierarchy 순서와 일대일로 맞춘다.
		private const int TAB_CURRENT = 0;
		private const int TAB_ALL = 1;

		private int _currentTab = TAB_CURRENT;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		// 레벨 보너스 문구 조립용 — 표시 순서를 ID 로 고정해야 해서 정렬 버퍼가 필요하다.
		private readonly List<Table_MasteryLevelBonus.Row> _bonusRows = new List<Table_MasteryLevelBonus.Row>();
		private readonly System.Text.StringBuilder _bonusBuilder = new System.Text.StringBuilder();

		// 목록 렌더 버퍼 — 마스터리 종수만큼 매번 새로 담는다.
		private readonly List<Table_WeaponMastery.Row> _masteryRows = new List<Table_WeaponMastery.Row>();
		private readonly List<MasteryInfoSlotData> _slotData = new List<MasteryInfoSlotData>();

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnHomeClicked += onHomeClicked;

			EventManager.Instance.Subscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
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
			view.OnHomeClicked -= onHomeClicked;

			EventManager.Instance.Unsubscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			applyInitialTab();
			return UniTask.CompletedTask;
		}

		// 창을 닫을 때 바뀐 스킬 슬롯을 서버에 저장한다 — 조작마다 보내지 않고 여기서 한 번에 묶는다.
		public override UniTask OnCloseAsync()
		{
			NetworkManager.Instance.FlushMasteryIfDirty();
			return UniTask.CompletedTask;
		}

		// ── 초기 표시 ─────────────────────────────────────────────────────

		// 착용 무기가 정하는 것 — 현재 마스터리 탭의 잠금 여부와 시작 탭.
		// TabGroup.Select 는 OnTabChanged 를 발행하지 않으므로 페이지 전환은 직접 호출한다.
		private void applyInitialTab()
		{
			bool hasMastery = Account.Instance.Mastery.CurrentMastery != null;

			view.SetCurrentTabLocked(hasMastery == false);

			_currentTab = hasMastery ? TAB_CURRENT : TAB_ALL;
			view.SelectTab(_currentTab);
			view.ShowPage(_currentTab == TAB_CURRENT);

			renderCurrentTab();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			_currentTab = index;
			view.ShowPage(index == TAB_CURRENT);

			renderCurrentTab();
		}

		// 창을 닫는다. 마지막 창이면 WindowClosedEvent 가 발행되어 네비게이션 바의 탭 선택도 함께 풀린다.
		private void onHomeClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onMasteryChanged(MasteryChangeEvent e)
		{
			renderCurrentTab();
		}

		// 무기를 바꾸면 활성 마스터리 자체가 달라진다 — 잠금과 탭부터 다시 판정한다.
		private void onEquipmentChanged(EquipmentChangeEvent e)
		{
			applyInitialTab();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 보이는 페이지만 그린다 — 꺼져 있는 쪽은 다시 열릴 때 갱신된다.
		private void renderCurrentTab()
		{
			if (_currentTab == TAB_CURRENT)
			{
				render();
				return;
			}

			renderAll();
		}

		// 아이콘 로드가 await 라 연속 호출이 겹칠 수 있다 — 직전 렌더는 취소한다.
		private void render()
		{
			MasteryProgress progress = Account.Instance.Mastery.CurrentProgress;
			Table_WeaponMastery.Row row = Account.Instance.Mastery.CurrentMastery;
			if (progress == null || row == null)
			{
				return;
			}

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderCurrentAsync(buildInfo(row, progress), _renderCts.Token).Forget();
		}

		private MasteryInfoData buildInfo(Table_WeaponMastery.Row row, MasteryProgress progress)
		{
			int level = progress.Level;

			MasteryInfoData data;
			data.iconAddress = row.Icon;
			data.name = row.Name;
			data.level = level;
			data.isMaxLevel = level >= MasteryCatalog.MasteryMaxLevel;

			if (data.isMaxLevel == true)
			{
				// 다음 레벨이 없다 — 게이지는 꽉 찬 상태로 두고 텍스트만 MAX 로 바꾼다.
				data.expRatio = 1f;
				return data;
			}

			// 테이블이 누적 경험치라 현재 레벨 구간으로 정규화해야 한다.
			int cur = MasteryCatalog.GetMasteryTotalExp(level);
			int next = MasteryCatalog.GetMasteryTotalExp(level + 1);
			int span = next - cur;
			data.expRatio = span > 0 ? (float)(progress.TotalExp - cur) / span : 0f;

			return data;
		}

		// ── 전체 마스터리 ─────────────────────────────────────────────────

		// 상단 정보는 동기지만 목록은 아이콘 로드가 await 다 — 현재 마스터리 렌더와 같은 토큰을 쓴다
		// (한 번에 한 페이지만 그리므로 서로 취소해도 문제가 없다).
		private void renderAll()
		{
			int totalLevel = Account.Instance.Mastery.TotalLevel;
			int maxTotalLevel = MasteryCatalog.MasteryMaxLevel * MasteryCatalog.All().Count;

			MasteryTotalData data;
			data.totalLevel = totalLevel;
			data.maxTotalLevel = maxTotalLevel;
			data.bonusText = buildBonusText(totalLevel);
			data.isMax = maxTotalLevel > 0 && totalLevel >= maxTotalLevel;
			data.ratio = maxTotalLevel > 0 ? (float)totalLevel / maxTotalLevel : 0f;

			view.RenderTotal(data);

			buildSlotData();

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderMasteryListAsync(_slotData, _renderCts.Token).Forget();
		}

		// 마스터리 전종을 ID 오름차순으로 담는다 — Dictionary 는 순서를 보장하지 않아 칸 위치가 흔들린다.
		private void buildSlotData()
		{
			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row current = book.CurrentMastery;
			int maxLevel = MasteryCatalog.MasteryMaxLevel;

			_masteryRows.Clear();
			_masteryRows.AddRange(MasteryCatalog.All().Values);
			_masteryRows.Sort(compareMasteryId);

			_slotData.Clear();
			for (int i = 0; i < _masteryRows.Count; i++)
			{
				Table_WeaponMastery.Row row = _masteryRows[i];
				MasteryProgress progress = book.Find(row.ID);
				int level = (progress != null) ? progress.Level : 1;

				MasteryInfoSlotData data;
				data.iconAddress = row.Icon;
				data.name = row.Name;
				data.level = level;
				data.maxLevel = maxLevel;
				data.bonusText = buildMasteryBonusText(row, level);
				data.isActive = current != null && current.ID == row.ID;
				_slotData.Add(data);
			}
		}

		// 마스터리 1종의 레벨 보너스. 옵션이 하나뿐이라 문구도 한 줄이다.
		private string buildMasteryBonusText(Table_WeaponMastery.Row row, int level)
		{
			StatDetail detail;
			if (StatOptionText.TryGetStatDetail(row.LevelBonusOption, out detail) == false)
			{
				return string.Empty;
			}

			return StatOptionText.FormatStat(detail, row.LevelBonusPerLevel * level, false);
		}

		private int compareMasteryId(Table_WeaponMastery.Row a, Table_WeaponMastery.Row b)
		{
			return ((int)a.ID).CompareTo((int)b.ID);
		}

		// 레벨 보너스는 마스터리별이 아니라 총합 레벨에 한 번 곱해진다.
		// 여러 옵션이 한 줄에 나란히 서므로 장비 옵션과 같은 문구 규칙을 쓰고 사이를 공백으로 띄운다.
		private string buildBonusText(int totalLevel)
		{
			_bonusRows.Clear();
			_bonusRows.AddRange(Table_MasteryLevelBonus.All().Values);
			_bonusRows.Sort(compareBonusId);	// Dictionary 는 순서를 보장하지 않는다

			_bonusBuilder.Length = 0;
			for (int i = 0; i < _bonusRows.Count; i++)
			{
				Table_MasteryLevelBonus.Row row = _bonusRows[i];

				StatDetail detail;
				if (StatOptionText.TryGetStatDetail(row.LevelBonusOption, out detail) == false)
				{
					continue;
				}

				if (_bonusBuilder.Length > 0)
				{
					_bonusBuilder.Append(' ');
				}

				_bonusBuilder.Append(StatOptionText.FormatStat(detail, row.LevelBonusPerLevel * totalLevel, false));
			}

			return _bonusBuilder.ToString();
		}

		private int compareBonusId(Table_MasteryLevelBonus.Row a, Table_MasteryLevelBonus.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
