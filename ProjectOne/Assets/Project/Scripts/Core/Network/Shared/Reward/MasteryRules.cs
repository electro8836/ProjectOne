using System.Collections.Generic;
using EDT;

namespace ProjectOne.Shared
{
	// 마스터리 규칙 중 테이블만 보고 판단하는 부분 (마스터리 설계 5·6·7장). 클라·서버 공용.
	//
	// 클라는 레벨 계산을 여기에 위임하고, 서버는 같은 규칙으로 저장 요청(트리 최종 상태)을 검증한다.
	// 클라의 투자/회수 판정(MasteryProgress.GetInvestBlock / GetRefundBlock)은 한 번의 조작을 막는 규칙이고,
	// ValidateTree 는 그 조작들의 **결과 상태**가 유효한지 보는 규칙이다 — 같은 불변식의 양면이다.
	public static class MasteryRules
	{
		// 레벨 곡선 — ID(레벨) 오름차순. 테이블 로드 후 처음 쓸 때 만든다.
		private static List<Table_MasteryLevelExp.Row> _curve;
		private static List<Table_CharacterLevelExp.Row> _characterCurve;

		// 누적 경험치로 도달 레벨을 구한다 (설계 3.3 — 누적값이라 이분 탐색 1회면 된다). 만렙 초과분은 버린다.
		public static int LevelFromExp(int totalExp)
		{
			List<Table_MasteryLevelExp.Row> curve = getCurve();
			if (curve.Count == 0)
			{
				return 1;
			}

			int lo = 0;
			int hi = curve.Count - 1;
			int result = curve[0].ID;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (curve[mid].TotalExperience <= totalExp)
				{
					result = curve[mid].ID;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}

			return result;
		}

		// 캐릭터 레벨 — 마스터리와 같은 규칙(누적 경험치 이상인 마지막 행)을 캐릭터 곡선에 적용한다.
		// 서버는 경험치를 바꿀 때 이 값을 LoadoutDto.level 에 함께 저장한다(클라는 exp 에서 계산하므로 읽지 않는다).
		public static int CharacterLevelFromExp(int totalExp)
		{
			List<Table_CharacterLevelExp.Row> curve = getCharacterCurve();
			if (curve.Count == 0)
			{
				return 1;
			}

			int lo = 0;
			int hi = curve.Count - 1;
			int result = curve[0].ID;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (curve[mid].TotalExperience <= totalExp)
				{
					result = curve[mid].ID;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}

			return result;
		}

		public static int GetMaxPoint(SkillPoint source)
		{
			Table_SkillPoint.Row row = Table_SkillPoint.Get(source);
			return row != null ? row.MaxPoint : 0;
		}

		// 가용 포인트 총량(투자분을 빼기 전) — min(레벨, SkillPoint_Level 상한) + 지식의 서 + 업적 (설계 7.1).
		public static int GetTotalPoints(int totalExp, int itemPointUsed, int achievementPoint)
		{
			int levelCap = GetMaxPoint(SkillPoint.SkillPoint_Level);
			int level = LevelFromExp(totalExp);
			int fromLevel = (levelCap > 0 && level > levelCap) ? levelCap : level;
			return fromLevel + itemPointUsed + achievementPoint;
		}

		// 트리 최종 상태 검증. 유효하면 null, 아니면 사유.
		public static string ValidateTree(WeaponMastery id, IReadOnlyList<int> nodeIds, IReadOnlyList<int> nodeLevels,
			int totalExp, int itemPointUsed, int achievementPoint)
		{
			Table_WeaponMastery.Row mastery = Table_WeaponMastery.Get(id);
			if (mastery == null || id == WeaponMastery.None)
			{
				return "unknown mastery " + (int)id;
			}

			if (nodeIds == null || nodeLevels == null || nodeIds.Count != nodeLevels.Count)
			{
				return "node arrays mismatch";
			}

			// 1. 노드 자체 — 존재·소속·레벨 범위·중복
			Dictionary<int, int> levels = new Dictionary<int, int>();
			int invested = 0;
			for (int i = 0; i < nodeIds.Count; i++)
			{
				Table_SkillTreeNode.Row node = Table_SkillTreeNode.Get(nodeIds[i]);
				if (node == null || node.GroupID != mastery.SkillTreeNodeGroupID)
				{
					return "node " + nodeIds[i] + " not in tree";
				}

				int level = nodeLevels[i];
				if (level < 1 || level > node.MaxLevel)
				{
					return "node " + node.ID + " level " + level + " out of range";
				}

				if (levels.ContainsKey(node.ID) == true)
				{
					return "duplicated node " + node.ID;
				}

				levels.Add(node.ID, level);
				invested += level;
			}

			// 2. 해금 조건 — 투자된 모든 노드가 행 게이트와 선행 만렙을 만족해야 한다 (설계 6.1).
			Dictionary<int, int>.Enumerator e = levels.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillTreeNode.Row node = Table_SkillTreeNode.Get(e.Current.Key);

				if (node.RequireTreePoint > 0 && investedAboveRow(levels, node.NodePos_Row) < node.RequireTreePoint)
				{
					return "node " + node.ID + " row gate not met";
				}

				if (node.PrevNodeID > 0)
				{
					Table_SkillTreeNode.Row prev = Table_SkillTreeNode.Get(node.PrevNodeID);
					int prevLevel;
					if (prev == null || levels.TryGetValue(prev.ID, out prevLevel) == false || prevLevel < prev.MaxLevel)
					{
						return "node " + node.ID + " prev node not maxed";
					}
				}
			}

			// 3. 포인트
			int total = GetTotalPoints(totalExp, itemPointUsed, achievementPoint);
			if (invested > total)
			{
				return "not enough points: " + invested + " > " + total;
			}

			return null;
		}

		// 서버 적립 — 해당 마스터리 진행도에 경험치를 더한다. 처음 든 무기면 항목을 만든다.
		public static void AddExp(MasteryDto dto, WeaponMastery id, int amount)
		{
			if (dto == null || id == WeaponMastery.None || amount <= 0)
			{
				return;
			}

			MasteryProgressDto progress = FindOrCreate(dto, id);
			progress.totalExp += amount;
			progress.level = LevelFromExp(progress.totalExp);
		}

		public static MasteryProgressDto FindOrCreate(MasteryDto dto, WeaponMastery id)
		{
			for (int i = 0; i < dto.masteries.Count; i++)
			{
				if (dto.masteries[i] != null && dto.masteries[i].masteryId == (int)id)
				{
					return dto.masteries[i];
				}
			}

			MasteryProgressDto created = new MasteryProgressDto();
			created.masteryId = (int)id;
			dto.masteries.Add(created);
			return created;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int investedAboveRow(Dictionary<int, int> levels, int row)
		{
			int sum = 0;
			Dictionary<int, int>.Enumerator e = levels.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillTreeNode.Row node = Table_SkillTreeNode.Get(e.Current.Key);
				if (node != null && node.NodePos_Row < row)
				{
					sum += e.Current.Value;
				}
			}

			return sum;
		}

		// 캐릭터 만렙 — 곡선의 마지막 레벨이 사실상의 상한이다.
		public static int CharacterMaxLevel
		{
			get
			{
				List<Table_CharacterLevelExp.Row> curve = getCharacterCurve();
				return curve.Count > 0 ? curve[curve.Count - 1].ID : 1;
			}
		}

		private static List<Table_MasteryLevelExp.Row> getCurve()
		{
			if (_curve != null && _curve.Count > 0)
			{
				return _curve;
			}

			List<Table_MasteryLevelExp.Row> curve = new List<Table_MasteryLevelExp.Row>(Table_MasteryLevelExp.All().Values);
			curve.Sort(compareLevel);
			_curve = curve;
			return _curve;
		}

		private static List<Table_CharacterLevelExp.Row> getCharacterCurve()
		{
			if (_characterCurve != null && _characterCurve.Count > 0)
			{
				return _characterCurve;
			}

			List<Table_CharacterLevelExp.Row> curve = new List<Table_CharacterLevelExp.Row>(Table_CharacterLevelExp.All().Values);
			curve.Sort(compareCharacterLevel);
			_characterCurve = curve;
			return _characterCurve;
		}

		private static int compareCharacterLevel(Table_CharacterLevelExp.Row a, Table_CharacterLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		private static int compareLevel(Table_MasteryLevelExp.Row a, Table_MasteryLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
