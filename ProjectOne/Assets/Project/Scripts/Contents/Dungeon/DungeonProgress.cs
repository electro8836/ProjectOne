using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Dungeon
{
	// 던전 진행도 — 일일 입장 횟수 / 확장된 상한 / 최고 클리어 단계 (맵 설계 6절).
	//
	// 최고 클리어 단계는 일일 초기화 대상이 아니라 영구 누적이며,
	// **단계 해금과 Quest.DungeonClear 판정의 유일한 근거**다.
	//
	// TODO(STEP 14) — 지금은 인메모리다. 서버가 소유해야 하고 초기화 판정도 서버 시간 기준이어야 한다.
	public static class DungeonProgress
	{
		private sealed class Entry
		{
			public int usedToday;
			public int maxCount;		// DefaultEnterCount 에서 시작해 MaxEnterCount 까지 확장된다
			public int highestStage;	// 클리어한 최고 단계. 0이면 아직 하나도 못 깼다
		}

		private static readonly Dictionary<EDT.Dungeon, Entry> _byDungeon = new Dictionary<EDT.Dungeon, Entry>();

		// (DungeonType, Stage) → 행 조회 인덱스. 테이블이 평면이라 매번 순회하지 않도록 캐시한다.
		private static readonly Dictionary<long, Table_DungeonStage.Row> _stageIndex = new Dictionary<long, Table_DungeonStage.Row>();

		// 던전별 마지막 단계 번호
		private static readonly Dictionary<EDT.Dungeon, int> _lastStage = new Dictionary<EDT.Dungeon, int>();

		private static bool _built;

		// 테이블 로드 이후 1회. StatCatalog / SkillParamCatalog 와 같은 패턴이다.
		public static void Build()
		{
			_stageIndex.Clear();
			_lastStage.Clear();

			Dictionary<int, Table_DungeonStage.Row> all = Table_DungeonStage.All();
			Dictionary<int, Table_DungeonStage.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_DungeonStage.Row row = e.Current.Value;
				if (row.DungeonType == EDT.Dungeon.None || row.Stage <= 0)
				{
					continue;
				}

				_stageIndex[stageKey(row.DungeonType, row.Stage)] = row;

				int last;
				if (_lastStage.TryGetValue(row.DungeonType, out last) == false || row.Stage > last)
				{
					_lastStage[row.DungeonType] = row.Stage;
				}
			}

			_built = true;
			Debug.Log($"[DungeonProgress] 구축 완료 — 단계 {_stageIndex.Count}개 / 던전 {_lastStage.Count}종");
		}

		public static Table_DungeonStage.Row FindStageRow(EDT.Dungeon type, int stage)
		{
			Table_DungeonStage.Row row;
			_stageIndex.TryGetValue(stageKey(type, stage), out row);
			return row;
		}

		public static int GetLastStage(EDT.Dungeon type)
		{
			int last;
			_lastStage.TryGetValue(type, out last);
			return last;
		}

		// ── 입장 횟수 ─────────────────────────────────────────────────

		public static int GetUsedToday(EDT.Dungeon type)
		{
			return getOrCreate(type).usedToday;
		}

		// 현재 상한. 유저 데이터로 확장되며 Default 에서 시작해 Max 까지 늘어난다.
		public static int GetMaxCount(EDT.Dungeon type)
		{
			return getOrCreate(type).maxCount;
		}

		public static int GetRemainingCount(EDT.Dungeon type)
		{
			Entry entry = getOrCreate(type);
			int remaining = entry.maxCount - entry.usedToday;
			return remaining > 0 ? remaining : 0;
		}

		public static bool CanEnter(EDT.Dungeon type)
		{
			return GetRemainingCount(type) > 0;
		}

		// 입장 시 1회 소모. 클리어·실패·즉시 이탈을 가리지 않으며 환불도 없다 (맵 설계 6절).
		public static bool TryConsumeEnter(EDT.Dungeon type)
		{
			Entry entry = getOrCreate(type);
			if (entry.usedToday >= entry.maxCount)
			{
				return false;
			}

			entry.usedToday++;
			return true;
		}

		// 입장 횟수 상한 확장 — 계정 레벨·아이템·과금 등 각 시스템이 부여한다.
		public static void ExpandMaxCount(EDT.Dungeon type, int delta)
		{
			if (delta <= 0)
			{
				return;
			}

			Table_Dungeon.Row row = Table_Dungeon.Get(type);
			if (row == null)
			{
				return;
			}

			Entry entry = getOrCreate(type);
			entry.maxCount += delta;
			if (entry.maxCount > row.MaxEnterCount)
			{
				entry.maxCount = row.MaxEnterCount;
			}
		}

		// 매일 정해진 시각에 호출. 최고 클리어 단계는 초기화 대상이 아니다.
		public static void ResetDaily()
		{
			Dictionary<EDT.Dungeon, Entry>.Enumerator e = _byDungeon.GetEnumerator();
			while (e.MoveNext() == true)
			{
				e.Current.Value.usedToday = 0;
			}
		}

		// ── 단계 진행 ─────────────────────────────────────────────────

		public static int GetHighestStage(EDT.Dungeon type)
		{
			return getOrCreate(type).highestStage;
		}

		// Stage N 입장 가능 ⟺ 최고 클리어 단계 >= N - 1 (맵 설계 9장)
		public static bool IsStageUnlocked(EDT.Dungeon type, int stage)
		{
			if (stage <= 0)
			{
				return false;
			}

			return getOrCreate(type).highestStage >= stage - 1;
		}

		public static void MarkStageCleared(EDT.Dungeon type, int stage)
		{
			Entry entry = getOrCreate(type);
			if (stage > entry.highestStage)
			{
				entry.highestStage = stage;
			}
		}

		// 다음 단계가 존재하고 해금돼 있는가 — 결과창의 "다음 단계 도전" 버튼 판정.
		public static bool HasNextStage(EDT.Dungeon type, int currentStage)
		{
			return FindStageRow(type, currentStage + 1) != null;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static Entry getOrCreate(EDT.Dungeon type)
		{
			Entry entry;
			if (_byDungeon.TryGetValue(type, out entry) == true)
			{
				return entry;
			}

			entry = new Entry();

			// 상한의 시작값은 테이블의 기본 입장 횟수다.
			Table_Dungeon.Row row = Table_Dungeon.Get(type);
			entry.maxCount = (row != null) ? row.DefaultEnterCount : 0;

			_byDungeon[type] = entry;
			return entry;
		}

		private static long stageKey(EDT.Dungeon type, int stage)
		{
			return ((long)type << 32) | (uint)stage;
		}

		public static bool IsBuilt
		{
			get { return _built; }
		}
	}
}
