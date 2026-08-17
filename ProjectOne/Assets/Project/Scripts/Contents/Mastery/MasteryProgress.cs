using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Mastery
{
	// 마스터리 1종의 진행도 (마스터리 설계 8.1).
	//
	// 레벨·가용 포인트·노드 해금 여부는 **저장하지 않고 계산한다** (설계 8.2).
	// 저장되는 것은 누적 경험치 · 지식의 서 사용 횟수 · 노드별 투자 레벨뿐이다.
	public sealed class MasteryProgress
	{
		public readonly WeaponMastery id;

		private int _totalExp;
		private int _itemPointUsed;

		// NodeID → 투자 레벨. 투자한 노드만 키를 둔다 (설계 8.2).
		private readonly Dictionary<int, int> _nodeLevels = new Dictionary<int, int>();

		public MasteryProgress(WeaponMastery id)
		{
			this.id = id;
		}

		public int TotalExp
		{
			get { return _totalExp; }
		}

		public int ItemPointUsed
		{
			get { return _itemPointUsed; }
		}

		// 누적 경험치에서 파생된다. 만렙 초과분은 버린다 (설계 5.1).
		public int Level
		{
			get { return MasteryCatalog.GetMasteryLevel(_totalExp); }
		}

		public IReadOnlyDictionary<int, int> NodeLevels
		{
			get { return _nodeLevels; }
		}

		// ── 경험치 ────────────────────────────────────────────────────

		public void AddExp(int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			_totalExp += amount;
		}

		// ── 스킬포인트 ────────────────────────────────────────────────

		// 이 트리에 투자한 누적 포인트. 행 게이트(RequireTreePoint) 판정의 기준이다.
		public int TotalInvested
		{
			get
			{
				int sum = 0;
				Dictionary<int, int>.Enumerator e = _nodeLevels.GetEnumerator();
				while (e.MoveNext() == true)
				{
					sum += e.Current.Value;
				}

				return sum;
			}
		}

		// 가용 포인트 (설계 7.1).
		//
		//   min(레벨, SkillPoint_Level.MaxPoint)   레벨분
		// + ItemPointUsed                          지식의 서
		// + AchievementPoint                       전역 정액 — 차감되지 않는다
		// - 이 트리 투자분
		public int GetAvailablePoints(int achievementPoint)
		{
			int levelCap = MasteryCatalog.GetMaxPoint(SkillPoint.SkillPoint_Level);
			int level = Level;
			int fromLevel = (levelCap > 0 && level > levelCap) ? levelCap : level;

			return fromLevel + _itemPointUsed + achievementPoint - TotalInvested;
		}

		// 지식의 서 사용 — 상한(SkillPoint_Item.MaxPoint)에 도달했으면 거부한다.
		// 아이템 소모는 호출자(소모품 시스템, STEP 11)가 담당한다.
		public bool TryUseItemPoint(int amount)
		{
			if (amount <= 0)
			{
				return false;
			}

			int cap = MasteryCatalog.GetMaxPoint(SkillPoint.SkillPoint_Item);
			if (cap > 0 && _itemPointUsed + amount > cap)
			{
				return false;
			}

			_itemPointUsed += amount;
			return true;
		}

		// ── 스킬 트리 ─────────────────────────────────────────────────

		public int GetNodeLevel(int nodeId)
		{
			int level;
			if (_nodeLevels.TryGetValue(nodeId, out level) == true)
			{
				return level;
			}

			return 0;
		}

		// 노드 해금 판정 — 두 조건이 **독립적으로** 작동한다 (설계 6.1).
		//   행 게이트   RequireTreePoint 이상을 이 트리에 투자했는가
		//   개별 선행   PrevNodeID 가 만렙인가
		// 둘 다 비어 있으면 처음부터 열려 있다.
		public bool IsNodeUnlocked(Table_SkillTreeNode.Row node)
		{
			if (node == null)
			{
				return false;
			}

			if (node.RequireTreePoint > 0 && TotalInvested < node.RequireTreePoint)
			{
				return false;
			}

			if (node.PrevNodeID > 0)
			{
				Table_SkillTreeNode.Row prev = MasteryCatalog.GetNode(node.PrevNodeID);
				if (prev == null)
				{
					Debug.LogError($"[MasteryProgress] 선행 노드가 실존하지 않습니다 — node:{node.ID} prev:{node.PrevNodeID}");
					return false;
				}

				if (GetNodeLevel(prev.ID) < prev.MaxLevel)
				{
					return false;
				}
			}

			return true;
		}

		// 노드에 1포인트 투자. 해금·만렙·가용 포인트를 전부 만족해야 성공한다.
		public bool TryInvest(int nodeId, int achievementPoint)
		{
			Table_SkillTreeNode.Row node = MasteryCatalog.GetNode(nodeId);
			if (node == null)
			{
				Debug.LogError($"[MasteryProgress] 트리 노드가 없습니다: {nodeId}");
				return false;
			}

			if (node.MaxLevel <= 0)
			{
				Debug.LogError($"[MasteryProgress] 투자할 수 없는 노드입니다(MaxLevel<=0): {nodeId}");
				return false;
			}

			if (GetNodeLevel(nodeId) >= node.MaxLevel)
			{
				return false;
			}

			if (IsNodeUnlocked(node) == false)
			{
				return false;
			}

			if (GetAvailablePoints(achievementPoint) <= 0)
			{
				return false;
			}

			_nodeLevels[nodeId] = GetNodeLevel(nodeId) + 1;
			return true;
		}

		// 트리 초기화 — 무료·무제한·마스터리별 독립 (설계 6.5).
		// 부분 초기화는 지원하지 않는다(선행 노드를 빼면 후속이 연쇄로 무효가 된다).
		public void ResetTree()
		{
			_nodeLevels.Clear();
		}

		// 노드 효과값 — 1레벨 기준이다 (설계 6.3). 장비의 Val 은 0레벨 기준이라 규칙이 반대다.
		public static float GetNodeValue(Table_SkillTreeNode.Row node, int level)
		{
			if (node == null || level <= 0)
			{
				return 0f;
			}

			return node.BaseValue + node.PerLevelValue * (level - 1);
		}

		// ── 직렬화 ────────────────────────────────────────────────────

		public void LoadFrom(Shared.MasteryProgressDto dto)
		{
			_nodeLevels.Clear();
			if (dto == null)
			{
				_totalExp = 0;
				_itemPointUsed = 0;
				return;
			}

			_totalExp = dto.totalExp;
			_itemPointUsed = dto.itemPointUsed;

			if (dto.nodeIds == null || dto.nodeLevels == null)
			{
				return;
			}

			int count = dto.nodeIds.Count < dto.nodeLevels.Count ? dto.nodeIds.Count : dto.nodeLevels.Count;
			for (int i = 0; i < count; i++)
			{
				if (dto.nodeLevels[i] <= 0)
				{
					continue;	// 0레벨 노드는 키를 두지 않는다
				}

				_nodeLevels[dto.nodeIds[i]] = dto.nodeLevels[i];
			}
		}

		public Shared.MasteryProgressDto ToDto()
		{
			Shared.MasteryProgressDto dto = new Shared.MasteryProgressDto();
			dto.masteryId = (int)id;
			dto.level = Level;
			dto.totalExp = _totalExp;
			dto.itemPointUsed = _itemPointUsed;

			Dictionary<int, int>.Enumerator e = _nodeLevels.GetEnumerator();
			while (e.MoveNext() == true)
			{
				dto.nodeIds.Add(e.Current.Key);
				dto.nodeLevels.Add(e.Current.Value);
			}

			return dto;
		}
	}
}
