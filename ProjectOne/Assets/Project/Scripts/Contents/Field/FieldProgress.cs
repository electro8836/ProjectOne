using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.UserData;

namespace ProjectOne.Field
{
	// 필드의 해금·진행 판정을 한곳에서 소유한다.
	//
	// 월드 화면·액트 목록·필드 슬롯 셋이 같은 질문("이 필드를 지났는가")을 하므로 판정이 흩어지면
	// 한쪽만 고쳐져 화면끼리 어긋난다.
	//
	// 클리어 여부는 Table_Field.ReqQuestID 로 판정한다 — 필드 클리어 기록을 따로 저장하지 않기 때문이다.
	// 메인 퀘스트가 요구치를 지났으면 그 필드는 열린 것으로 본다. 맵 포털 장막(MapPortal)도 같은 판정을 쓴다.
	public static class FieldProgress
	{
		private const string LAST_VISITED_FIELD_KEY = "LastVisitedFieldId";

		// 필드 목록 정렬용 버퍼. 액트 하나의 필드 수만큼만 담긴다.
		private static readonly List<Table_Field.Row> _sortBuffer = new List<Table_Field.Row>();

		// ReqQuestID 를 클리어했는가. 했으면 그 필드는 클리어(=이동 가능)다. 0 이면 항상 열림
		// (QuestCatalog.IsSpawnActive 와 같은 비교).
		public static bool IsCleared(Table_Field.Row field)
		{
			if (field == null)
			{
				return false;
			}

			return Account.Instance.Quests.ClearedQuestId >= field.ReqQuestID;
		}

		// 지금 서 있는 필드. 마을이거나 던전이면 0 이다 (FieldDirector 는 필드 씬에만 존재한다).
		public static int GetCurrentFieldId()
		{
			if (FieldDirector.HasInstance == false)
			{
				return 0;
			}

			return FieldDirector.Instance.CurrentFieldId;
		}

		// 마지막 방문 필드. 기기에만 남긴다 — 기기를 바꾸면 없어지고, 그때는 갈 수 있는 마지막 필드로 대신한다.
		public static void SetLastVisitedFieldId(int fieldId)
		{
			if (PlayerPrefs.GetInt(LAST_VISITED_FIELD_KEY, 0) == fieldId)
			{
				return;
			}

			PlayerPrefs.SetInt(LAST_VISITED_FIELD_KEY, fieldId);
			PlayerPrefs.Save();
		}

		// 화면이 기준으로 삼을 필드. 필드 안이면 지금 필드, 밖이면 마지막 방문 필드,
		// 기록이 없거나 지금은 갈 수 없는 필드면(같은 기기의 다른 계정 등) 갈 수 있는 마지막 필드다.
		public static int GetFocusFieldId()
		{
			int current = GetCurrentFieldId();
			if (current > 0)
			{
				return current;
			}

			int saved = PlayerPrefs.GetInt(LAST_VISITED_FIELD_KEY, 0);
			if (saved > 0 && IsCleared(Table_Field.Get(saved)) == true)
			{
				return saved;
			}

			return getLastUnlockedFieldId();
		}

		// 화면에 처음 띄울 액트 — 기준 필드가 속한 액트다. 기준 필드가 없으면 첫 액트.
		public static int GetCurrentActId()
		{
			Table_Field.Row field = Table_Field.Get(GetFocusFieldId());
			return field != null ? field.ActID : 1;
		}

		// 갈 수 있는 필드 중 (ActID, Order) 가 가장 뒤인 것. 하나도 없으면 0.
		private static int getLastUnlockedFieldId()
		{
			Table_Field.Row last = null;

			Dictionary<int, Table_Field.Row> all = Table_Field.All();
			Dictionary<int, Table_Field.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Field.Row field = e.Current.Value;
				if (IsCleared(field) == false)
				{
					continue;
				}

				if (last == null || field.ActID > last.ActID || (field.ActID == last.ActID && field.Order > last.Order))
				{
					last = field;
				}
			}

			return last != null ? last.ID : 0;
		}

		// 한 액트의 필드를 Order 순으로 담는다.
		// Table_Field.All() 은 Dictionary 라 순서를 보장하지 않는다 — 정렬하지 않으면 칸 위치가 매번 흔들린다.
		public static void CollectFields(int actId, List<Table_Field.Row> buffer)
		{
			buffer.Clear();

			Dictionary<int, Table_Field.Row> all = Table_Field.All();
			Dictionary<int, Table_Field.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.ActID == actId)
				{
					buffer.Add(e.Current.Value);
				}
			}

			buffer.Sort(compareOrder);
		}

		// 액트 진행률 0~1. 그 액트의 필드 중 클리어한 비율이다.
		public static float GetActProgress(int actId)
		{
			CollectFields(actId, _sortBuffer);
			if (_sortBuffer.Count == 0)
			{
				return 0f;
			}

			int cleared = 0;
			for (int i = 0; i < _sortBuffer.Count; i++)
			{
				if (IsCleared(_sortBuffer[i]) == true)
				{
					cleared++;
				}
			}

			return (float)cleared / _sortBuffer.Count;
		}

		private static int compareOrder(Table_Field.Row a, Table_Field.Row b)
		{
			return a.Order.CompareTo(b.Order);
		}
	}
}
