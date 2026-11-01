using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Shared;
using UnityEngine;
using ProjectOne.Resources;
using ProjectOne.Unit;

namespace ProjectOne.Mastery
{
	// 무기 마스터리 정적 조회 캐시.
	//
	// 런타임 조회 키가 테이블 기본 키와 다른 것들을 인덱싱한다.
	//   WeaponType     → WeaponMastery   무기를 끼면 이 역참조로 마스터리가 결정된다 (설계 1.5)
	//   WeaponMastery  → 스킬 8개        해금 레벨 오름차순 (설계 6.2)
	//   누적 경험치     → 레벨            MasteryLevelExp 는 누적값이라 이분 탐색이 가능하다
	//
	// StatCatalog / OptionCatalog 와 동일 패턴 — BootState 가 테이블 로드 직후 Build() 를 호출한다.
	// OptionCatalog 이후여야 한다(LevelBonusOption 해석에 의존).
	public static class MasteryCatalog
	{
		// WeaponType → 마스터리 행. 설계상 1:1 이며 Build 에서 중복을 검사한다 (설계 9장 No.2/3).
		private static readonly Dictionary<WeaponType, Table_WeaponMastery.Row> _byWeaponType =
			new Dictionary<WeaponType, Table_WeaponMastery.Row>();

		// WeaponMastery → 그 마스터리의 스킬. 해금 레벨 오름차순 — 해금 목록 표시 순서다.
		private static readonly Dictionary<WeaponMastery, List<Table_MasterySkill.Row>> _skillsByMastery =
			new Dictionary<WeaponMastery, List<Table_MasterySkill.Row>>();

		// Equipment.AnimControllerName → 무기별 애니메이터 오버라이드.
		// 무기 교체 경로(Loadout.reapplyHero)가 완전 동기라 부트에서 미리 잡아 두고 동기로 꺼내 쓴다.
		private static readonly Dictionary<string, RuntimeAnimatorController> _animControllers =
			new Dictionary<string, RuntimeAnimatorController>();

		// 레벨 오름차순 누적 경험치 — 이분 탐색용
		private static readonly List<Table_MasteryLevelExp.Row> _masteryExp = new List<Table_MasteryLevelExp.Row>();

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

		// 전 마스터리 레벨 합의 상한 — MasteryBook.TotalLevel 과 같은 범위(전 마스터리)를 센다.
		public static int TotalMasteryMaxLevel
		{
			get { return MasteryMaxLevel * Table_WeaponMastery.All().Count; }
		}

		public static void Build()
		{
			_byWeaponType.Clear();
			_skillsByMastery.Clear();
			_masteryExp.Clear();

			buildMasteries();
			checkWeaponAttacks();
			buildExpCurves();
			buildSkills();

			_built = true;
			Debug.Log($"[MasteryCatalog] 구축 완료 — 마스터리:{_byWeaponType.Count} 스킬마스터리:{_skillsByMastery.Count} 마스터리레벨:{_masteryExp.Count}");
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

		// 무기(Equipment)가 참조하는 AnimControllerName 을 전부 미리 로드한다.
		// Build() 이후에 호출해야 한다 — 주소 목록을 테이블에서 뽑는다.
		//
		// 해제하지 않는다. 세션 내내 필요한 자산이고 여러 무기가 같은 주소를 공유해 종류가 적다.
		public static async UniTask PreloadAnimControllersAsync(CancellationToken ct = default(CancellationToken))
		{
			_animControllers.Clear();

			Dictionary<int, Table_Equipment.Row> all = Table_Equipment.All();
			Dictionary<int, Table_Equipment.Row>.Enumerator e = all.GetEnumerator();
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

		// ── 스킬 해금 조회 ────────────────────────────────────────────

		// 그 마스터리의 스킬 전부(해금 레벨 오름차순). 데이터가 없으면 빈 목록(널 아님).
		public static IReadOnlyList<Table_MasterySkill.Row> GetSkills(WeaponMastery id)
		{
			List<Table_MasterySkill.Row> list;
			if (_skillsByMastery.TryGetValue(id, out list) == true)
			{
				return list;
			}

			return _emptySkills;
		}

		private static readonly List<Table_MasterySkill.Row> _emptySkills = new List<Table_MasterySkill.Row>();

		// 그 마스터리에서 스킬이 해금되는 레벨. 그 마스터리의 스킬이 아니면 0.
		public static int GetUnlockLevel(WeaponMastery id, EDT.Skill skill)
		{
			IReadOnlyList<Table_MasterySkill.Row> skills = GetSkills(id);
			for (int i = 0; i < skills.Count; i++)
			{
				if (skills[i].SkillID == skill)
				{
					return skills[i].UnlockLevel;
				}
			}

			return 0;
		}

		// ── 경험치 곡선 ───────────────────────────────────────────────

		// 누적 경험치로 도달 레벨을 구한다 (설계 3.3 — 누적값이라 이분 탐색 1회면 된다).
		// 서버가 같은 규칙으로 트리를 검증하므로 공유 규칙(MasteryRules)에 위임한다.
		public static int GetMasteryLevel(int totalExp)
		{
			return MasteryRules.LevelFromExp(totalExp);
		}

		// 해당 레벨에 도달하기 위한 누적 경험치. 테이블에 없으면 0.
		public static int GetMasteryTotalExp(int level)
		{
			Table_MasteryLevelExp.Row row = Table_MasteryLevelExp.Get(level);
			return row != null ? row.TotalExperience : 0;
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

				_byWeaponType[row.WeaponType] = row;
			}
		}

		// 평타·애니메이터는 무기 행이 갖는다 — 무기마다 평타 판정과 모션이 다를 수 있다.
		private static void checkWeaponAttacks()
		{
			Dictionary<int, Table_Equipment.Row> all = Table_Equipment.All();
			Dictionary<int, Table_Equipment.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_Equipment.Row row = e.Current.Value;
				if (row.WeaponType == WeaponType.None)
				{
					continue;
				}

				if (row.NormalAttackSkill == EDT.Skill.None)
				{
					Debug.LogWarning($"[MasteryCatalog] 기본공격이 없는 무기입니다: {row.ID}");
				}

				if (string.IsNullOrEmpty(row.AnimControllerName) == true)
				{
					Debug.LogWarning($"[MasteryCatalog] 애니메이터 오버라이드가 없는 무기입니다: {row.ID}");
				}
			}
		}

		// 마스터리별 스킬 목록 + 데이터 검증 (설계 9장 No.5~10).
		private static void buildSkills()
		{
			int maxLevel = MasteryMaxLevel;

			Dictionary<int, Table_MasterySkill.Row> all = Table_MasterySkill.All();
			Dictionary<int, Table_MasterySkill.Row>.Enumerator e = all.GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_MasterySkill.Row row = e.Current.Value;
				if (row.WeaponMastery == WeaponMastery.None || Table_WeaponMastery.Get(row.WeaponMastery) == null)
				{
					Debug.LogError($"[MasteryCatalog] MasterySkill {row.ID} 의 마스터리가 없습니다: {row.WeaponMastery}");
					continue;
				}

				Table_Skill.Row skill = Table_Skill.Get(row.SkillID);
				if (skill == null)
				{
					Debug.LogError($"[MasteryCatalog] MasterySkill {row.ID} 의 스킬이 없습니다: {row.SkillID}");
					continue;
				}

				if (skill.SkillCategory == SkillCategoryTypes.Normal)
				{
					Debug.LogError($"[MasteryCatalog] 평타는 MasterySkill 에 넣지 않습니다(슬롯 밖이다): {row.ID} {row.SkillID}");
					continue;
				}

				if (row.UnlockLevel < 1 || row.UnlockLevel > maxLevel)
				{
					Debug.LogError($"[MasteryCatalog] MasterySkill {row.ID} 의 해금 레벨 {row.UnlockLevel} 이 1~{maxLevel} 밖입니다 — 영영 해금되지 않습니다.");
					continue;
				}

				List<Table_MasterySkill.Row> list;
				if (_skillsByMastery.TryGetValue(row.WeaponMastery, out list) == false)
				{
					list = new List<Table_MasterySkill.Row>();
					_skillsByMastery.Add(row.WeaponMastery, list);
				}

				if (containsSkill(list, row.SkillID) == true)
				{
					Debug.LogError($"[MasteryCatalog] {row.WeaponMastery} 에 같은 스킬이 두 번 있습니다: {row.SkillID}");
					continue;
				}

				list.Add(row);
			}

			Dictionary<WeaponMastery, List<Table_MasterySkill.Row>>.Enumerator se = _skillsByMastery.GetEnumerator();
			while (se.MoveNext() == true)
			{
				List<Table_MasterySkill.Row> list = se.Current.Value;
				list.Sort(compareUnlock);

				if (list.Count > 0 && list[0].UnlockLevel != 1)
				{
					Debug.LogWarning($"[MasteryCatalog] {se.Current.Key} 에 1레벨 스킬이 없습니다 — 첫 무기 장착 시 슬롯이 빕니다.");
				}
			}
		}

		private static bool containsSkill(List<Table_MasterySkill.Row> list, EDT.Skill skill)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].SkillID == skill)
				{
					return true;
				}
			}

			return false;
		}

		private static int compareUnlock(Table_MasterySkill.Row a, Table_MasterySkill.Row b)
		{
			if (a.UnlockLevel != b.UnlockLevel)
			{
				return a.UnlockLevel.CompareTo(b.UnlockLevel);
			}

			return a.ID.CompareTo(b.ID);
		}

		private static void buildExpCurves()
		{
			_masteryExp.AddRange(Table_MasteryLevelExp.All().Values);
			_masteryExp.Sort(compareMasteryLevel);
		}

		private static int compareMasteryLevel(Table_MasteryLevelExp.Row a, Table_MasteryLevelExp.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
