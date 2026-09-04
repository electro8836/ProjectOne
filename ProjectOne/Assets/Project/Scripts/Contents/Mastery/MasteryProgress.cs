using System.Collections.Generic;
using EDT;
using UnityEngine;

namespace ProjectOne.Mastery
{
	// 노드 투자가 막힌 이유. UI 가 "왜 안 되는지" 를 알려주려면 성패 bool 만으로는 부족하다.
	// 선언 순서가 곧 안내 우선순위다 — 해금 두 조건은 독립이라 동시에 걸릴 수 있다 (설계 6.1).
	public enum InvestBlock
	{
		None = 0,			// 투자 가능
		MaxLevel,			// 이미 만렙
		RequireTreePoint,	// 행 게이트 미달
		PrevNodeNotMax,		// 선행 노드가 만렙이 아님
		NoPoint				// 가용 포인트 없음
	}

	// 노드 회수가 막힌 이유. 막혔다고 버튼을 잠그는 대신 "트리 전체 초기화" 를 제안해야 해서,
	// 성패 bool 이 아니라 사유가 필요하다.
	public enum RefundBlock
	{
		None = 0,			// 그냥 1포인트 회수 가능
		NoLevel,			// 투자한 적이 없다 — 되돌릴 것이 없다
		NextNodeInvested,	// 후속 노드에 투자가 남아 있다
		RequireTreePoint	// 회수하면 다른 노드의 행 게이트가 깨진다
	}

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

		// 이 행보다 **위쪽** 행들에 투자한 포인트 합 — 행 게이트(RequireTreePoint)의 기준이다.
		// 트리 전체 투자량(TotalInvested)은 가용 포인트 계산용이라 축이 다르다.
		public int GetInvestedAboveRow(int row)
		{
			int sum = 0;
			Dictionary<int, int>.Enumerator e = _nodeLevels.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillTreeNode.Row invested = MasteryCatalog.GetNode(e.Current.Key);
				if (invested == null || invested.NodePos_Row >= row)
				{
					continue;
				}

				sum += e.Current.Value;
			}

			return sum;
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

		// 투자가 막힌 이유 — 투자 판정의 유일한 출처다. TryInvest 와 팝업이 모두 이 함수만 본다
		// (판정을 두 벌 두면 규칙이 바뀔 때 버튼과 실제 동작이 조용히 갈라진다).
		//
		// 해금은 두 조건이 **독립적으로** 작동한다 (설계 6.1).
		//   행 게이트   RequireTreePoint 이상을 **위쪽 행들에** 투자했는가
		//   개별 선행   PrevNodeID 가 만렙인가
		// 둘 다 비어 있으면 처음부터 열려 있다. 동시에 걸리면 행 게이트를 먼저 알린다 —
		// 포인트를 써야 선행도 찍을 수 있으므로 그쪽이 먼저 풀려야 한다.
		//
		// node 는 널이 아니어야 한다(호출자가 이미 조회한 행을 넘긴다).
		public InvestBlock GetInvestBlock(Table_SkillTreeNode.Row node, int achievementPoint)
		{
			if (GetNodeLevel(node.ID) >= node.MaxLevel)
			{
				return InvestBlock.MaxLevel;
			}

			if (node.RequireTreePoint > 0 && GetInvestedAboveRow(node.NodePos_Row) < node.RequireTreePoint)
			{
				return InvestBlock.RequireTreePoint;
			}

			if (node.PrevNodeID > 0)
			{
				Table_SkillTreeNode.Row prev = MasteryCatalog.GetNode(node.PrevNodeID);
				if (prev == null)
				{
					Debug.LogError($"[MasteryProgress] 선행 노드가 실존하지 않습니다 — node:{node.ID} prev:{node.PrevNodeID}");
					return InvestBlock.PrevNodeNotMax;
				}

				if (GetNodeLevel(prev.ID) < prev.MaxLevel)
				{
					return InvestBlock.PrevNodeNotMax;
				}
			}

			if (GetAvailablePoints(achievementPoint) <= 0)
			{
				return InvestBlock.NoPoint;
			}

			return InvestBlock.None;
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

			if (GetInvestBlock(node, achievementPoint) != InvestBlock.None)
			{
				return false;
			}

			_nodeLevels[nodeId] = GetNodeLevel(nodeId) + 1;
			return true;
		}

		// 회수가 막힌 이유 — 회수 판정의 유일한 출처다. 해금 조건(설계 6.1)의 역방향이며,
		// 되돌린 뒤에도 트리 전체가 유효해야 한다.
		//   1) 투자 레벨이 1 이상인가
		//   2) 이 노드를 선행으로 삼는 노드에 투자가 남아 있지 않은가
		//      — 레벨이 내려가면 그 후속 노드의 선행 조건(선행 만렙)이 깨진다
		//   3) 회수 후에도 투자된 모든 노드가 자기 행 게이트를 만족하는가
		//      — 게이트가 행 단위라 **회수한 행보다 아래 행** 노드만 영향을 받는다
		public RefundBlock GetRefundBlock(int nodeId)
		{
			if (GetNodeLevel(nodeId) <= 0)
			{
				return RefundBlock.NoLevel;
			}

			IReadOnlyList<Table_SkillTreeNode.Row> next = MasteryCatalog.GetNextNodes(nodeId);
			for (int i = 0; i < next.Count; i++)
			{
				if (GetNodeLevel(next[i].ID) > 0)
				{
					return RefundBlock.NextNodeInvested;
				}
			}

			Table_SkillTreeNode.Row target = MasteryCatalog.GetNode(nodeId);
			if (target == null)
			{
				Debug.LogError($"[MasteryProgress] 트리 노드가 없습니다: {nodeId}");
				return RefundBlock.NoLevel;
			}

			Dictionary<int, int>.Enumerator e = _nodeLevels.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillTreeNode.Row invested = MasteryCatalog.GetNode(e.Current.Key);
				if (invested == null || invested.RequireTreePoint <= 0)
				{
					continue;	// 테이블에서 사라진 노드거나 게이트가 없는 노드
				}

				int above = GetInvestedAboveRow(invested.NodePos_Row);
				if (target.NodePos_Row < invested.NodePos_Row)
				{
					above--;	// 이 회수가 그 노드의 게이트를 1 깎는다
				}

				if (above < invested.RequireTreePoint)
				{
					return RefundBlock.RequireTreePoint;
				}
			}

			return RefundBlock.None;
		}

		// 노드에서 1포인트 회수. 0 이 되면 키 자체를 지운다("투자한 노드만 키를 둔다" 불변식).
		public bool TryRefund(int nodeId)
		{
			if (GetRefundBlock(nodeId) != RefundBlock.None)
			{
				return false;
			}

			int level = GetNodeLevel(nodeId) - 1;
			if (level <= 0)
			{
				_nodeLevels.Remove(nodeId);
				return true;
			}

			_nodeLevels[nodeId] = level;
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
