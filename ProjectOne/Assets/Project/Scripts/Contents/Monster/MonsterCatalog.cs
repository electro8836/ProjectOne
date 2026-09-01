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

		// 스킬세트 그룹 → 그 그룹의 스킬들(Priority 오름차순).
		// 일반·엘리트는 보유 목록이 곧 우선순위 목록이고, 보스는 보유(합집합)와 페이즈별 부분집합을
		// 같은 테이블의 다른 GroupID 로 갖는다.
		private static readonly Dictionary<int, List<EDT.Skill>> _skillSetByGroup =
			new Dictionary<int, List<EDT.Skill>>();

		// 보스 → 페이즈 행들(PhaseOrder 오름차순). 보스가 아닌 몬스터는 등록되지 않는다.
		private static readonly Dictionary<int, List<Table_BossMonsterPhase.Row>> _phasesByMonster =
			new Dictionary<int, List<Table_BossMonsterPhase.Row>>();

		private static readonly List<Table_MonsterSpawn.Row> _emptySpawns = new List<Table_MonsterSpawn.Row>();
		private static readonly List<EDT.Skill> _emptySkills = new List<EDT.Skill>();
		private static readonly List<Table_BossMonsterPhase.Row> _emptyPhases = new List<Table_BossMonsterPhase.Row>();

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
			_skillSetByGroup.Clear();
			_phasesByMonster.Clear();

			buildStats();
			buildSpawns();
			buildSkillSets();
			buildPhases();
			buildNormalAttacks();

			_built = true;
			Debug.Log($"[MonsterCatalog] 구축 완료 — 몬스터:{Table_Monster.All().Count} 스폰그룹:{_spawnsByGroup.Count} 스탯그룹:{_statsByGroup.Count}");

			validate();
		}

		// ── 조회 ──────────────────────────────────────────────────────

		public static Table_Monster.Row GetMonster(int monsterId)
		{
			return Table_Monster.Get(monsterId);
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

		// 스킬세트 그룹의 스킬들(Priority 오름차순). 없으면 빈 목록(널 아님).
		public static IReadOnlyList<EDT.Skill> GetSkillSet(int groupId)
		{
			List<EDT.Skill> list;
			if (_skillSetByGroup.TryGetValue(groupId, out list) == true)
			{
				return list;
			}

			return _emptySkills;
		}

		// 보스 페이즈 행들(PhaseOrder 오름차순). 없으면 빈 목록(널 아님).
		public static IReadOnlyList<Table_BossMonsterPhase.Row> GetBossPhases(int monsterId)
		{
			List<Table_BossMonsterPhase.Row> list;
			if (_phasesByMonster.TryGetValue(monsterId, out list) == true)
			{
				return list;
			}

			return _emptyPhases;
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

		// 스킬세트 — 행을 그룹으로 모은 뒤 Priority 오름차순으로 정렬한다.
		// 정렬 결과가 곧 SkillSelector 의 우선순위이므로 여기서 한 번만 정렬해 둔다.
		private static void buildSkillSets()
		{
			Dictionary<int, List<Table_MonsterSkillSet.Row>> rowsByGroup =
				new Dictionary<int, List<Table_MonsterSkillSet.Row>>();

			Dictionary<int, Table_MonsterSkillSet.Row> all = Table_MonsterSkillSet.All();
			Dictionary<int, Table_MonsterSkillSet.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MonsterSkillSet.Row row = e.Current.Value;
				if (row.GroupID <= 0 || row.SkillID == EDT.Skill.None)
				{
					continue;
				}

				List<Table_MonsterSkillSet.Row> list;
				if (rowsByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_MonsterSkillSet.Row>();
					rowsByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
			}

			Dictionary<int, List<Table_MonsterSkillSet.Row>>.Enumerator g = rowsByGroup.GetEnumerator();
			while (g.MoveNext() == true)
			{
				List<Table_MonsterSkillSet.Row> rows = g.Current.Value;
				sortByPriority(rows);

				List<EDT.Skill> skills = new List<EDT.Skill>(rows.Count);
				for (int i = 0; i < rows.Count; i++)
				{
					skills.Add(rows[i].SkillID);
				}

				_skillSetByGroup.Add(g.Current.Key, skills);
			}
		}

		// 보스 페이즈 — 몬스터별로 모아 PhaseOrder 오름차순 정렬.
		private static void buildPhases()
		{
			Dictionary<int, Table_BossMonsterPhase.Row> all = Table_BossMonsterPhase.All();
			Dictionary<int, Table_BossMonsterPhase.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_BossMonsterPhase.Row row = e.Current.Value;
				if (row.MonsterID <= 0)
				{
					continue;
				}

				List<Table_BossMonsterPhase.Row> list;
				if (_phasesByMonster.TryGetValue(row.MonsterID, out list) == false)
				{
					list = new List<Table_BossMonsterPhase.Row>();
					_phasesByMonster.Add(row.MonsterID, list);
				}

				list.Add(row);
			}

			Dictionary<int, List<Table_BossMonsterPhase.Row>>.Enumerator p = _phasesByMonster.GetEnumerator();
			while (p.MoveNext() == true)
			{
				sortByPhaseOrder(p.Current.Value);
			}
		}

		// 행 수가 한 자릿수라 삽입 정렬로 충분하다 — 람다 비교자를 만들지 않는다.
		private static void sortByPriority(List<Table_MonsterSkillSet.Row> rows)
		{
			for (int i = 1; i < rows.Count; i++)
			{
				Table_MonsterSkillSet.Row key = rows[i];
				int j = i - 1;
				while (j >= 0 && rows[j].Priority > key.Priority)
				{
					rows[j + 1] = rows[j];
					j--;
				}

				rows[j + 1] = key;
			}
		}

		private static void sortByPhaseOrder(List<Table_BossMonsterPhase.Row> rows)
		{
			for (int i = 1; i < rows.Count; i++)
			{
				Table_BossMonsterPhase.Row key = rows[i];
				int j = i - 1;
				while (j >= 0 && rows[j].PhaseOrder > key.PhaseOrder)
				{
					rows[j + 1] = rows[j];
					j--;
				}

				rows[j + 1] = key;
			}
		}

		private static void buildNormalAttacks()
		{
			Dictionary<int, Table_Monster.Row> all = Table_Monster.All();
			Dictionary<int, Table_Monster.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Monster.Row row = e.Current.Value;
				IReadOnlyList<EDT.Skill> set = GetSkillSet(row.SkillSetGroupID);
				for (int i = 0; i < set.Count; i++)
				{
					Table_Skill.Row skill = Table_Skill.Get(set[i]);
					if (skill != null && skill.SkillCategory == SkillCategoryTypes.Normal)
					{
						_normalAttack[row.ID] = set[i];
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
			issues += validateBossPhases();

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

			if (row.AIType == MonsterAIType.None)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 에 AIType 이 없습니다 — 기본 AI 로 동작합니다.");
				issues++;
			}

			// 보유 스킬은 MonsterSkillSet 그룹에서 온다. 그룹이 비면 스킬이 하나도 등록되지 않는다.
			IReadOnlyList<EDT.Skill> owned = GetSkillSet(row.SkillSetGroupID);
			if (owned.Count == 0)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 SkillSetGroupID={row.SkillSetGroupID} 가 MonsterSkillSet 에 없습니다 — 스킬이 하나도 등록되지 않습니다.");
				issues++;
			}

			// 기본공격이 없으면 아무것도 못 한다 (설계 11장).
			int normalCount = 0;
			for (int i = 0; i < owned.Count; i++)
			{
				Table_Skill.Row skill = Table_Skill.Get(owned[i]);
				if (skill == null)
				{
					Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 스킬세트 {row.SkillSetGroupID} 에 있는 {owned[i]} 가 Skill 에 없습니다.");
					issues++;
					continue;
				}

				if (skill.SkillCategory == SkillCategoryTypes.Normal)
				{
					normalCount++;
				}
			}

			if (normalCount != 1)
			{
				Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 의 기본공격(SkillCategory=Normal)이 {normalCount}개입니다 — 정확히 1개여야 합니다.");
				issues++;
			}

			return issues;
		}

		// 필수 스탯 5종이 없으면 주기 계산이 붕괴하거나 즉사하거나 제자리에 굳는다 (설계 4장).
		private static readonly StatDetail[] _requiredStats = new StatDetail[]
		{
			StatDetail.StatDetail_Atk_Base,
			StatDetail.StatDetail_AtkSpeed_Base,
			StatDetail.StatDetail_MaxHp_Base,
			StatDetail.StatDetail_Def_Base,
			StatDetail.StatDetail_MoveSpeed_Base
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

		// 보스 페이즈 검증.
		//
		// 가장 잘 나는 실수는 **페이즈 세트에만 있고 보유 세트에 없는 스킬**이다.
		// 등록은 Monster.SkillSetGroupID 로 한 번만 하므로, 보유에 없으면 TryCast 가
		// 거부해 그 페이즈가 통째로 먹통이 된다. 로그 없이 조용히 죽는 경로라 여기서 잡는다.
		private static int validateBossPhases()
		{
			int issues = 0;

			Dictionary<int, Table_Monster.Row> monsters = Table_Monster.All();
			Dictionary<int, Table_Monster.Row>.Enumerator e = monsters.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Monster.Row row = e.Current.Value;
				IReadOnlyList<Table_BossMonsterPhase.Row> phases = GetBossPhases(row.ID);

				if (row.MonsterType == MonsterType.Boss && phases.Count == 0)
				{
					Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 에 BossMonsterPhase 행이 없습니다 — 일반 몬스터처럼 동작합니다.");
					issues++;
					continue;
				}

				if (phases.Count > 0 && row.MonsterType != MonsterType.Boss)
				{
					Debug.LogWarning($"[MonsterCatalog] 몬스터 {row.ID}({row.Name}) 는 MonsterType 이 Boss 가 아닌데 페이즈 데이터가 있습니다 — 페이즈는 무시됩니다.");
					issues++;
				}

				IReadOnlyList<EDT.Skill> owned = GetSkillSet(row.SkillSetGroupID);
				float prevThreshold = float.MaxValue;

				for (int i = 0; i < phases.Count; i++)
				{
					Table_BossMonsterPhase.Row phase = phases[i];

					if (phase.HpThreshold > prevThreshold)
					{
						Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 의 HpThreshold={phase.HpThreshold} 가 이전 페이즈보다 큽니다 — PhaseOrder 순으로 내림차순이어야 합니다.");
						issues++;
					}

					prevThreshold = phase.HpThreshold;

					IReadOnlyList<EDT.Skill> set = GetSkillSet(phase.SkillSetGroupID);
					if (set.Count == 0)
					{
						Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 의 SkillSetGroupID={phase.SkillSetGroupID} 가 MonsterSkillSet 에 없습니다.");
						issues++;
					}

					for (int k = 0; k < set.Count; k++)
					{
						if (contains(owned, set[k]) == false)
						{
							Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 의 {set[k]} 가 보유 세트({row.SkillSetGroupID})에 없습니다 — 등록되지 않아 시전할 수 없습니다.");
							issues++;
						}
					}

					if (phase.PhaseSkillID == EDT.Skill.None)
					{
						continue;
					}

					if (contains(owned, phase.PhaseSkillID) == false)
					{
						Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 의 PhaseSkillID={phase.PhaseSkillID} 가 보유 세트({row.SkillSetGroupID})에 없습니다 — 전멸기를 시전할 수 없습니다.");
						issues++;
					}

					if (phase.GimmickCount <= 0)
					{
						Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 에 전멸기가 있는데 GimmickCount 가 0입니다 — 파훼할 수단이 없습니다.");
						issues++;
					}
					else if (phase.GimmickRequired > phase.GimmickCount)
					{
						Debug.LogWarning($"[MonsterCatalog] 보스 {row.ID}({row.Name}) 페이즈 {phase.PhaseOrder} 의 GimmickRequired={phase.GimmickRequired} 가 소환 개수 {phase.GimmickCount} 보다 큽니다 — 파훼가 불가능합니다.");
						issues++;
					}
				}
			}

			return issues;
		}

		private static bool contains(IReadOnlyList<EDT.Skill> list, EDT.Skill id)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == id)
				{
					return true;
				}
			}

			return false;
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
