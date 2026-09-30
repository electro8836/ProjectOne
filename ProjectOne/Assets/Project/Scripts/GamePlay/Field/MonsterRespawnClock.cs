using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Field
{
	// 필드 몬스터의 리젠 시각 원장 (몬스터 설계 8장).
	//
	// FieldMonsterSpawner 는 현재 필드의 슬롯만 들고 있고 필드를 떠나면 버린다.
	// 리젠 시각까지 같이 사라지면 "1-2 를 도는 동안 1-1 의 리젠이 흐른다"가 성립하지 않으므로
	// 시각만 여기로 뺐다. 순수 C# 싱글톤이라 씬 전환(마을·던전)에도 살아남는다.
	//
	// 기록이 없다 = 즉시 스폰 가능. 한 번도 죽인 적 없는 슬롯, 살아있는 채로 회수된 슬롯이 여기 해당한다.
	//
	// 리젠 시각은 로컬 전용이다. 서버에 올리지 않는다.
	// 초 단위 리젠(RespawnType.Time)은 저장하지 않는다 — 앱을 껐다 켜면 살아있는 상태로 시작한다. 의도된 동작이다.
	// 일일 리젠(RespawnType.DailyReset, 필드보스)만 처치한 초기화일을 PlayerPrefs 에 남긴다 —
	// 재시작으로 하루에 여러 번 잡는 것을 막기 위해서다. 초기화 경계는 DailyReset(06시)을 따른다.
	public sealed class MonsterRespawnClock : Singleton<MonsterRespawnClock>
	{
		// 기록 없음 표시 — PlayerPrefs 를 한 번 조회한 뒤 캐시해 다시 읽지 않는다.
		private const int NoRecord = -1;

		// 슬롯 → 리젠 가능해지는 시각
		private readonly Dictionary<SlotKey, float> _readyAt = new Dictionary<SlotKey, float>();

		// 슬롯 → 처치한 초기화일(DailyReset.GetResetDay). 오늘과 같으면 아직 리젠 전이다.
		private readonly Dictionary<SlotKey, int> _killedDay = new Dictionary<SlotKey, int>();

		protected MonsterRespawnClock() { }

		// 일일 리젠 — 오늘 처치했다고 기록한다. 다음 06시에 리젠된다.
		public void SetDailyRespawn(in SlotKey key)
		{
			int today = DailyReset.GetResetDay();
			_killedDay[key] = today;
			PlayerPrefs.SetInt(getPrefsKey(key), today);
			PlayerPrefs.Save();
		}

		// 리젠까지 남은 초. 기록이 없거나 이미 지났으면 0.
		public float GetRemainingSeconds(in SlotKey key)
		{
			if (isKilledToday(key) == true)
			{
				return (float)DailyReset.GetRemaining().TotalSeconds;
			}

			float ready;
			if (_readyAt.TryGetValue(key, out ready) == false)
			{
				return 0f;
			}

			return Mathf.Max(0f, ready - Time.time);
		}

		// 오늘 처치한 일일 리젠 슬롯인지. 날이 넘어간 기록은 여기서 지운다.
		private bool isKilledToday(in SlotKey key)
		{
			int day;
			if (_killedDay.TryGetValue(key, out day) == false)
			{
				day = PlayerPrefs.GetInt(getPrefsKey(key), NoRecord);
				_killedDay[key] = day;
			}

			if (day == NoRecord)
			{
				return false;
			}

			if (day == DailyReset.GetResetDay())
			{
				return true;
			}

			_killedDay[key] = NoRecord;
			PlayerPrefs.DeleteKey(getPrefsKey(key));
			return false;
		}

		private static string getPrefsKey(in SlotKey key)
		{
			return $"FieldDailyKill_{key.MapId}_{key.PointIndex}_{key.RowIndex}_{key.UnitIndex}";
		}

		public void SetRespawn(in SlotKey key, float respawnTime)
		{
			if (respawnTime <= 0f)
			{
				_readyAt.Remove(key);
				return;
			}

			_readyAt[key] = Time.time + respawnTime;
		}

		// 기록이 없으면 즉시 스폰 가능하다.
		public bool IsReady(in SlotKey key)
		{
			if (isKilledToday(key) == true)
			{
				return false;
			}

			float ready;
			if (_readyAt.TryGetValue(key, out ready) == false)
			{
				return true;
			}

			return Time.time >= ready;
		}

		public void Clear(in SlotKey key)
		{
			_readyAt.Remove(key);
		}

		// 개발/테스트용 초기화 경로. 필드 이동은 기록을 지우지 않는다.
		public void ClearAll()
		{
			_readyAt.Clear();
		}
	}

	// 스폰 슬롯 하나를 가리키는 키. 맵 프리팹을 다시 로드해도 같은 값이 나와야 한다.
	//
	// 스폰 포인트 목록은 TilemapGrid 가 GetComponentsInChildren 으로 계층 순서대로 수집하므로
	// 같은 프리팹이면 인덱스가 같다. 오브젝트 이름을 키로 쓰는 방식은 복제 시 "(1)" 이 붙어
	// 조용히 깨지므로 쓰지 않는다 (설계 12장).
	public readonly struct SlotKey : IEquatable<SlotKey>
	{
		// Field.ID 와 Map.ID 는 같은 값이다.
		public readonly int MapId;

		// MapManager.GetSpawnPoints(mapId) 안의 인덱스
		public readonly int PointIndex;

		// MonsterCatalog.GetSpawnGroup() 안의 행 인덱스
		public readonly int RowIndex;

		// 그 행의 Count 중 몇 번째 개체인지
		public readonly int UnitIndex;

		public SlotKey(int mapId, int pointIndex, int rowIndex, int unitIndex)
		{
			MapId = mapId;
			PointIndex = pointIndex;
			RowIndex = rowIndex;
			UnitIndex = unitIndex;
		}

		public bool Equals(SlotKey other)
		{
			return MapId == other.MapId
				&& PointIndex == other.PointIndex
				&& RowIndex == other.RowIndex
				&& UnitIndex == other.UnitIndex;
		}

		public override bool Equals(object obj)
		{
			return obj is SlotKey && Equals((SlotKey)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = MapId;
				hash = (hash * 397) ^ PointIndex;
				hash = (hash * 397) ^ RowIndex;
				hash = (hash * 397) ^ UnitIndex;
				return hash;
			}
		}

		public override string ToString()
		{
			return $"{MapId}/{PointIndex}/{RowIndex}/{UnitIndex}";
		}
	}
}
