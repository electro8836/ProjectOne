using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using UnityEngine;
using ProjectOne.Resources;

namespace ProjectOne.Mastery
{
	// 무기 마스터리 정적 조회 캐시.
	//
	// 런타임 조회 키가 테이블 기본 키와 다른 것들을 인덱싱한다.
	//   WeaponType   → WeaponMastery   무기를 끼면 이 역참조로 마스터리가 결정된다 (설계 1.5)
	//   GroupID      → 트리 노드 목록   보드 하나를 통째로 순회해야 한다
	//   PrevNodeID   → 후속 노드 목록   선행 노드 해금 판정의 역방향
	//   누적 경험치   → 레벨            MasteryLevelExp 는 누적값이라 이분 탐색이 가능하다
	//
	// StatCatalog / OptionCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	// OptionCatalog 이후여야 한다(LevelBonusOption 해석에 의존).
	public static class MasteryCatalog
	{
		// WeaponType → 마스터리 행. 설계상 1:1 이며 Build 에서 중복을 검사한다 (설계 9장 No.2/3).
		private static readonly Dictionary<WeaponType, Table_WeaponMastery.Row> _byWeaponType =
			new Dictionary<WeaponType, Table_WeaponMastery.Row>();

		// SkillTreeNodeGroupID → 그 보드의 노드 전부
		private static readonly Dictionary<int, List<Table_SkillTreeNode.Row>> _nodesByGroup =
			new Dictionary<int, List<Table_SkillTreeNode.Row>>();

		// NodeID → 노드 (PrevNodeID 추적용)
		private static readonly Dictionary<int, Table_SkillTreeNode.Row> _nodeById =
			new Dictionary<int, Table_SkillTreeNode.Row>();

		// PrevNodeID → 그 노드를 선행으로 삼는 후속 노드들. 포인트 회수 가능 판정의 역방향 조회다.
		private static readonly Dictionary<int, List<Table_SkillTreeNode.Row>> _nextNodes =
			new Dictionary<int, List<Table_SkillTreeNode.Row>>();

		// AnimControllerName → 무기별 애니메이터 오버라이드.
		// 무기 교체 경로(Loadout.reapplyHero)가 완전 동기라 부트에서 미리 잡아 두고 동기로 꺼내 쓴다.
		private static readonly Dictionary<string, RuntimeAnimatorController> _animControllers =
			new Dictionary<string, RuntimeAnimatorController>();

		// 레벨 오름차순 누적 경험치 — 이분 탐색용
		private static readonly List<Table_MasteryLevelExp.Row> _masteryExp = new List<Table_MasteryLevelExp.Row>();
		private static readonly List<Table_CharacterLevelExp.Row> _characterExp = new List<Table_CharacterLevelExp.Row>();

		private static bool _built;

		public static bool IsBuilt
		{
			get { return _built; }
		}

		// 마스터리 만렙 — 테이블의 마지막 레벨이 사실상의 상한이다 (설계 5.1).
		public static int MasteryMaxLevel
		{
			get { return _masteryExp.Count > 0 ? _masteryExp[_masteryExp.Count - 1].ID : 1; }
		}

		public static void Build()
		{
			_byWeaponType.Clear();
			_nodesByGroup.Clear();
			_nodeById.Clear();
			_nextNodes.Clear();
			_masteryExp.Clear();
			_characterExp.Clear();

			buildMasteries();
			buildNodes();
			buildExpCurves();

			_built = true;
			Debug.Log($"[MasteryCatalog] 구축 완료 — 마스터리:{_byWeaponType.Count} 트리그룹:{_nodesByGroup.Count} 노드:{_nodeById.Count} 마스터리레벨:{_masteryExp.Count}");
		}

		// ── 마스터리 조회 ─────────────────────────────────────────────

		// 장착 무기의 종류로 마스터리를 역참조한다. 무기 미착용(None)이면 null.
		public static Table_WeaponMastery.Row GetByWeaponType(WeaponType weaponType)
		{
			if (weaponType == WeaponType.None)
			{
				return null;
			}

			Table_WeaponMastery.Row row;
			_byWeaponType.TryGetValue(weaponType, out row);
			return row;
		}

		public static Table_WeaponMastery.Row Get(WeaponMastery id)
		{
			return Table_WeaponMastery.Get(id);
		}

		public static Dictionary<WeaponMastery, Table_WeaponMastery.Row> All()
		{
			return Table_WeaponMastery.All();
		}

		// ── 애니메이터 오버라이드 ─────────────────────────────────────

		// 마스터리 테이블이 참조하는 AnimControllerName 을 전부 미리 로드한다.
		// Build() 이후에 호출해야 한다 — 주소 목록을 테이블에서 뽑는다.
		//
		// 해제하지 않는다. 세션 내내 필요한 자산이고 종류도 무기 계열 수만큼뿐이다.
		public static async UniTask PreloadAnimControllersAsync(CancellationToken ct = default(CancellationToken))
		{
			_animControllers.Clear();

			Dictionary<WeaponMastery, Table_WeaponMastery.Row> all = Table_WeaponMastery.All();
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				string address = e.Current.Value.AnimControllerName;
				if (string.IsNullOrEmpty(address) == true || _animControllers.ContainsKey(address) == true)
				{
					continue;
				}

				// 구상 타입으로 요청한다 — Addressables 의 타입 필터가 베이스 타입에서 미스할 여지를 없앤다.
				AnimatorOverrideController controller = await ResourceManager.Instance.AcquireAsync<AnimatorOverrideController>(address, ct);
				if (controller == null)
				{
					// 조용히 넘기면 무기를 들어도 모션만 안 나오는 무음 실패가 된다.
					Debug.LogError($"[MasteryCatalog] 애니메이터 오버라이드 로드 실패 — address:{address}");
					continue;
				}

				_animControllers.Add(address, controller);
			}

			Debug.Log($"[MasteryCatalog] 애니메이터 오버라이드 프리로드 완료 — {_animControllers.Count}종");
		}

		// 프리로드된 오버라이드를 동기로 꺼낸다. 이름이 비어 있으면 null (무기 미착용 등 정상 경로).
		public static RuntimeAnimatorController GetAnimController(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return null;
			}

			RuntimeAnimatorController controller;
			if (_animControllers.TryGetValue(address, out controller) == false)
			{
				Debug.LogError($"[MasteryCatalog] 프리로드되지 않은 애니메이터 오버라이드 — address:{address}");
				return null;
			}

			return controller;
		}

		// ── 스킬 트리 조회 ────────────────────────────────────────────

		// 보드 하나의 노드 전부. 데이터가 없으면 빈 목록(널 아님).
		public static IReadOnlyList<Table_SkillTreeNode.Row> GetNodes(int groupId)
		{
			List<Table_SkillTreeNode.Row> list;
			if (_nodesByGroup.TryGetValue(groupId, out list) == true)
			{
				return list;
			}

			return _emptyNodes;
		}

		private static readonly List<Table_SkillTreeNode.Row> _emptyNodes = new List<Table_SkillTreeNode.Row>();

		public static Table_SkillTreeNode.Row GetNode(int nodeId)
		{
			Table_SkillTreeNode.Row row;
			_nodeById.TryGetValue(nodeId, out row);
			return row;
		}

		// 이 노드를 선행으로 삼는 노드 전부. 없으면 빈 목록(널 아님).
		public static IReadOnlyList<Table_SkillTreeNode.Row> GetNextNodes(int prevNodeId)
		{
			List<Table_SkillTreeNode.Row> list;
			if (_nextNodes.TryGetValue(prevNodeId, out list) == true)
			{
				return list;
			}

			return _emptyNodes;
		}

		// ── 경험치 곡선 ───────────────────────────────────────────────

		// 누적 경험치로 도달 레벨을 구한다 (설계 3.3 — 누적값이라 이분 탐색 1회면 된다).
		public static int GetMasteryLevel(int totalExp)
		{
			return findLevel(_masteryExp, totalExp);
		}

		public static int GetCharacterLevel(int totalExp)
		{
			return findLevelCharacter(_characterExp, totalExp);
		}

		// 해당 레벨에 도달하기 위한 누적 경험치. 테이블에 없으면 0.
		public static int GetMasteryTotalExp(int level)
		{
			Table_MasteryLevelExp.Row row = Table_MasteryLevelExp.Get(level);
			return row != null ? row.TotalExperience : 0;
		}

		// ── 스킬포인트 상한 ───────────────────────────────────────────

		// 해당 출처로 얻을 수 있는 최대 포인트. 행이 없으면 0(획득 불가).
		public static int GetMaxPoint(SkillPoint source)
		{
			Table_SkillPoint.Row row = Table_SkillPoint.Get(source);
			return row != null ? row.MaxPoint : 0;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static void buildMasteries()
		{
			Dictionary<WeaponMastery, Table_WeaponMastery.Row> all = Table_WeaponMastery.All();
			Dictionary<WeaponMastery, Table_WeaponMastery.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_WeaponMastery.Row row = e.Current.Value;
				if (row.ID == WeaponMastery.None)
				{
					continue;
				}

				if (row.WeaponType == WeaponType.None)
				{
					Debug.LogError($"[MasteryCatalog] 마스터리 {row.ID} 에 WeaponType 이 없습니다.");
					continue;
				}

				// WeaponType 하나당 마스터리 하나여야 조회가 유일하게 결정된다 (설계 2.2).
				if (_byWeaponType.ContainsKey(row.WeaponType) == true)
				{
					Debug.LogError($"[MasteryCatalog] WeaponType {row.WeaponType} 를 쓰는 마스터리가 둘 이상입니다: {row.ID}");
					continue;
				}

				if (row.NormalAttackSkill == EDT.Skill.None)
				{
					Debug.LogWarning($"[MasteryCatalog] 기본공격이 없는 마스터리입니다: {row.ID}");
				}

				_byWeaponType[row.WeaponType] = row;
			}
		}

		private static void buildNodes()
		{
			Dictionary<int, Table_SkillTreeNode.Row> all = Table_SkillTreeNode.All();
			Dictionary<int, Table_SkillTreeNode.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_SkillTreeNode.Row row = e.Current.Value;
				if (row.GroupID <= 0)
				{
					Debug.LogWarning($"[MasteryCatalog] 소속 보드가 없는 트리 노드입니다: {row.ID}");
					continue;
				}

				List<Table_SkillTreeNode.Row> list;
				if (_nodesByGroup.TryGetValue(row.GroupID, out list) == false)
				{
					list = new List<Table_SkillTreeNode.Row>();
					_nodesByGroup.Add(row.GroupID, list);
				}

				list.Add(row);
				_nodeById[row.ID] = row;

				if (row.PrevNodeID > 0)
				{
					List<Table_SkillTreeNode.Row> next;
					if (_nextNodes.TryGetValue(row.PrevNodeID, out next) == false)
					{
						next = new List<Table_SkillTreeNode.Row>();
						_nextNodes.Add(row.PrevNodeID, next);
					}

					next.Add(row);
				}
			}

			// 보드 순회 순서를 좌표로 고정한다 — UI 배치와 누적 계산의 재현성 확보.
			Dictionary<int, List<Table_SkillTreeNode.Row>>.Enumerator ge = _nodesByGroup.GetEnumerator();
			while (ge.MoveNext() == true)
			{
				ge.Current.Value.Sort(compareNodePos);
			}
		}

		private static int compareNodePos(Table_SkillTreeNode.Row a, Table_SkillTreeNode.Row b)
		{
			if (a.NodePos_Row != b.NodePos_Row)
			{
				return a.NodePos_Row.CompareTo(b.NodePos_Row);
			}

			if (a.NodePos_Column != b.NodePos_Column)
			{
				return a.NodePos_Column.CompareTo(b.NodePos_Column);
			}

			return a.ID.CompareTo(b.ID);
		}

		private static void buildExpCurves()
		{
			_masteryExp.AddRange(Table_MasteryLevelExp.All().Values);
			_masteryExp.Sort(compareMasteryLevel);

			_characterExp.AddRange(Table_CharacterLevelExp.All().Values);
			_characterExp.Sort(compareCharacterLevel);
		}

		private static int compareMasteryLevel(Table_MasteryLevelExp.Row a, Table_MasteryLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		private static int compareCharacterLevel(Table_CharacterLevelExp.Row a, Table_CharacterLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}

		// 누적 경험치 이상인 마지막 행의 레벨. 테이블이 비면 1.
		private static int findLevel(List<Table_MasteryLevelExp.Row> curve, int totalExp)
		{
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

		// 제네릭으로 묶으면 인터페이스 제약이 필요해 자동생성 테이블에 손을 대야 한다. 두 벌로 둔다.
		private static int findLevelCharacter(List<Table_CharacterLevelExp.Row> curve, int totalExp)
		{
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
	}
}
