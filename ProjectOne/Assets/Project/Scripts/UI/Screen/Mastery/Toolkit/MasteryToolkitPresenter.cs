using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Mastery;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Skill;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 마스터리 화면(UI Toolkit) Presenter — 현재 무기의 스킬 칸 6개 장착과 전체 마스터리 조회.
	//
	// 장착 흐름:
	//   스킬 행(또는 스킬이 든 칸)을 누르면 상세 팝업이 뜬다.
	//   팝업의 [장착] 은 빈 칸을 찾아 넣고, 빈 칸이 없으면 팝업을 닫고 바꿀 칸을 고르게 한다
	//   (넣을 수 있는 칸이 금색으로 빛나고, 그 칸을 누르면 장착 — 다른 칸에 있던 스킬이면 맞바꿈).
	//   [해제] 는 그 칸을 비운다.
	// 막힘 판정은 MasteryBook.GetSlotBlock 하나만 본다. 저장은 창을 닫을 때 FlushMasteryIfDirty 가 묶어 보낸다.
	// 스킬 목록은 종류 탭(전체·패시브·전술·코어)으로 거르고, 종류 → 장착 중 → 획득 → 미획득(해금 레벨) 순으로 늘어놓는다.
	public sealed class MasteryToolkitPresenter : Presenter<MasteryToolkitUI>
	{
		private const int TAB_CURRENT = 0;
		private const int TAB_ALL = 1;
		private const int FILTER_ALL = 0;	// 1 패시브 · 2 전술 · 3 코어 = 종류 순번 + 1

		// 목록 한 줄의 정렬 키
		private struct SkillEntry
		{
			public Table_MasterySkill.Row mastery;
			public Table_Skill.Row skill;
			public int categoryOrder;	// 0 패시브 · 1 전술 · 2 코어
			public int stateOrder;		// 0 장착 중 · 1 획득 · 2 미획득
			public bool unlocked;
			public bool equipped;
		}

		private int _currentTab = TAB_CURRENT;
		private int _skillFilter = FILTER_ALL;

		private EDT.Skill _selectedSkill = EDT.Skill.None;
		private bool _isPopupOpen;
		private bool _awaitingSlot;	// [장착] 을 눌렀는데 빈 칸이 없다 — 바꿀 칸을 누르길 기다린다

		// 현재 무기 렌더 버퍼
		private readonly List<MasterySlotViewData> _slotData = new List<MasterySlotViewData>();
		private readonly List<MasterySkillRowData> _skillData = new List<MasterySkillRowData>();
		private IReadOnlyList<Table_MasterySkill.Row> _skills;
		private readonly List<SkillEntry> _entries = new List<SkillEntry>();

		// 전체 마스터리 렌더 버퍼
		private readonly List<Table_WeaponMastery.Row> _masteryRows = new List<Table_WeaponMastery.Row>();
		private readonly List<MasteryInfoSlotData> _listData = new List<MasteryInfoSlotData>();
		private readonly List<Table_MasteryLevelBonus.Row> _bonusRows = new List<Table_MasteryLevelBonus.Row>();
		private readonly System.Text.StringBuilder _bonusBuilder = new System.Text.StringBuilder();

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnSkillFilterSelected += onSkillFilterSelected;
			view.OnSkillClicked += onSkillClicked;
			view.OnSlotClicked += onSlotClicked;
			view.OnEquipClicked += onEquipClicked;
			view.OnUnequipClicked += onUnequipClicked;
			view.OnPopupCloseClicked += onPopupCloseClicked;
			view.OnSlotHintCancelClicked += onSlotHintCancelClicked;

			EventManager.Instance.Subscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Subscribe<EquipmentChangeEvent>(onEquipmentChanged);
		}

		protected override void OnDispose()
		{
			view.OnTabSelected -= onTabSelected;
			view.OnSkillFilterSelected -= onSkillFilterSelected;
			view.OnSkillClicked -= onSkillClicked;
			view.OnSlotClicked -= onSlotClicked;
			view.OnEquipClicked -= onEquipClicked;
			view.OnUnequipClicked -= onUnequipClicked;
			view.OnPopupCloseClicked -= onPopupCloseClicked;
			view.OnSlotHintCancelClicked -= onSlotHintCancelClicked;

			EventManager.Instance.Unsubscribe<MasteryChangeEvent>(onMasteryChanged);
			EventManager.Instance.Unsubscribe<EquipmentChangeEvent>(onEquipmentChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			applyInitialTab();
			return UniTask.CompletedTask;
		}

		// 창을 닫을 때 바뀐 스킬 칸을 서버에 저장한다 — 조작마다 보내지 않고 여기서 한 번에 묶는다.
		public override UniTask OnCloseAsync()
		{
			NetworkManager.Instance.FlushMasteryIfDirty();
			return UniTask.CompletedTask;
		}

		// ── 초기 표시 ─────────────────────────────────────────────────────

		// 무기를 끼지 않으면 활성 마스터리가 없다 — 현재 무기 탭을 잠그고 전체 탭으로 시작한다.
		private void applyInitialTab()
		{
			bool hasMastery = Account.Instance.Mastery.CurrentMastery != null;

			clearSelection();

			view.SetCurrentTabLocked(hasMastery == false);
			_currentTab = hasMastery ? TAB_CURRENT : TAB_ALL;
			view.ShowTab(_currentTab);

			renderCurrentTab();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			_currentTab = index;
			view.ShowTab(index);
			renderCurrentTab();
		}

		private void onSkillFilterSelected(int index)
		{
			_skillFilter = index;
			renderCurrent();
		}

		// 스킬 행 → 상세 팝업 (행 순서는 거르고 정렬한 _entries 순서)
		private void onSkillClicked(int index)
		{
			if (index < 0 || index >= _entries.Count)
			{
				return;
			}

			openPopup(_entries[index].mastery.SkillID);
		}

		private void onSlotClicked(int index)
		{
			MasteryBook book = Account.Instance.Mastery;
			MasteryProgress progress = book.CurrentProgress;
			Table_WeaponMastery.Row row = book.CurrentMastery;
			if (progress == null || row == null)
			{
				return;
			}

			// 칸 고르는 중이면 빛나는 칸에 장착
			if (isTargetSlot(book, row.ID, progress, index) == true)
			{
				equip(index);
				return;
			}

			// 아니면 그 칸의 스킬 상세를 연다(빈 칸은 무시).
			EDT.Skill skill = progress.GetSlot(index);
			if (skill != EDT.Skill.None)
			{
				openPopup(skill);
			}
		}

		private void openPopup(EDT.Skill skill)
		{
			_selectedSkill = skill;
			_isPopupOpen = true;
			_awaitingSlot = false;
			renderCurrent();
		}

		private void onPopupCloseClicked()
		{
			clearSelection();
			renderCurrent();
		}

		private void onSlotHintCancelClicked()
		{
			clearSelection();
			renderCurrent();
		}

		private void clearSelection()
		{
			_selectedSkill = EDT.Skill.None;
			_isPopupOpen = false;
			_awaitingSlot = false;
		}

		private void onEquipClicked()
		{
			MasteryBook book = Account.Instance.Mastery;
			MasteryProgress progress = book.CurrentProgress;
			Table_WeaponMastery.Row row = book.CurrentMastery;
			if (progress == null || row == null || _selectedSkill == EDT.Skill.None)
			{
				return;
			}

			for (int i = 0; i < MasteryRules.SlotCount; i++)
			{
				if (progress.GetSlot(i) == EDT.Skill.None && book.GetSlotBlock(row.ID, i, _selectedSkill) == SlotBlock.None)
				{
					equip(i);
					return;
				}
			}

			// 빈 칸이 없다 — 팝업을 닫고 바꿀 칸을 고르게 한다
			_isPopupOpen = false;
			_awaitingSlot = true;
			renderCurrent();
		}

		private void onUnequipClicked()
		{
			MasteryBook book = Account.Instance.Mastery;
			MasteryProgress progress = book.CurrentProgress;
			Table_WeaponMastery.Row row = book.CurrentMastery;
			if (progress == null || row == null)
			{
				return;
			}

			int index = progress.IndexOfSlot(_selectedSkill);
			if (index < 0)
			{
				return;
			}

			// 상태를 먼저 비운다 — 성공하면 MasteryChangeEvent 가 그 상태로 다시 그린다.
			clearSelection();
			if (book.TrySetSlot(row.ID, index, EDT.Skill.None) == false)
			{
				renderCurrent();
			}
		}

		private void equip(int index)
		{
			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row row = book.CurrentMastery;
			EDT.Skill skill = _selectedSkill;

			// 상태를 먼저 비운다 — 성공하면 MasteryChangeEvent 가 그 상태로 다시 그린다.
			clearSelection();
			if (book.TrySetSlot(row.ID, index, skill) == false)
			{
				renderCurrent();
			}
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

		private void renderCurrentTab()
		{
			if (_currentTab == TAB_CURRENT)
			{
				renderCurrent();
				return;
			}

			renderAll();
		}

		private void renderCurrent()
		{
			MasteryBook book = Account.Instance.Mastery;
			MasteryProgress progress = book.CurrentProgress;
			Table_WeaponMastery.Row row = book.CurrentMastery;
			if (progress == null || row == null)
			{
				return;
			}

			_skills = MasteryCatalog.GetSkills(row.ID);

			_slotData.Clear();
			for (int i = 0; i < MasteryRules.SlotCount; i++)
			{
				EDT.Skill skill = progress.GetSlot(i);
				Table_Skill.Row skillRow = skill != EDT.Skill.None ? Table_Skill.Get(skill) : null;

				MasterySlotViewData data;
				data.iconAddress = skillRow != null ? skillRow.Icon : string.Empty;
				data.isEmpty = skill == EDT.Skill.None;
				data.isSelected = skill != EDT.Skill.None && skill == _selectedSkill;
				data.isTarget = isTargetSlot(book, row.ID, progress, i);
				data.isFocus = _skillFilter == FILTER_ALL || _skillFilter - 1 == getSlotCategoryOrder(i);
				_slotData.Add(data);
			}

			buildEntries(progress);

			_skillData.Clear();
			for (int i = 0; i < _entries.Count; i++)
			{
				SkillEntry entry = _entries[i];
				Table_Skill.Row skillRow = entry.skill;

				MasterySkillRowData data;
				data.iconAddress = skillRow != null ? skillRow.Icon : string.Empty;
				data.name = skillRow != null ? skillRow.Name : entry.mastery.SkillID.ToString();
				data.categoryLabel = getCategoryLabel(skillRow);
				data.categoryClass = getCategoryClass(skillRow);
				data.showCategory = _skillFilter == FILTER_ALL;
				data.isUnlocked = entry.unlocked;
				data.isEquipped = entry.equipped;
				data.isSelected = entry.mastery.SkillID == _selectedSkill;
				data.stateText = entry.unlocked == false ? $"Lv.{entry.mastery.UnlockLevel} 획득" : (entry.equipped ? "장착 중" : string.Empty);
				_skillData.Add(data);
			}

			view.ShowSkillFilter(_skillFilter);
			view.SetSlotHintVisible(_awaitingSlot);
			view.RenderPopup(buildPopup(row, progress));
			view.RenderCurrentAsync(buildInfo(row, progress), _slotData, _skillData, view.GetDestroyToken()).Forget();
		}

		// 종류 탭으로 거른 뒤 종류 → 장착 중 → 획득 → 미획득(해금 레벨) 순으로 정렬한다.
		private void buildEntries(MasteryProgress progress)
		{
			_entries.Clear();
			for (int i = 0; i < _skills.Count; i++)
			{
				SkillEntry entry;
				entry.mastery = _skills[i];
				entry.skill = Table_Skill.Get(entry.mastery.SkillID);
				entry.categoryOrder = getCategoryOrder(entry.skill);
				if (_skillFilter != FILTER_ALL && _skillFilter - 1 != entry.categoryOrder)
				{
					continue;
				}

				entry.unlocked = progress.IsUnlocked(entry.mastery.SkillID);
				entry.equipped = progress.IndexOfSlot(entry.mastery.SkillID) >= 0;
				entry.stateOrder = entry.equipped ? 0 : (entry.unlocked ? 1 : 2);
				_entries.Add(entry);
			}

			_entries.Sort(compareEntry);
		}

		private int compareEntry(SkillEntry a, SkillEntry b)
		{
			if (a.categoryOrder != b.categoryOrder)
			{
				return a.categoryOrder.CompareTo(b.categoryOrder);
			}

			if (a.stateOrder != b.stateOrder)
			{
				return a.stateOrder.CompareTo(b.stateOrder);
			}

			return a.mastery.UnlockLevel.CompareTo(b.mastery.UnlockLevel);
		}

		// 0 패시브 · 1 전술 · 2 코어 — 장착 칸 순서와 같다
		private static int getCategoryOrder(Table_Skill.Row skillRow)
		{
			if (skillRow == null)
			{
				return 1;
			}

			switch (skillRow.SkillCategory)
			{
				case SkillCategoryTypes.Passive:
					return 0;

				case SkillCategoryTypes.Ultimate:
					return 2;
			}

			return 1;
		}

		private static int getSlotCategoryOrder(int index)
		{
			if (index == MasteryRules.PassiveSlot)
			{
				return 0;
			}

			if (index == MasteryRules.UltimateSlot)
			{
				return 2;
			}

			return 1;
		}

		// 칸 고르는 중이고, 고른 스킬을 그 칸에 넣을 수 있고, 이미 그 칸에 있지 않다.
		private bool isTargetSlot(MasteryBook book, WeaponMastery id, MasteryProgress progress, int index)
		{
			if (_awaitingSlot == false || _selectedSkill == EDT.Skill.None || progress.GetSlot(index) == _selectedSkill)
			{
				return false;
			}

			return book.GetSlotBlock(id, index, _selectedSkill) == SlotBlock.None;
		}

		private MasterySkillDetailData buildPopup(Table_WeaponMastery.Row row, MasteryProgress progress)
		{
			MasterySkillDetailData data = default(MasterySkillDetailData);
			if (_isPopupOpen == false || _selectedSkill == EDT.Skill.None)
			{
				return data;
			}

			Table_Skill.Row skillRow = Table_Skill.Get(_selectedSkill);
			bool unlocked = progress.IsUnlocked(_selectedSkill);
			int slotIndex = progress.IndexOfSlot(_selectedSkill);
			bool equipped = slotIndex >= 0;
			bool locked = MasteryBook.IsSlotLocked;
			int unlockLevel = MasteryCatalog.GetUnlockLevel(row.ID, _selectedSkill);

			data.isOpen = true;
			data.iconAddress = skillRow != null ? skillRow.Icon : string.Empty;
			data.name = skillRow != null ? skillRow.Name : _selectedSkill.ToString();
			data.categoryLabel = getCategoryLabel(skillRow);
			data.categoryClass = getCategoryClass(skillRow);
			data.description = SkillDescriptionBuilder.Build(_selectedSkill);
			data.showEquip = unlocked == true && equipped == false;
			data.showUnequip = equipped;
			data.canInteract = locked == false;

			if (unlocked == false)
			{
				data.stateText = $"Lv.{unlockLevel} 해금 필요";
			}
			else if (equipped == true)
			{
				data.stateText = "장착 중 · " + getSlotLabel(slotIndex);
			}
			else
			{
				data.stateText = "장착 가능";
			}

			if (locked == true)
			{
				data.hint = "던전 안에서는 스킬을 바꿀 수 없습니다";
			}
			else if (unlocked == false)
			{
				data.hint = $"숙련도 {unlockLevel}레벨에 해금됩니다";
			}
			else
			{
				data.hint = string.Empty;
			}

			return data;
		}

		private static string getSlotLabel(int index)
		{
			if (index == MasteryRules.PassiveSlot)
			{
				return "패시브";
			}

			if (index == MasteryRules.UltimateSlot)
			{
				return "코어";
			}

			return "전술 " + index;
		}

		private MasteryInfoData buildInfo(Table_WeaponMastery.Row row, MasteryProgress progress)
		{
			int level = progress.Level;

			MasteryInfoData data;
			data.iconAddress = row.Icon;
			data.name = row.Name;
			data.level = level;
			data.isMaxLevel = level >= MasteryCatalog.MasteryMaxLevel;
			data.bonusText = buildMasteryBonusText(row, level);

			if (data.isMaxLevel == true)
			{
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

		// 화면 이름: 패시브 · 전술(Active) · 코어(Ultimate). 코드 enum 은 그대로 둔다.
		private static string getCategoryLabel(Table_Skill.Row skillRow)
		{
			if (skillRow == null)
			{
				return string.Empty;
			}

			switch (skillRow.SkillCategory)
			{
				case SkillCategoryTypes.Passive:
					return "패시브";

				case SkillCategoryTypes.Ultimate:
					return "코어";
			}

			return "전술";
		}

		private static string getCategoryClass(Table_Skill.Row skillRow)
		{
			if (skillRow == null)
			{
				return string.Empty;
			}

			switch (skillRow.SkillCategory)
			{
				case SkillCategoryTypes.Passive:
					return "chip--passive";

				case SkillCategoryTypes.Ultimate:
					return "chip--core";
			}

			return string.Empty;
		}

		// ── 전체 마스터리 ─────────────────────────────────────────────────

		private void renderAll()
		{
			int totalLevel = Account.Instance.Mastery.TotalLevel;
			int maxTotalLevel = MasteryCatalog.MasteryMaxLevel * MasteryCatalog.All().Count;

			MasteryTotalData total;
			total.totalLevel = totalLevel;
			total.maxTotalLevel = maxTotalLevel;
			total.bonusText = buildBonusText(totalLevel);
			total.isMax = maxTotalLevel > 0 && totalLevel >= maxTotalLevel;
			total.ratio = maxTotalLevel > 0 ? (float)totalLevel / maxTotalLevel : 0f;

			buildListData();
			view.RenderAllAsync(total, _listData, view.GetDestroyToken()).Forget();
		}

		// 마스터리 전종을 ID 오름차순으로 담는다 — Dictionary 는 순서를 보장하지 않는다.
		private void buildListData()
		{
			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row current = book.CurrentMastery;
			int maxLevel = MasteryCatalog.MasteryMaxLevel;

			_masteryRows.Clear();
			_masteryRows.AddRange(MasteryCatalog.All().Values);
			_masteryRows.Sort(compareMasteryId);

			_listData.Clear();
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
				_listData.Add(data);
			}
		}

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

		// 레벨 보너스는 총합 레벨에 한 번 곱해진다. 여러 옵션을 공백으로 이어 한 줄로 만든다.
		private string buildBonusText(int totalLevel)
		{
			_bonusRows.Clear();
			_bonusRows.AddRange(Table_MasteryLevelBonus.All().Values);
			_bonusRows.Sort(compareBonusId);

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
					_bonusBuilder.Append("   ");
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
