using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Field;
using ProjectOne.Map;

namespace ProjectOne.UI
{
	// 액트·필드 목록 팝업 Presenter — 어느 액트를 보여줄지와 필드 해금 판정을 담당한다.
	//
	// 처음 열 때는 지금 있는 액트를 편다. 마을이라면 서 있는 필드가 없으므로 마지막으로 연 액트를
	// 대신 편다 (FieldProgress.GetCurrentActId 가 그 판단을 소유한다).
	public sealed class ActListPopupPresenter : Presenter<ActListPopup>
	{
		// 액트 정렬 버퍼 — 탭 순서를 Order 로 고정해야 해서 필요하다.
		private readonly List<Table_Act.Row> _acts = new List<Table_Act.Row>();

		// 필드 렌더 버퍼 — 액트 하나의 필드 수만큼 매번 새로 담는다.
		private readonly List<Table_Field.Row> _fields = new List<Table_Field.Row>();
		private readonly List<FieldSlotData> _slotData = new List<FieldSlotData>();

		private int _currentTab;

		protected override void OnInitialize()
		{
			view.OnTabSelected += onTabSelected;
			view.OnMoveRequested += onMoveRequested;
		}

		protected override void OnDispose()
		{
			view.OnTabSelected -= onTabSelected;
			view.OnMoveRequested -= onMoveRequested;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			buildActs();
			view.SetTabCount(_acts.Count);

			_currentTab = indexOfAct(FieldProgress.GetCurrentActId());
			view.SelectTab(_currentTab);

			render();

			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(int index)
		{
			_currentTab = index;
			render();
		}

		// 이동은 목적지 ID 하나로 끝난다 — 어느 상태로 갈지는 MapNavigator 가 Map 테이블을 보고 정한다.
		//
		// 이동이 시작되면 팝업뿐 아니라 월드 창도 남아 있을 이유가 없다. 다만 창을 닫는 것은
		// 팝업의 일이 아니므로(팝업은 자기를 띄운 화면을 모른다) 이동했다는 사실만 결과로 넘긴다.
		private void onMoveRequested(int fieldId)
		{
			MapNavigator.MoveToMap(fieldId, view.GetDestroyToken());
			view.CloseByMove();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 액트 전종을 ID 순으로 담는다 — ID 가 곧 진행 순서다.
		// Dictionary 는 순서를 보장하지 않아 정렬하지 않으면 탭 위치가 흔들린다.
		private void buildActs()
		{
			_acts.Clear();

			Dictionary<int, Table_Act.Row> all = Table_Act.All();
			Dictionary<int, Table_Act.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				_acts.Add(e.Current.Value);
			}

			_acts.Sort(compareActId);
		}

		private void render()
		{
			if (_currentTab < 0 || _currentTab >= _acts.Count)
			{
				return;
			}

			Table_Act.Row act = _acts[_currentTab];
			view.SetActName(act.Name);

			buildSlotData(act.ID);
			view.RenderFields(_slotData);
		}

		private void buildSlotData(int actId)
		{
			FieldProgress.CollectFields(actId, _fields);
			int currentFieldId = FieldProgress.GetCurrentFieldId();

			_slotData.Clear();
			for (int i = 0; i < _fields.Count; i++)
			{
				Table_Field.Row field = _fields[i];

				FieldSlotData data;
				data.fieldId = field.ID;
				data.order = field.Order;
				data.name = field.Name;
				data.cleared = FieldProgress.IsCleared(field);
				data.current = field.ID == currentFieldId;
				_slotData.Add(data);
			}
		}

		// 액트 ID 로 탭 인덱스를 찾는다. 없으면 첫 탭 — 데이터가 비정상이어도 빈 화면을 보이지 않는다.
		private int indexOfAct(int actId)
		{
			for (int i = 0; i < _acts.Count; i++)
			{
				if (_acts[i].ID == actId)
				{
					return i;
				}
			}

			return 0;
		}

		private int compareActId(Table_Act.Row a, Table_Act.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
