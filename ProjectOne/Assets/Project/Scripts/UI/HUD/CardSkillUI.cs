using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 로비 카드스킬 목록 화면. 보유 여부와 무관하게 전체 카드스킬을 등급순으로 나열한다.
	// 생명주기는 UIManager가 담당하고, 이 클래스는 정렬/카운트/슬롯 바인딩만 책임진다(표시 전용).
	// 강화 실행 인터랙션은 범위 밖 — 현재는 목록 표시 + 등급 정렬만.
	public class CardSkillUI : UIScreen
	{
		[SerializeField] private UIButton _closeButton;	// Button_Convex_LeftFlush_01_Gray
		[SerializeField] private UIButton _sortButton;	// Button_Sorting (등급 정렬 토글)

		[Header("리스트")]
		[SerializeField] private RectTransform _gridParent;		// ListFrame_01_GridLayout_Acheivement
		[SerializeField] private CardSkillSlot _slotPrefab;		// Prefab_CardSkillSlot
		[SerializeField] private TMP_Text _collectionCountText;	// CardCount

		[Header("비활성 탭 (Effect/Merge)")]
		[SerializeField] private UITabButton[] _inactiveTabs;

		private bool _descending = true;	// true=고등급→저등급 (기본)

		private readonly List<CardSkillSlot> _slots = new List<CardSkillSlot>();
		private readonly List<Table_CardSkill.Row> _rows = new List<Table_CardSkill.Row>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();	// applyToSlotsAsync 일괄 대기용

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		private Action<CardSkillChangedEvent> _onCardSkillChanged;

		private void Awake()
		{
			_closeButton.OnClickEvent += onCloseClicked;
			_sortButton.OnClickEvent += onSortClicked;

			// 나머지 두 탭은 회색(Locked)으로 비활성 — 클릭만 차단
			if (_inactiveTabs != null)
			{
				for (int i = 0; i < _inactiveTabs.Length; i++)
				{
					if (_inactiveTabs[i] != null)
					{
						_inactiveTabs[i].SetState(TabState.Locked, true);
					}
				}
			}

			_onCardSkillChanged = onCardSkillChanged;
			EventManager.Instance.Subscribe<CardSkillChangedEvent>(_onCardSkillChanged);
		}

		private void OnDestroy()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
				_rebuildCts = null;
			}

			_closeButton.OnClickEvent -= onCloseClicked;
			_sortButton.OnClickEvent -= onSortClicked;

			EventManager.Instance.Unsubscribe<CardSkillChangedEvent>(_onCardSkillChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			rebuild();
			return UniTask.CompletedTask;
		}

		private void onCloseClicked()
		{
			UIManager.Instance.CloseOverlayAsync().Forget();
		}

		private void onSortClicked()
		{
			_descending = !_descending;
			rebuild();
		}

		private void onCardSkillChanged(CardSkillChangedEvent e)
		{
			rebuild();
		}

		// 전체 카드스킬을 정렬해 슬롯에 바인딩한다. 아이콘 로드를 일괄 대기하므로 비동기로 진행하되 이전 rebuild는 취소.
		private void rebuild()
		{
			if (_rebuildCts != null)
			{
				_rebuildCts.Cancel();
				_rebuildCts.Dispose();
			}

			_rebuildCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
			rebuildAsync(_rebuildCts.Token).Forget();
		}

		private async UniTaskVoid rebuildAsync(CancellationToken ct)
		{
			collectRows();
			_rows.Sort(compareRows);
			updateCollectionCount();
			await applyToSlotsAsync(ct);
		}

		private void collectRows()
		{
			_rows.Clear();

			List<Table_CardSkill.Row> all = new List<Table_CardSkill.Row>(Table_CardSkill.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				_rows.Add(all[i]);
			}
		}

		// 등급 정렬(방향 적용) 후 동급은 ID 오름차순.
		private int compareRows(Table_CardSkill.Row a, Table_CardSkill.Row b)
		{
			int ga = (int)a.CardGrade;
			int gb = (int)b.CardGrade;
			if (ga != gb)
			{
				return _descending ? gb.CompareTo(ga) : ga.CompareTo(gb);
			}

			return a.ID.CompareTo(b.ID);
		}

		// CardCount: 카드스킬 (해금수 / 전체수)
		private void updateCollectionCount()
		{
			int unlocked = 0;
			for (int i = 0; i < _rows.Count; i++)
			{
				if (CardSkillResolver.IsUnlocked(_rows[i]) == true)
				{
					unlocked++;
				}
			}

			if (_collectionCountText != null)
			{
				_collectionCountText.text = string.Format("카드스킬 ({0} / {1})", unlocked, _rows.Count);
			}
		}

		private async UniTask applyToSlotsAsync(CancellationToken ct)
		{
			CardSkillBook book = Account.Instance.CardSkillBook;

			_bindTasks.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				CardSkillSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				Table_CardSkill.Row card = _rows[i];
				bool unlocked = CardSkillResolver.IsUnlocked(card);
				int ownedCount = book.GetCount(card.ID);
				// 해금된 카드는 미보유여도 최소 레벨 1 로 표시 (강화레벨 기본 1 기준)
				int level = Mathf.Max(book.GetEnchantLevel(card.ID), CardSkillResolver.MinLevel);
				int nextLevel = level + 1;
				int required = CardSkillResolver.RequiredCountForEnchant(card.CardGrade, nextLevel);
				bool maxed = level >= CardSkillResolver.MaxLevel || required <= 0;

				_bindTasks.Add(slot.Bind(card, unlocked, ownedCount, level, required, maxed, ct));
			}

			// 남는 슬롯은 비활성화 (풀 재사용)
			for (int i = _rows.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		private CardSkillSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			CardSkillSlot slot = Instantiate(_slotPrefab, _gridParent);
			_slots.Add(slot);
			return slot;
		}
	}
}
