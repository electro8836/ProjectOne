using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 캐릭터 리스트 화면. UIManager.OpenOverlayAsync로 오버레이 캔버스에 열린다.
	// 생명주기(로드/생성/파괴/스택)는 UIManager가 담당하고,
	// 이 클래스는 자기 내부 콘텐츠(정렬/카운트/슬롯 바인딩)만 책임진다. 슬롯은 표시 전용.
	public class CharacterUI : UIScreen
	{
		// 캐릭터 디테일 팝업 Addressable 주소.
		private const string DETAIL_POPUP_ADDRESS = "Prefab_CharacterDetailPopup";

		[SerializeField] private UIButton _closeButton;	// Button_Return
		[SerializeField] private UIButton _sortButton;	// Button_Sorting (등급 정렬 토글)

		[Header("리스트")]
		[SerializeField] private RectTransform _gridParent;				// CharacterCardGrid
		[SerializeField] private CharacterSlot _slotPrefab;				// Prefab_CharacterSlot
		[SerializeField] private CharacterGradeColorTable _gradeColors;	// 캐릭터 등급 색상 SO
		[SerializeField] private TMP_Text _collectionCountText;			// CollectionCount

		[Header("클래스 아이콘 (Melee/Ranged/Mage 순)")]
		[SerializeField] private Sprite[] _classIcons;

		private bool _descending = true;	// true=고등급→저등급 (기본)

		private readonly List<CharacterSlot> _slots = new List<CharacterSlot>();
		private readonly List<Table_Character.Row> _rows = new List<Table_Character.Row>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();		// applyToSlotsAsync 일괄 대기용

		private CancellationTokenSource _rebuildCts;	// rebuild 단위 취소 (연속 호출 경합 방지)

		private void Awake()
		{
			_closeButton.OnClickEvent += onCloseClicked;
			_sortButton.OnClickEvent += onSortClicked;

			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);
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

			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			// 아이콘은 부트에서 아틀라스(IconAtlasCache)로 상주하므로 프리로드 대기 없이 곧바로 슬롯을 만든다.
			// → 슬롯이 즉시 생성되고 동기 아틀라스 조회로 아이콘까지 채워진다.
			rebuild();
			return UniTask.CompletedTask;
		}

		public override UniTask OnCloseAsync()
		{
			// 메인 선택/장착 변경이 있으면 서버에 1회 저장 (미로그인·비-dirty 는 내부에서 무시)
			NetworkManager.Instance.FlushLoadoutIfDirty();
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

		private void onCharacterChanged(CharacterChangeEvent e)
		{
			rebuild();
		}

		// 슬롯 클릭 → 해당 캐릭터의 디테일 팝업을 연다.
		private void onSlotClicked(int characterId)
		{
			UIManager.Instance.ShowCharacterDetailPopupAsync(DETAIL_POPUP_ADDRESS, characterId, this.GetCancellationTokenOnDestroy()).Forget();
		}

		// 전체 캐릭터를 정렬해 슬롯에 바인딩한다.
		// 아이콘 로드를 일괄 대기하므로 비동기로 진행하되, 이전 rebuild는 취소한다.
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

			List<Table_Character.Row> all = new List<Table_Character.Row>(Table_Character.All().Values);
			for (int i = 0; i < all.Count; i++)
			{
				_rows.Add(all[i]);
			}
		}

		// 등급 정렬(방향 적용) 후 동급은 ID 오름차순.
		private int compareRows(Table_Character.Row a, Table_Character.Row b)
		{
			int ga = (int)a.Grade;
			int gb = (int)b.Grade;
			if (ga != gb)
			{
				return _descending ? gb.CompareTo(ga) : ga.CompareTo(gb);
			}

			return a.ID.CompareTo(b.ID);
		}

		// CollectionCount: HEROES (보유수 / 전체수)
		private void updateCollectionCount()
		{
			Loadout loadout = Account.Instance.Loadout;
			int owned = 0;
			for (int i = 0; i < _rows.Count; i++)
			{
				if (loadout.Has(_rows[i].ID))
				{
					owned++;
				}
			}

			_collectionCountText.text = string.Format("HEROES ({0} / {1})", owned, _rows.Count);
		}

		private async UniTask applyToSlotsAsync(CancellationToken ct)
		{
			Loadout loadout = Account.Instance.Loadout;
			int selectedId = loadout.Selected;

			_bindTasks.Clear();
			for (int i = 0; i < _rows.Count; i++)
			{
				CharacterSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				Table_Character.Row row = _rows[i];
				bool owned = loadout.Has(row.ID);
				int level = 0;
				if (owned)
				{
					OwnedCharacter oc = loadout.GetOwned(row.ID);
					if (oc != null)
					{
						level = oc.level;
					}
				}

				bool selected = owned && row.ID == selectedId;
				Sprite classIcon = iconForType(row.CharacterType);

				_bindTasks.Add(slot.Bind(row, owned, level, selected, _gradeColors, classIcon, ct));
			}

			// 남는 슬롯은 비활성화 (풀 재사용)
			for (int i = _rows.Count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 캐릭터 타입 → 클래스 아이콘 (_classIcons: Melee/Ranged/Mage 순)
		private Sprite iconForType(CharacterTypes type)
		{
			int index;
			switch (type)
			{
				case CharacterTypes.Class_Ranged: index = 1; break;
				case CharacterTypes.Class_Mage:   index = 2; break;
				default:                          index = 0; break;
			}

			if (_classIcons == null || index >= _classIcons.Length)
			{
				return null;
			}

			return _classIcons[index];
		}

		private CharacterSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			CharacterSlot slot = Instantiate(_slotPrefab, _gridParent);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);
			return slot;
		}
	}
}
