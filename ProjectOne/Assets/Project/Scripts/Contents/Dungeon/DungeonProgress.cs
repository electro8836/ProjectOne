using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;
using ProjectOne.Utils;
using UnityEngine;

namespace ProjectOne.Dungeon
{
	// 던전 진행도 — 일일 입장 횟수 / 확장된 상한 / 최고 클리어 단계 (맵 설계 6절).
	//
	// 최고 클리어 단계는 일일 초기화 대상이 아니라 영구 누적이며,
	// **단계 해금과 Quest.DungeonClear 판정의 유일한 근거**다.
	//
	// 서버(USER_DUNGEON)가 소유한다 — 로그인 때 Apply, 입장·소탕·정산 응답마다 ApplyEntry 로 서버값을 반영한다.
	// 입장 차감과 일일 초기화 판정은 서버가 서버 시간으로 한다. 여기 usedToday 는 표시용 사본이다.
	// 미로그인(오프라인 테스트)에서는 TryConsumeEnter 로 로컬에서만 센다.
	public static class DungeonProgress
	{
		private sealed class Entry
		{
			public int usedToday;
			public int resetDay;		// usedToday 를 센 날 — 오늘이 아니면 0 으로 본다
			public int maxCount;		// DefaultEnterCount 에서 시작해 MaxEnterCount 까지 확장된다
			public int highestStage;	// 클리어한 최고 단계. 0이면 아직 하나도 못 깼다
		}

		// 맵 하나로 지목되는 던전 단계 — 개발용 이동 버튼이 쓴다.
		private struct MapTarget
		{
			public EDT.Dungeon type;
			public int stage;
		}

		private static readonly Dictionary<EDT.Dungeon, Entry> _byDungeon = new Dictionary<EDT.Dungeon, Entry>();

		// Stage → 골드던전 행. 테이블이 ID 키라 매번 순회하지 않도록 캐시한다.
		private static readonly Dictionary<int, Table_GoldDungeon.Row> _goldIndex = new Dictionary<int, Table_GoldDungeon.Row>();
		private static int _goldLastStage;

		// Wave → 균열던전 행. 마지막 행보다 높은 웨이브는 마지막 행을 반복한다.
		private static readonly Dictionary<int, Table_RiftDungeon.Row> _riftIndex = new Dictionary<int, Table_RiftDungeon.Row>();
		private static int _riftLastWave;

		// Stage → 미궁던전 행.
		private static readonly Dictionary<int, Table_LabyrinthDungeon.Row> _labyrinthIndex = new Dictionary<int, Table_LabyrinthDungeon.Row>();
		private static int _labyrinthLastStage;

		// Stage → 유적던전 행.
		private static readonly Dictionary<int, Table_RuinsDungeon.Row> _ruinsIndex = new Dictionary<int, Table_RuinsDungeon.Row>();
		private static int _ruinsLastStage;


		// MapID → 단계. 맵 하나로 던전 단계를 지목하는 경로(개발용 이동 버튼)가 쓴다.
		private static readonly Dictionary<int, MapTarget> _byMapId = new Dictionary<int, MapTarget>();

		private static bool _built;

		// 테이블 로드 이후 1회. StatCatalog / SkillParamCatalog 와 같은 패턴이다.
		public static void Build()
		{
			_goldIndex.Clear();
			_riftIndex.Clear();
			_labyrinthIndex.Clear();
			_ruinsIndex.Clear();
			_byMapId.Clear();
			_goldLastStage = 0;
			_riftLastWave = 0;
			_labyrinthLastStage = 0;
			_ruinsLastStage = 0;

			buildGold();
			buildRift();
			buildLabyrinth();
			buildRuins();

			_built = true;
			Debug.Log($"[DungeonProgress] 구축 완료 — 골드 {_goldIndex.Count}단계 / 균열 {_riftIndex.Count}웨이브 / 미궁 {_labyrinthIndex.Count}단계 / 유적 {_ruinsIndex.Count}단계");
		}

		private static void buildGold()
		{
			Dictionary<int, Table_GoldDungeon.Row>.Enumerator e = Table_GoldDungeon.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_GoldDungeon.Row row = e.Current.Value;
				if (row.Stage <= 0)
				{
					continue;
				}

				_goldIndex[row.Stage] = row;
				if (row.Stage > _goldLastStage)
				{
					_goldLastStage = row.Stage;
				}

				registerMapId(row.MapID, EDT.Dungeon.Gold, row.Stage);
			}
		}

		// 균열 보상은 재화 1종이다 — 행마다 다르면 표시·정산이 첫 행 기준이라 조용히 어긋나므로 알린다.
		private static void buildRift()
		{
			EDT.Currency currency = EDT.Currency.None;

			Dictionary<int, Table_RiftDungeon.Row>.Enumerator e = Table_RiftDungeon.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_RiftDungeon.Row row = e.Current.Value;
				if (row.Wave <= 0)
				{
					continue;
				}

				_riftIndex[row.Wave] = row;
				if (row.Wave > _riftLastWave)
				{
					_riftLastWave = row.Wave;
				}

				if (currency == EDT.Currency.None)
				{
					currency = row.RewardCurrency;
				}
				else if (row.RewardCurrency != currency)
				{
					Debug.LogError($"[DungeonProgress] RiftDungeon 보상 재화가 행마다 다릅니다 — Wave {row.Wave}: {row.RewardCurrency} (기준 {currency})");
				}

				registerMapId(row.MapID, EDT.Dungeon.Rift, row.Wave);
			}
		}

		private static void buildLabyrinth()
		{
			Dictionary<int, Table_LabyrinthDungeon.Row>.Enumerator e = Table_LabyrinthDungeon.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_LabyrinthDungeon.Row row = e.Current.Value;
				if (row.Stage <= 0)
				{
					continue;
				}

				_labyrinthIndex[row.Stage] = row;
				if (row.Stage > _labyrinthLastStage)
				{
					_labyrinthLastStage = row.Stage;
				}

				registerMapId(row.MapID, EDT.Dungeon.Labyrinth, row.Stage);
			}
		}

		private static void buildRuins()
		{
			Dictionary<int, Table_RuinsDungeon.Row>.Enumerator e = Table_RuinsDungeon.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_RuinsDungeon.Row row = e.Current.Value;
				if (row.Stage <= 0)
				{
					continue;
				}

				_ruinsIndex[row.Stage] = row;
				if (row.Stage > _ruinsLastStage)
				{
					_ruinsLastStage = row.Stage;
				}

				registerMapId(row.MapID, EDT.Dungeon.Ruins, row.Stage);
			}
		}

		public static Table_RuinsDungeon.Row FindRuinsStage(int stage)
		{
			Table_RuinsDungeon.Row row;
			_ruinsIndex.TryGetValue(stage, out row);
			return row;
		}

		public static Table_LabyrinthDungeon.Row FindLabyrinthStage(int stage)
		{
			Table_LabyrinthDungeon.Row row;
			_labyrinthIndex.TryGetValue(stage, out row);
			return row;
		}

		public static Table_GoldDungeon.Row FindGoldStage(int stage)
		{
			Table_GoldDungeon.Row row;
			_goldIndex.TryGetValue(stage, out row);
			return row;
		}

		// 마지막 행보다 높은 웨이브는 마지막 행을 반복한다 — 웨이브에는 끝이 없다.
		public static Table_RiftDungeon.Row FindRiftWave(int wave)
		{
			if (wave > _riftLastWave)
			{
				wave = _riftLastWave;
			}

			Table_RiftDungeon.Row row;
			_riftIndex.TryGetValue(wave, out row);
			return row;
		}

		// 던전 한 판이 쓰는 맵. 균열은 시작 웨이브의 맵을 한 판 내내 쓴다. 없으면 0.
		public static int GetMapId(EDT.Dungeon type, int stage)
		{
			switch (type)
			{
				case EDT.Dungeon.Gold:
				{
					Table_GoldDungeon.Row gold = FindGoldStage(stage);
					return (gold != null) ? gold.MapID : 0;
				}
				case EDT.Dungeon.Rift:
				{
					Table_RiftDungeon.Row rift = FindRiftWave(stage);
					return (rift != null) ? rift.MapID : 0;
				}
				case EDT.Dungeon.Labyrinth:
				{
					Table_LabyrinthDungeon.Row labyrinth = FindLabyrinthStage(stage);
					return (labyrinth != null) ? labyrinth.MapID : 0;
				}
				case EDT.Dungeon.Ruins:
				{
					Table_RuinsDungeon.Row ruins = FindRuinsStage(stage);
					return (ruins != null) ? ruins.MapID : 0;
				}
			}

			return 0;
		}

		// 단계형 던전(골드·미궁·유적)의 마지막 단계. 균열은 웨이브에 끝이 없어 0 이다.
		public static int GetLastStage(EDT.Dungeon type)
		{
			switch (type)
			{
				case EDT.Dungeon.Gold:
					return _goldLastStage;
				case EDT.Dungeon.Labyrinth:
					return _labyrinthLastStage;
				case EDT.Dungeon.Ruins:
					return _ruinsLastStage;
			}

			return 0;
		}

		// 맵으로 단계를 되찾는다. 없으면 false.
		public static bool TryFindStageByMapId(int mapId, out EDT.Dungeon type, out int stage)
		{
			MapTarget target;
			if (_byMapId.TryGetValue(mapId, out target) == false)
			{
				type = EDT.Dungeon.None;
				stage = 0;
				return false;
			}

			type = target.type;
			stage = target.stage;
			return true;
		}

		// 두 단계가 같은 맵을 쓰면 MapID 만으로는 단계를 특정할 수 없다 — 먼저 등록된 단계로 고정한다.
		// 균열은 모든 웨이브가 한 맵을 공유하는 게 정상이라 경고하지 않는다.
		private static void registerMapId(int mapId, EDT.Dungeon type, int stage)
		{
			if (mapId <= 0)
			{
				return;
			}

			MapTarget exist;
			if (_byMapId.TryGetValue(mapId, out exist) == true)
			{
				if (exist.type != type || type != EDT.Dungeon.Rift)
				{
					Debug.LogWarning($"[DungeonProgress] Map {mapId} 를 여러 단계가 공유합니다 — {exist.type} {exist.stage} 단계로 고정합니다 (무시: {type} {stage}).");
				}

				// 균열은 가장 낮은 웨이브(= 1웨이브)로 지목한다.
				if (exist.type == EDT.Dungeon.Rift && type == EDT.Dungeon.Rift && stage < exist.stage)
				{
					exist.stage = stage;
					_byMapId[mapId] = exist;
				}

				return;
			}

			MapTarget target;
			target.type = type;
			target.stage = stage;
			_byMapId[mapId] = target;
		}

		// ── 서버 반영 ─────────────────────────────────────────────────

		// 로그인 스냅샷 — 서버 진행도로 통째로 덮는다.
		public static void Apply(DungeonProgressDto dto)
		{
			_byDungeon.Clear();
			if (dto == null || dto.entries == null)
			{
				return;
			}

			for (int i = 0; i < dto.entries.Count; i++)
			{
				ApplyEntry(dto.entries[i]);
			}
		}

		// 입장·소탕·정산 응답의 진행도 하나를 반영한다.
		public static void ApplyEntry(DungeonEntryDto dto)
		{
			if (dto == null)
			{
				return;
			}

			Entry entry = getOrCreate((EDT.Dungeon)dto.dungeonType);
			entry.highestStage = dto.highestStage;
			entry.usedToday = dto.usedToday;
			entry.resetDay = dto.resetDay;
		}

		// ── 입장 횟수 ─────────────────────────────────────────────────

		public static int GetUsedToday(EDT.Dungeon type)
		{
			Entry entry = getOrCreate(type);
			return (entry.resetDay == DailyReset.GetResetDay()) ? entry.usedToday : 0;
		}

		// 현재 상한. 유저 데이터로 확장되며 Default 에서 시작해 Max 까지 늘어난다.
		public static int GetMaxCount(EDT.Dungeon type)
		{
			return getOrCreate(type).maxCount;
		}

		public static int GetRemainingCount(EDT.Dungeon type)
		{
			int remaining = getOrCreate(type).maxCount - GetUsedToday(type);
			return remaining > 0 ? remaining : 0;
		}

		public static bool CanEnter(EDT.Dungeon type)
		{
			return GetRemainingCount(type) > 0;
		}

		// 입장 시 1회 소모. 클리어·실패·즉시 이탈을 가리지 않으며 환불도 없다 (맵 설계 6절).
		// 미로그인 전용 — 로그인 중에는 서버(DungeonEnter)가 차감하고 ApplyEntry 로 돌려준다.
		public static bool TryConsumeEnter(EDT.Dungeon type)
		{
			if (GetRemainingCount(type) <= 0)
			{
				return false;
			}

			Entry entry = getOrCreate(type);
			entry.usedToday = GetUsedToday(type) + 1;
			entry.resetDay = DailyReset.GetResetDay();
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
			return DungeonRules.IsStageUnlocked(getOrCreate(type).highestStage, stage);
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
		// 균열은 단계형이 아니라 다음 단계가 없다.
		public static bool HasNextStage(EDT.Dungeon type, int currentStage)
		{
			switch (type)
			{
				case EDT.Dungeon.Gold:
					return FindGoldStage(currentStage + 1) != null;
				case EDT.Dungeon.Labyrinth:
					return FindLabyrinthStage(currentStage + 1) != null;
				case EDT.Dungeon.Ruins:
					return FindRuinsStage(currentStage + 1) != null;
			}

			return false;
		}

		// ── 균열 ──────────────────────────────────────────────────────
		//
		// 균열은 highestStage 를 최고 기록 웨이브로 쓴다 — 종료 웨이브의 전 웨이브다.

		// 다음 도전의 시작 웨이브. 기록 17 → 16, 기록 4 → 1.
		public static int GetRiftCheckpoint()
		{
			return DungeonRules.GetRiftCheckpoint(GetHighestStage(EDT.Dungeon.Rift));
		}

		// 웨이브 1개를 넘겼을 때의 보상 수량
		public static int GetRiftWaveReward(int wave)
		{
			Table_RiftDungeon.Row row = FindRiftWave(wave);
			return (row != null) ? row.RewardCount : 0;
		}

		// [fromWave, toWave] 구간 웨이브 보상의 합. 구간이 비면 0. 서버 정산과 같은 규칙(DungeonRules)이다.
		public static int SumRiftWaveReward(int fromWave, int toWave)
		{
			return DungeonRules.SumRiftWaveReward(fromWave, toWave);
		}

		// 입장보상 — 1웨이브부터 체크포인트 웨이브까지(포함)의 보상 합이다. 소탕 보상도 이 값이다.
		public static int GetRiftEntryReward()
		{
			return DungeonRules.GetRiftSweepReward(GetHighestStage(EDT.Dungeon.Rift));
		}

		// 균열 보상 재화. 모든 행이 같은 재화다(Build 에서 검증).
		public static EDT.Currency GetRiftRewardCurrency()
		{
			return DungeonRules.GetRiftRewardCurrency();
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

		public static bool IsBuilt
		{
			get { return _built; }
		}
	}
}
