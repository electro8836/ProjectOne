using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 던전 규칙 중 테이블만 보고 판단하는 부분 (맵 설계 6·9절). 클라·서버 공용.
	//
	// 클라 DungeonProgress 는 표시·판정을 여기에 위임하고, 서버는 같은 규칙으로 입장·정산 요청을 검증한다.
	// 런 시드로 굴리는 것(미궁 상자 보상)도 여기서 정해야 양쪽이 같은 결과를 낸다.
	public static class DungeonRules
	{
		// 균열 체크포인트 간격 — 1, 6, 11 … 에서 시작한다.
		public const int RiftCheckpointInterval = 5;

		// 균열 몬스터를 한 마리씩 내보내는 간격(초). 웨이브 최소 시간 = 마리 수 × 이 값(서버 핵 방지 검증).
		public const float RiftSpawnIntervalSeconds = 1f;

		// 단계 인덱스 — 테이블이 ID 키라 단계로 찾으려면 한 번 굽는다. 처음 쓸 때 만든다.
		private static Dictionary<int, Table_GoldDungeon.Row> _gold;
		private static Dictionary<int, Table_RiftDungeon.Row> _rift;
		private static Dictionary<int, Table_LabyrinthDungeon.Row> _labyrinth;
		private static Dictionary<int, Table_RuinsDungeon.Row> _ruins;
		private static int _riftLastWave;

		// ── 입장 ──────────────────────────────────────────────────────

		public static int GetDefaultEnterCount(EDT.Dungeon type)
		{
			Table_Dungeon.Row row = Table_Dungeon.Get(type);
			return (row != null) ? row.DefaultEnterCount : 0;
		}

		// Stage N 입장 가능 ⟺ 최고 클리어 단계 >= N - 1 (맵 설계 9장)
		public static bool IsStageUnlocked(int highestStage, int stage)
		{
			return stage > 0 && highestStage >= stage - 1;
		}

		// 던전에 그 단계(균열은 웨이브)가 있는가.
		public static bool HasStage(EDT.Dungeon type, int stage)
		{
			switch (type)
			{
				case EDT.Dungeon.Gold:
					return FindGoldStage(stage) != null;
				case EDT.Dungeon.Rift:
					return stage >= 1 && FindRiftWave(stage) != null;
				case EDT.Dungeon.Labyrinth:
					return FindLabyrinthStage(stage) != null;
				case EDT.Dungeon.Ruins:
					return FindRuinsStage(stage) != null;
			}

			return false;
		}

		// 그 진행도에서 입장할 수 있는 단계인가. 균열은 체크포인트에서만 시작한다.
		public static bool CanEnterStage(EDT.Dungeon type, int highestStage, int stage)
		{
			if (HasStage(type, stage) == false)
			{
				return false;
			}

			if (type == EDT.Dungeon.Rift)
			{
				return stage == GetRiftCheckpoint(highestStage);
			}

			return IsStageUnlocked(highestStage, stage);
		}

		// ── 단계 조회 ─────────────────────────────────────────────────

		public static Table_GoldDungeon.Row FindGoldStage(int stage)
		{
			ensureBuilt();
			Table_GoldDungeon.Row row;
			_gold.TryGetValue(stage, out row);
			return row;
		}

		public static Table_LabyrinthDungeon.Row FindLabyrinthStage(int stage)
		{
			ensureBuilt();
			Table_LabyrinthDungeon.Row row;
			_labyrinth.TryGetValue(stage, out row);
			return row;
		}

		public static Table_RuinsDungeon.Row FindRuinsStage(int stage)
		{
			ensureBuilt();
			Table_RuinsDungeon.Row row;
			_ruins.TryGetValue(stage, out row);
			return row;
		}

		// 마지막 행보다 높은 웨이브는 마지막 행을 반복한다 — 웨이브에는 끝이 없다.
		public static Table_RiftDungeon.Row FindRiftWave(int wave)
		{
			ensureBuilt();
			if (wave > _riftLastWave)
			{
				wave = _riftLastWave;
			}

			Table_RiftDungeon.Row row;
			_rift.TryGetValue(wave, out row);
			return row;
		}

		// ── 균열 ──────────────────────────────────────────────────────
		//
		// 균열은 highestStage 를 최고 기록 웨이브로 쓴다 — 종료 웨이브의 전 웨이브다.

		// 다음 도전의 시작 웨이브. 기록 17 → 16, 기록 4 → 1.
		public static int GetRiftCheckpoint(int highestWave)
		{
			return (highestWave / RiftCheckpointInterval) * RiftCheckpointInterval + 1;
		}

		// [fromWave, toWave] 구간 웨이브 보상의 합. 구간이 비면 0.
		public static int SumRiftWaveReward(int fromWave, int toWave)
		{
			int sum = 0;
			for (int wave = fromWave; wave <= toWave; wave++)
			{
				Table_RiftDungeon.Row row = FindRiftWave(wave);
				if (row != null)
				{
					sum += row.RewardCount;
				}
			}

			return sum;
		}

		// 한 판의 보상 — 입장보상(1 ~ 시작 웨이브) + 시작 웨이브 다음부터 통과한 웨이브.
		// 시작 웨이브는 입장보상에 들어 있으므로 웨이브 보상에서 뺀다 — 시작 웨이브에서 끝나도 입장보상은 받는다.
		public static int GetRiftRunReward(int startWave, int clearedWave)
		{
			return SumRiftWaveReward(1, startWave) + SumRiftWaveReward(startWave + 1, clearedWave);
		}

		// 소탕 보상 = 입장보상(1 ~ 체크포인트).
		public static int GetRiftSweepReward(int highestWave)
		{
			return SumRiftWaveReward(1, GetRiftCheckpoint(highestWave));
		}

		// 웨이브 하나를 끝내는 데 걸리는 최소 시간(초) — 마리 수만큼 간격을 두고 내보내므로 그보다 빨리 끝날 수 없다.
		public static float GetRiftWaveMinSeconds(int wave)
		{
			Table_RiftDungeon.Row row = FindRiftWave(wave);
			if (row == null)
			{
				return 0f;
			}

			int count = 0;
			for (int i = 0; i < row.MonsterSpawnGroupIDs.Length; i++)
			{
				count += CountSpawnGroup(row.MonsterSpawnGroupIDs[i]);
			}

			return count * RiftSpawnIntervalSeconds;
		}

		// 스폰 그룹을 펼친 총 마리 수 — 클라 SpawnGroupRunner 와 같은 규칙(MonsterID 없는 행 제외, Count 가 0 이하면 1마리).
		public static int CountSpawnGroup(int groupId)
		{
			if (groupId <= 0)
			{
				return 0;
			}

			int total = 0;
			Dictionary<int, Table_MonsterSpawn.Row>.Enumerator e = Table_MonsterSpawn.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MonsterSpawn.Row row = e.Current.Value;
				if (row.GroupID != groupId || row.MonsterID <= 0)
				{
					continue;
				}

				total += (row.Count > 0) ? row.Count : 1;
			}

			return total;
		}

		// 균열 보상 재화. 모든 행이 같은 재화다(클라 DungeonProgress.Build 에서 검증).
		public static EDT.Currency GetRiftRewardCurrency()
		{
			Table_RiftDungeon.Row row = FindRiftWave(1);
			return (row != null) ? row.RewardCurrency : EDT.Currency.None;
		}

		// ── 상자 ──────────────────────────────────────────────────────

		// 상자 1개의 추첨 시드 — 런 시드에서 상자마다 독립 시드를 파생한다(여는 순서와 무관하게 재현된다).
		public static ulong ChestSeed(long runSeed, int chestIndex, int groupId)
		{
			return KillSeed.Derive(runSeed, chestIndex, groupId);
		}

		// 미궁 상자 등급 → 이 단계의 등급별 보상 그룹. 등급이 없으면 일반으로 본다.
		public static int GetLabyrinthChestGroup(Table_LabyrinthDungeon.Row row, DungeonChestGrade grade)
		{
			switch (grade)
			{
				case DungeonChestGrade.Advanced:
					return row.ChestRewardGroupID_Advanced;
				case DungeonChestGrade.Premium:
					return row.ChestRewardGroupID_Premium;
				default:
					return row.ChestRewardGroupID_Normal;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void ensureBuilt()
		{
			if (_gold != null)
			{
				return;
			}

			Dictionary<int, Table_GoldDungeon.Row> gold = new Dictionary<int, Table_GoldDungeon.Row>();
			Dictionary<int, Table_GoldDungeon.Row>.Enumerator ge = Table_GoldDungeon.All().GetEnumerator();
			while (ge.MoveNext() == true)
			{
				if (ge.Current.Value.Stage > 0)
				{
					gold[ge.Current.Value.Stage] = ge.Current.Value;
				}
			}

			Dictionary<int, Table_RiftDungeon.Row> rift = new Dictionary<int, Table_RiftDungeon.Row>();
			int riftLast = 0;
			Dictionary<int, Table_RiftDungeon.Row>.Enumerator re = Table_RiftDungeon.All().GetEnumerator();
			while (re.MoveNext() == true)
			{
				Table_RiftDungeon.Row row = re.Current.Value;
				if (row.Wave > 0)
				{
					rift[row.Wave] = row;
					if (row.Wave > riftLast)
					{
						riftLast = row.Wave;
					}
				}
			}

			Dictionary<int, Table_LabyrinthDungeon.Row> labyrinth = new Dictionary<int, Table_LabyrinthDungeon.Row>();
			Dictionary<int, Table_LabyrinthDungeon.Row>.Enumerator le = Table_LabyrinthDungeon.All().GetEnumerator();
			while (le.MoveNext() == true)
			{
				if (le.Current.Value.Stage > 0)
				{
					labyrinth[le.Current.Value.Stage] = le.Current.Value;
				}
			}

			Dictionary<int, Table_RuinsDungeon.Row> ruins = new Dictionary<int, Table_RuinsDungeon.Row>();
			Dictionary<int, Table_RuinsDungeon.Row>.Enumerator ue = Table_RuinsDungeon.All().GetEnumerator();
			while (ue.MoveNext() == true)
			{
				if (ue.Current.Value.Stage > 0)
				{
					ruins[ue.Current.Value.Stage] = ue.Current.Value;
				}
			}

			_rift = rift;
			_riftLastWave = riftLast;
			_labyrinth = labyrinth;
			_ruins = ruins;
			_gold = gold;
		}
	}
}
