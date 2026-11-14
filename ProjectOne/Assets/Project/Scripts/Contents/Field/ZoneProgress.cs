using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.UserData;

namespace ProjectOne.Field
{
	// 구역의 해금·진행 판정을 한곳에서 소유한다.
	//
	// 월드 화면·구역 목록·구역 관문 장막(ZoneGate) 셋이 같은 질문("이 구역이 열렸는가")을 하므로
	// 판정이 흩어지면 한쪽만 고쳐져 화면끼리 어긋난다.
	//
	// 해금은 Table_Zone.ReqQuestID 로 판정한다 — 구역 해금 기록을 따로 저장하지 않기 때문이다.
	// 메인 퀘스트가 요구치를 지났으면 그 구역은 열린 것으로 본다.
	public static class ZoneProgress
	{
		private const string LAST_VISITED_ZONE_KEY = "LastVisitedZoneId";

		// 구역 목록 정렬용 버퍼. 월드 하나의 구역 수만큼만 담긴다.
		private static readonly List<Table_Zone.Row> _sortBuffer = new List<Table_Zone.Row>();

		// ReqQuestID 를 클리어했는가. 했으면 그 구역은 열려 있다(=이동 가능). 0 이면 항상 열림
		// (QuestCatalog.IsSpawnActive 와 같은 비교).
		public static bool IsOpen(Table_Zone.Row zone)
		{
			if (zone == null)
			{
				return false;
			}

			return Account.Instance.Quests.ClearedQuestId >= zone.ReqQuestID;
		}

		// 지금 서 있는 구역. 마을이거나 던전이면 0 이다 (WorldDirector 는 월드 씬에만 존재한다).
		public static int GetCurrentZoneId()
		{
			if (WorldDirector.HasInstance == false)
			{
				return 0;
			}

			return WorldDirector.Instance.CurrentZoneId;
		}

		// 마지막 방문 구역. 기기에만 남긴다 — 기기를 바꾸면 없어지고, 그때는 갈 수 있는 마지막 구역으로 대신한다.
		public static void SetLastVisitedZoneId(int zoneId)
		{
			if (PlayerPrefs.GetInt(LAST_VISITED_ZONE_KEY, 0) == zoneId)
			{
				return;
			}

			PlayerPrefs.SetInt(LAST_VISITED_ZONE_KEY, zoneId);
			PlayerPrefs.Save();
		}

		// 화면이 기준으로 삼을 구역. 월드 안이면 지금 구역, 밖이면 마지막 방문 구역,
		// 기록이 없거나 지금은 갈 수 없는 구역이면(같은 기기의 다른 계정 등) 갈 수 있는 마지막 구역이다.
		public static int GetFocusZoneId()
		{
			int current = GetCurrentZoneId();
			if (current > 0)
			{
				return current;
			}

			int saved = PlayerPrefs.GetInt(LAST_VISITED_ZONE_KEY, 0);
			if (saved > 0 && IsOpen(Table_Zone.Get(saved)) == true)
			{
				return saved;
			}

			return getLastOpenZoneId();
		}

		// 화면에 처음 띄울 월드 — 기준 구역이 속한 월드다. 기준 구역이 없으면 첫 월드.
		public static int GetCurrentWorldId()
		{
			Table_Zone.Row zone = Table_Zone.Get(GetFocusZoneId());
			return zone != null ? zone.WorldID : 1;
		}

		// 갈 수 있는 구역 중 (WorldID, Order) 가 가장 뒤인 것. 하나도 없으면 0.
		private static int getLastOpenZoneId()
		{
			Table_Zone.Row last = null;

			Dictionary<int, Table_Zone.Row> all = Table_Zone.All();
			Dictionary<int, Table_Zone.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Zone.Row zone = e.Current.Value;
				if (IsOpen(zone) == false)
				{
					continue;
				}

				if (last == null || zone.WorldID > last.WorldID || (zone.WorldID == last.WorldID && zone.Order > last.Order))
				{
					last = zone;
				}
			}

			return last != null ? last.ID : 0;
		}

		// 한 월드의 구역을 Order 순으로 담는다.
		// Table_Zone.All() 은 Dictionary 라 순서를 보장하지 않는다 — 정렬하지 않으면 칸 위치가 매번 흔들린다.
		public static void CollectZones(int worldId, List<Table_Zone.Row> buffer)
		{
			buffer.Clear();

			Dictionary<int, Table_Zone.Row> all = Table_Zone.All();
			Dictionary<int, Table_Zone.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Value.WorldID == worldId)
				{
					buffer.Add(e.Current.Value);
				}
			}

			buffer.Sort(compareOrder);
		}

		// 월드 진행률 0~1. 그 월드의 구역 중 열린 비율이다.
		public static float GetWorldProgress(int worldId)
		{
			CollectZones(worldId, _sortBuffer);
			if (_sortBuffer.Count == 0)
			{
				return 0f;
			}

			int open = 0;
			for (int i = 0; i < _sortBuffer.Count; i++)
			{
				if (IsOpen(_sortBuffer[i]) == true)
				{
					open++;
				}
			}

			return (float)open / _sortBuffer.Count;
		}

		private static int compareOrder(Table_Zone.Row a, Table_Zone.Row b)
		{
			return a.Order.CompareTo(b.Order);
		}
	}
}
