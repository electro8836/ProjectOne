using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Monsters
{
	// 몬스터 정적 조회 캐시 + 데이터 정합성 검증.
	//
	// 설계의 핵심은 **원형(Monster)과 배치(MonsterSpawn)의 분리**다.
	// 해골전사는 1종이고, 어디에 몇 레벨로 나오는지는 스폰이 결정한다.
	//
	// Build() 가 설계 11장의 검증을 수행해 경고로 남긴다. 컨버터 이식(STEP 15) 전까지
	// 끊어진 참조를 잡아내는 유일한 장치이므로, 부팅 로그가 곧 엑셀 작업 지시서가 된다.
	//
	// StatCatalog / OptionCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	public static class MonsterCatalog
	{
		// 스폰 그룹 → 그 조합에 속한 행들. 같은 GroupID 로 여러 종이 섞인다.
		private static readonly Dictionary<int, List<Table_MonsterSpawn.Row>> _spawnsByGroup =
			new Dictionary<int, List<Table_MonsterSpawn.Row>>();

		// 스탯 그룹 → 그 그룹의 스탯 행들 (검증용. 실제 조립은 StatContainerFactory 가 한다)
		private static readonly Dictionary<int, List<Table_MonsterStat.Row>> _statsByGroup =
			new Dictionary<int, List<Table_MonsterStat.Row>>();

		// 몬스터 → 기본공격. 배열 순서가 아니라 SkillCategory == Normal 로 판정한다 (설계 3장).
		private static readonly Dictionary<int, EDT.Skill> _normalAttack = new Dictionary<int, EDT.Skill>();

		private static readonly List<Table_MonsterSpawn.Row> _emptySpawns = new List<Table_MonsterSpawn.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		public static void Build()
		{
			_spawnsByGroup.Clear();
			_statsByGroup.Clear();
			_normalAttack.Clear();

			buildStats();
			buildSpawns();
			buildNormalAttacks();

			_built = true;
			Debug.Log($"[MonsterCatalog] 구축 완료 — 몬스터:{Table_Monster.All().Count} AI:{Table_MonsterAI.All().Count} 스폰그룹:{_spawnsByGroup.Count} 스탯그룹:{_statsByGroup.Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static Table_Monster.Row GetMonster(int monsterId)
		{
			return Table_Monster.Get(monsterId);
		}

		// 몬스터의 AI 파라미터. AIGroupID 는 MonsterAI.ID 를 직접 가리킨다(1행 = 1그룹).
		// 지정되지 않았거나 행이 없으면 null — 호출자가 기본 동작으로 폴백한다.
		public static Table_MonsterAI.Row GetAI(int monsterId)
		{
			Table_Monster.Row monster = Table_Monster.Get(monsterId);
			if (monster == null || monster.AIGroupID <= 0)
			{
				return null;
			}

			return Table_MonsterAI.Get(monster.AIGroupID);
		}

		// 스폰 조합. 없으면 빈 목록(널 아님).
		public static IReadOnlyList<Table_MonsterSpawn.Row> GetSpawnGroup(int groupId)
		{
			List<Table_MonsterSpawn.Row> list;
			if (_spawnsByGroup.TryGetValue(groupId, out list) == true)
			{
				return list;
			}

			return _emptySpawns;
		}

		// 기본공격 스킬. 없으면 None.
		public static EDT.Skill GetNormalAttack(int monsterId)
		{
			EDT.Skill skill;
			if (_normalAttack.TryGetValue(monsterId, out skill) == true)
			{
				return skill;
			}

			return EDT.Skill.None;
		}

		// 처치 경험치 (설계 3장) — Stat_ExpBonus 는 지급 시점에 호출자가 곱한다.
		public static int GetKillExp(int monsterId, int level)
		{
			Table_Monster.Row row = Table_Monster.Get(monsterId);
			if (row == null)
			{
				return 0;
			}

			int lv = level > 0 ? level : 1;
			return row.BaseExp + row.PerLevelExp * (lv - 1);
		}

		public static MonsterType GetMonsterType(int monsterId)
		{
			Table_Monster.Row row = Table_Monster.Get(monsterId);
			return row != null ? row.MonsterType : MonsterType.None;
		}

		// ── 내부: 인덱싱 ──────────────────────────────────────────────

		private static void buildStats()
		{
			Dictionary<int, Table_MonsterStat.Row> all = Table_MonsterStat.All();
			Dictionary<int, Table_MonsterStat.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MonsterStat.Row row = e.Current.Value;
				if (row.GroupID <= 0)
				{
					continue;
				}

				List<Table_MonsterStat.Row> list;
				if (_statsByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_MonsterStat.Row>();
					_statsByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
			}
		}

		private static void buildSpawns()
		{
			Dictionary<int, Table_MonsterSpawn.Row> all = Table_MonsterSpawn.All();
			Dictionary<int, Table_MonsterSpawn.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MonsterSpawn.Row row = e.Current.Value;
				if (row.GroupID <= 0)
				{
					continue;
				}

				List<Table_MonsterSpawn.Row> list;
				if (_spawnsByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_MonsterSpawn.Row>();
					_spawnsByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
			}
		}

		private static void buildNormalAttacks()
		{
			Dictionary<int, Table_Monster.Row> all = Table_Monster.All();
			Dictionary<int, Table_Monster.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Monster.Row row = e.Current.Value;
				if (row.SkillIDs == null)
				{
					continue;
				}

				for (int i = 0; i < row.SkillIDs.Length; i++)
				{
					Table_Skill.Row skill = Table_Skill.Get(row.SkillIDs[i]);
					if (skill != null && skill.SkillCategory == SkillCategoryTypes.Normal)
					{
						_normalAttack[row.ID] = row.SkillIDs[i];
						break;
					}
				}
			}
		}

		// ── 내부: 정합성 검증 (설계 11장) ─────────────────────────────

		// 여기서 나오는 경고 목록이 곧 채워야 할 엑셀 작업 목록이다.
		// 컨버터가 이 검증을 넘겨받으면(STEP 15) 이 함수는 빌드 실패로 승격된다.
		private static void validate()
		{
			int issues = 0;

			Dictionary<int, Table_Monster.Row> monsters = Table_Monster.All();
			Dictionary<int, Table_Monster.Row>.Enumerator e = monsters.GetEnumerator();
			while (e.MoveNext() == true)
			{
				issues += validateMonster(e.Current.Value);
			}

			issues += validateStatGroups();
			issues += validateSpawns();

			if (issues > 0)
			{
				Debug.LogWarning($"[MonsterCatalog] 데이터 정합성 문제 {issues}건 — 위 경고 목록이 채워야 할 엑셀 작업입니다.");
			}
		}

		private static int validateMonster(Table_Monster.Row row)
		{
			int issues = 0;

			if (_statsByGroup.ContainsKey(row.StatGroupID) == false)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 StatGroupID={row.StatGroupID} 가 MonsterStat 에 없습니다 — 스탯이 전부 0이 됩니다.");
				issues++;
			}

			if (row.AIGroupID <= 0)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 에 AIGroupID 가 없습니다 — 기본 AI 로 동작합니다.");
				issues++;
			}
			else if (Table_MonsterAI.Get(row.AIGroupID) == null)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 AIGroupID={row.AIGroupID} 가 MonsterAI 에 없습니다.");
				issues++;
			}

			// 기본공격이 없으면 아무것도 못 한다 (설계 11장).
			int normalCount = 0;
			if (row.SkillIDs != null)
			{
				for (int i = 0; i < row.SkillIDs.Length; i++)
				{
					Table_Skill.Row skill = Table_Skill.Get(row.SkillIDs[i]);
					if (skill == null)
					{
						Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 SkillIDs[{i}]={row.SkillIDs[i]} 가 Skill 에 없습니다.");
						issues++;
						continue;
					}

					if (skill.SkillCategory == SkillCategoryTypes.Normal)
					{
						normalCount++;
					}
				}
			}

			if (normalCount != 1)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 기본공격(SkillCategory=Normal)이 {normalCount}개입니다 — 정확히 1개여야 합니다.");
				issues++;
			}

			return issues;
		}

		// 필수 스탯 4종이 없으면 주기 계산이 붕괴하거나 즉사한다 (설계 4장).
		private static readonly StatDetail[] _requiredStats = new StatDetail[]
		{
			StatDetail.StatDetail_Atk_Base,
			StatDetail.StatDetail_AtkSpeed_Base,
			StatDetail.StatDetail_MaxHp_Base,
			StatDetail.StatDetail_Def_Base
		};

		private static int validateStatGroups()
		{
			int issues = 0;

			Dictionary<int, List<Table_MonsterStat.Row>>.Enumerator e = _statsByGroup.GetEnumerator();
			while (e.MoveNext() == true)
			{
				int groupId = e.Current.Key;
				List<Table_MonsterStat.Row> rows = e.Current.Value;

				for (int i = 0; i < _requiredStats.Length; i++)
				{
					if (hasStat(rows, _requiredStats[i]) == false)
					{
						Debug.LogWarning($"[MonsterCatalog] 스탯그룹 {groupId} 에 필수 스탯 {_requiredStats[i]} 가 없습니다.");
						issues++;
					}
				}

				// 치명타 확률만 있고 피해량이 없으면 치명타가 1배로 터진다.
				if (hasStat(rows, StatDetail.StatDetail_CritRate_Base) == true &&
					hasStat(rows, StatDetail.StatDetail_CritDamage_Base) == false)
				{
					Debug.LogWarning($"[MonsterCatalog] 스탯그룹 {groupId} 에 치명타 확률만 있고 피해량이 없습니다.");
					issues++;
				}

				issues += warnDuplicateStats(groupId, rows);
			}

			return issues;
		}

		private static bool hasStat(List<Table_MonsterStat.Row> rows, StatDetail detail)
		{
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i].StatDetailID == detail)
				{
					return true;
				}
			}

			return false;
		}

		private static int warnDuplicateStats(int groupId, List<Table_MonsterStat.Row> rows)
		{
			int issues = 0;
			for (int i = 0; i < rows.Count; i++)
			{
				for (int j = i + 1; j < rows.Count; j++)
				{
					if (rows[i].StatDetailID == rows[j].StatDetailID)
					{
						Debug.LogWarning($"[MonsterCatalog] 스탯그룹 {groupId} 에 {rows[i].StatDetailID} 가 중복됩니다.");
						issues++;
					}
				}
			}

			return issues;
		}

		private static int validateSpawns()
		{
			int issues = 0;

			Dictionary<int, Table_MonsterSpawn.Row> all = Table_MonsterSpawn.All();
			Dictionary<int, Table_MonsterSpawn.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MonsterSpawn.Row row = e.Current.Value;
				if (Table_Monster.Get(row.MonsterID) == null)
				{
					Debug.LogWarning($"[MonsterCatalog] 스폰 {row.ID}(그룹 {row.GroupID}) 의 MonsterID={row.MonsterID} 가 Monster 에 없습니다.");
					issues++;
				}
			}

			return issues;
		}
	}
}
