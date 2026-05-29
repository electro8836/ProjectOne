using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Resources;
using ProjectOne.Event;
using ProjectOne.Unit.Stats;
using ProjectOne.Skill;
using ProjectOne.Buff;

namespace ProjectOne.Unit
{
	// ID 만으로 완성된 유닛(GameObject + 모든 컴포지션 + Aspect 적용)을 스폰
	// - CreateAsync: 타입 디스패치 통합 진입점
	// - CreateHeroAsync / CreateMonsterAsync: 구체 타입 반환이 필요할 때 사용
	// - Singleton<T> 컨벤션 (EventManager / ResourceManager 와 동일)
	public sealed class UnitFactory : Singleton<UnitFactory>
	{
		const string SourceBase = "Base";
		const float DefaultMass = 1f;

		// 1부터 시작 (0 = 미할당). 인스턴스마다 1회 후증가. 메인 스레드 단일 호출 가정 → lock 불필요.
		static int _nextInstanceId = 1;

		// 외부 디버그/로그용 — 현재 카운터 상태
		public static int PeekNextInstanceId
		{
			get { return _nextInstanceId; }
		}

		private UnitFactory()
		{
		}

		public async UniTask<UnitBase> CreateAsync(UnitType type, int id, Vector3 pos, Faction faction = Faction.None, CancellationToken ct = default)
		{
			switch (type)
			{
				case UnitType.Hero:
					return await CreateHeroAsync(id, pos, faction == Faction.None ? Faction.Player : faction, ct);
				case UnitType.Monster:
					return await CreateMonsterAsync(id, pos, faction == Faction.None ? Faction.Enemy : faction, ct);
				default:
					Debug.LogError($"[UnitFactory] 지원하지 않는 UnitType: {type}");
					return null;
			}
		}

		public async UniTask<Hero> CreateHeroAsync(int characterId, Vector3 pos, Faction faction = Faction.Player, CancellationToken ct = default)
		{
			Table_Character.Row row = Table_Character.Get(characterId);
			if (row == null)
			{
				Debug.LogError($"[UnitFactory] Table_Character.Get({characterId}) == null");
				return null;
			}

			string skinAddress = string.Empty;
			if (row.SkinID > 0)
			{
				Table_SkinInfo.Row skin = Table_SkinInfo.Get(row.SkinID);
				if (skin != null)
				{
					skinAddress = skin.Path;
				}
			}

			Hero hero = await SpawnAndComposeAsync<Hero>(UnitType.Hero, row.Path, row.Name, characterId, row.BaseStatID, row.SkillSetID, skinAddress, row.Radius, pos, faction, ct);
			if (hero == null)
			{
				return null;
			}

			// Hero 전용 후처리 — 강화/업적/콜렉션/장비/스킬/버프 Aspect
			HeroAspectRegistry.Instance.ApplyAll(hero);
			hero.RefreshAnimationStats();

			EventManager.Instance.Publish(new UnitSpawnedEvent(hero, UnitType.Hero, hero.GetID(), characterId));
			return hero;
		}

		public async UniTask<Monster> CreateMonsterAsync(int monsterId, Vector3 pos, Faction faction = Faction.Enemy, CancellationToken ct = default)
		{
			Table_Monster.Row row = Table_Monster.Get(monsterId);
			if (row == null)
			{
				Debug.LogError($"[UnitFactory] Table_Monster.Get({monsterId}) == null");
				return null;
			}

			Monster monster = await SpawnAndComposeAsync<Monster>(UnitType.Monster, row.Path, row.Name, monsterId, row.BaseStatID, row.SkillSetID, row.Skin, row.Radius, pos, faction, ct);
			if (monster == null)
			{
				return null;
			}

			monster.RefreshAnimationStats();
			EventManager.Instance.Publish(new UnitSpawnedEvent(monster, UnitType.Monster, monster.GetID(), monsterId));
			return monster;
		}

		// 공통 생성/주입 흐름 — Hero/Monster 동일
		async UniTask<T> SpawnAndComposeAsync<T>(UnitType unitType, string prefabAddress, string displayName, int id, int baseStatId, int skillSetId, string skinAddress, float radius, Vector3 pos, Faction faction, CancellationToken ct)
			where T : UnitBase
		{
			if (string.IsNullOrEmpty(prefabAddress) == true)
			{
				Debug.LogError($"[UnitFactory] 프리팹 Address 비어있음 (id={id})");
				return null;
			}

			GameObject prefab = await ResourceManager.Instance.AcquireAsync<GameObject>(prefabAddress, ct);
			if (prefab == null)
			{
				Debug.LogError($"[UnitFactory] 프리팹 로드 실패: {prefabAddress}");
				return null;
			}

			// UnitContainer 의 Type별 sub-Transform 아래로 스폰
			Transform parent = UnitContainer.Instance.GetRoot(unitType);
			GameObject go = Object.Instantiate(prefab, pos, Quaternion.identity, parent);
			go.name = $"{displayName}_{id}";

			T unit = go.GetComponent<T>();
			if (unit == null)
			{
				Debug.LogError($"[UnitFactory] 프리팹에 {typeof(T).Name} 컴포넌트 없음: {prefabAddress}");
				Object.Destroy(go);
				return null;
			}

			// 0. ID 주입 — 유일 인스턴스 ID + 테이블 ID
			unit.InjectIDs(_nextInstanceId++, id);

			// 1. 스탯 — Table_BaseStat 주입
			StatContainer stats = StatContainerFactory.FromBaseStatID(baseStatId);
			unit.InjectStats(stats);

			// 2. Vitals — stats 의 MaxHP 기준 초기화
			Vitals vitals = new Vitals(stats);
			vitals.InitHp();
			vitals.InitBreakGage();
			unit.InjectVitals(vitals);

			// 3. BuffContainer — 비어있는 상태로 시작
			//    (SkillContainer 등록보다 먼저: Passive 스킬이 등록 즉시 ActivateBuff 효과를 쓸 수 있어야 함)
			BuffContainer bc = new BuffContainer(unit);
			unit.InjectBuffContainer(bc);

			// 4. SkillContainer — SkillSet 의 Skill_1~4 를 "Base" 출처로 등록
			SkillContainer sc = new SkillContainer(unit);
			RegisterBaseSkills(sc, skillSetId);
			unit.InjectSkillContainer(sc);

			// 5. Mover — radius/mass (mass 는 테이블에 없으므로 기본 1f)
			UnitMover mover = unit.GetComponent<UnitMover>();
			if (mover != null)
			{
				mover.Initialize(radius, DefaultMass);
			}

			// 6. Skin — AnimatorOverrideController 교체 (주소 비어있으면 스킵)
			if (string.IsNullOrEmpty(skinAddress) == false)
			{
				await ApplySkinAsync(unit, skinAddress, ct);
			}

			// 7. Faction
			unit.SetFaction(faction);

			return unit;
		}

		static void RegisterBaseSkills(SkillContainer sc, int skillSetId)
		{
			if (skillSetId <= 0)
			{
				return;
			}
			Table_SkillSet.Row row = Table_SkillSet.Get(skillSetId);
			if (row == null)
			{
				return;
			}
			sc.Register(row.Skill_1, SourceBase);
			sc.Register(row.Skill_2, SourceBase);
			sc.Register(row.Skill_3, SourceBase);
			sc.Register(row.Skill_4, SourceBase);
		}

		static async UniTask ApplySkinAsync(UnitBase unit, string skinAddress, CancellationToken ct)
		{
			RuntimeAnimatorController controller = await ResourceManager.Instance.AcquireAsync<RuntimeAnimatorController>(skinAddress, ct);
			if (controller == null)
			{
				return;
			}
			UnitAnimator animator = unit.GetComponent<UnitAnimator>();
			if (animator != null)
			{
				animator.SetController(controller);
			}
		}
	}
}
