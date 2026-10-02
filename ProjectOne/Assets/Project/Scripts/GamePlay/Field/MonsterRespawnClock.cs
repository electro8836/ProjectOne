using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Shared;
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
	// 초 단위 리젠(RespawnType.Time)은 로컬 전용이고 저장하지 않는다 — 앱을 껐다 켜면 살아있는 상태로 시작한다. 의도된 동작이다.
	// 일일 리젠(RespawnType.DailyReset, 필드보스)은 서버가 소유한다 — 처치 기록(USER_FIELD.bossKills)을
	// 로그인 때 받고, 처치하면 바로 로컬에 표시한 뒤 서버(FieldBossKill)가 확정한다. 키는 (필드, 스폰 행)이다.
	public sealed class MonsterRespawnClock : Singleton<MonsterRespawnClock>
	{
		// 슬롯 → 리젠 가능해지는 시각
		private readonly Dictionary<SlotKey, float> _readyAt = new Dictionary<SlotKey, float>();

		// (필드, 스폰 행) → 처치한 초기화일(DailyReset.GetResetDay). 오늘과 같으면 아직 리젠 전이다.
		private readonly Dictionary<long, int> _bossKilledDay = new Dictionary<long, int>();

		protected MonsterRespawnClock() { }

		// 로그인 스냅샷 — 서버의 필드보스 처치 기록으로 덮는다.
		public void ApplyBossKills(List<FieldBossKillDto> kills)
		{
			_bossKilledDay.Clear();
			if (kills == null)
			{
				return;
			}

			for (int i = 0; i < kills.Count; i++)
			{
				if (kills[i] != null)
				{
					_bossKilledDay[bossKey(kills[i].fieldId, kills[i].spawnId)] = kills[i].resetDay;
				}
			}
		}

		// 필드보스 처치 기록 — 처치 즉시(오늘) 로컬에 남기고, 서버 응답이 오면 서버값으로 다시 남긴다.
		public void SetBossKilled(int fieldId, int spawnId, int resetDay)
		{
			_bossKilledDay[bossKey(fieldId, spawnId)] = resetDay;
		}

		// 오늘 처치한 필드보스인지 — 다음 06시까지 리젠되지 않는다.
		public bool IsBossKilledToday(int fieldId, int spawnId)
		{
			int day;
			return _bossKilledDay.TryGetValue(bossKey(fieldId, spawnId), out day) == true && day == DailyReset.GetResetDay();
		}

		private static long bossKey(int fieldId, int spawnId)
		{
			return ((long)fieldId << 32) | (uint)spawnId;
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
