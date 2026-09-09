using System.Collections.Generic;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.Field
{
	// 필드의 해금·진행 판정을 한곳에서 소유한다.
	//
	// 월드 화면·액트 목록·필드 슬롯 셋이 같은 질문("이 필드를 지났는가")을 하므로 판정이 흩어지면
	// 한쪽만 고쳐져 화면끼리 어긋난다.
	//
	// 클리어 여부는 Table_Field.ReqLevel 로 판정한다 — 필드 클리어 기록을 따로 저장하지 않기 때문이다.
	// 캐릭터 레벨이 요구치를 넘으면 그 필드는 지난 것으로 본다.
	public static class FieldProgress
	{
		// 필드 목록 정렬용 버퍼. 액트 하나의 필드 수만큼만 담긴다.
		private static readonly List<Table_Field.Row> _sortBuffer = new List<Table_Field.Row>();

		// ReqLevel 을 채웠는가. 채웠으면 그 필드는 클리어(=이동 가능)다.
		public static bool IsCleared(Table_Field.Row field)
		{
			if (field == null)
			{
				return false;
			}

			return Account.Instance.Loadout.Level >= field.ReqLevel;
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

		// 화면에 처음 띄울 액트. 필드 안이면 그 액트, 밖(마을·던전)이면 지금까지 연 마지막 액트다.
		public static int GetCurrentActId()
		{
			if (FieldDirector.HasInstance == true)
			{
				return FieldDirector.Instance.CurrentActId;
			}

			return GetLastUnlockedActId();
		}

		// 클리어한 필드가 속한 액트 중 가장 뒤. 하나도 없으면 첫 액트다.
		public static int GetLastUnlockedActId()
		{
			int last = 0;

			Dictionary<int, Table_Field.Row> all = Table_Field.All();
			Dictionary<int, Table_Field.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Field.Row field = e.Current.Value;
				if (IsCleared(field) == false)
				{
					continue;
				}

				if (field.ActID > last)
				{
					last = field.ActID;
				}
			}

			return last > 0 ? last : 1;
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
