using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Field;
using ProjectOne.Flow;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 포탈 UI — 액트를 고르고 그 액트의 필드를 고른다 (맵 설계 1.2).
	//
	// MapType=Field 인 것만 다룬다. 마을과 던전은 목록에 섞이지 않는다.
	// 필드 안에서 열면 같은 액트는 즉시 이동, 다른 액트는 그리드맵 교체로 처리한다.
	public class FieldSelectUI : UIScreen
	{
		[Header("액트")]
		[SerializeField] private TMP_Text _actNameText;
		[SerializeField] private UIButton _prevActButton;
		[SerializeField] private UIButton _nextActButton;

		[Header("필드 목록")]
		[SerializeField] private Transform _fieldGrid;
		[SerializeField] private FieldSelectSlot _slotPrefab;

		[Header("닫기")]
		[SerializeField] private UIButton _closeButton;

		private readonly List<Table_Act.Row> _acts = new List<Table_Act.Row>();
		private readonly List<Table_Field.Row> _fields = new List<Table_Field.Row>();
		private readonly List<FieldSelectSlot> _slots = new List<FieldSelectSlot>();

		private int _actIndex;

		private void Awake()
		{
			_prevActButton.OnClickEvent += onPrevActClicked;
			_nextActButton.OnClickEvent += onNextActClicked;
			_closeButton.OnClickEvent += onCloseClicked;
		}

		private void OnDestroy()
		{
			_prevActButton.OnClickEvent -= onPrevActClicked;
			_nextActButton.OnClickEvent -= onNextActClicked;
			_closeButton.OnClickEvent -= onCloseClicked;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			collectActs();

			// 필드에 있다면 현재 액트를 먼저 보여준다.
			_actIndex = 0;
			if (FieldDirector.HasInstance == true)
			{
				int index = _acts.FindIndex(matchesCurrentAct);
				if (index >= 0)
				{
					_actIndex = index;
				}
			}

			refresh();
			return UniTask.CompletedTask;
		}

		private bool matchesCurrentAct(Table_Act.Row row)
		{
			return row.ID == FieldDirector.Instance.CurrentActId;
		}

		// ── 목록 ──────────────────────────────────────────────────────

		private void collectActs()
		{
			_acts.Clear();
			_acts.AddRange(Table_Act.All().Values);
			_acts.Sort(compareActOrder);
		}

		private static int compareActOrder(Table_Act.Row a, Table_Act.Row b)
		{
			return a.Order.CompareTo(b.Order);
		}

		private void collectFields(int actId)
		{
			_fields.Clear();

			Dictionary<int, Table_Field.Row> all = Table_Field.All();
			Dictionary<int, Table_Field.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.ActID == actId)
				{
					_fields.Add(e.Current.Value);
				}
			}

			_fields.Sort(compareFieldOrder);
		}

		private static int compareFieldOrder(Table_Field.Row a, Table_Field.Row b)
		{
			return a.Order.CompareTo(b.Order);
		}

		// ── 표시 ──────────────────────────────────────────────────────

		private void refresh()
		{
			if (_acts.Count == 0)
			{
				return;
			}

			Table_Act.Row act = _acts[_actIndex];
			_actNameText.text = act.Name;

			_prevActButton.gameObject.SetActive(_actIndex > 0);
			_nextActButton.gameObject.SetActive(_actIndex < _acts.Count - 1);

			collectFields(act.ID);
			rebuildSlots();
		}

		private void rebuildSlots()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i] != null)
				{
					Destroy(_slots[i].gameObject);
				}
			}

			_slots.Clear();

			int level = Account.Instance.Loadout.Level;
			for (int i = 0; i < _fields.Count; i++)
			{
				Table_Field.Row field = _fields[i];
				Table_Map.Row map = Table_Map.Get(field.ID);

				// 입장 요구 레벨만 판정한다. ReqQuestID 판정은 퀘스트 시스템이 생기면 붙인다(STEP 12).
				bool unlocked = level >= field.ReqLevel;

				FieldSelectSlot slot = Instantiate(_slotPrefab, _fieldGrid);
				slot.Bind(field.ID, (map != null) ? map.Name : field.ID.ToString(), field.ReqLevel, unlocked, onFieldClicked);
				_slots.Add(slot);
			}
		}

		// ── 입력 ──────────────────────────────────────────────────────

		private void onPrevActClicked()
		{
			if (_actIndex <= 0)
			{
				return;
			}

			_actIndex--;
			refresh();
		}

		private void onNextActClicked()
		{
			if (_actIndex >= _acts.Count - 1)
			{
				return;
			}

			_actIndex++;
			refresh();
		}

		private void onFieldClicked(int fieldId)
		{
			UIManager.Instance.CloseWindowAsync().Forget();

			// 필드 밖(마을)이면 씬 전환, 필드 안이면 씬 전환 없이 이동/교체.
			if (FieldDirector.HasInstance == false)
			{
				GameFlow.Instance.ChangeStateAsync(new FieldState(fieldId)).Forget();
				return;
			}

			FieldDirector.Instance.ChangeActAsync(fieldId, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void onCloseClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}
	}
}
