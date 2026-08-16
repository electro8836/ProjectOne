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
using ProjectOne.Unit.AI;
using ProjectOne.UserData;

namespace ProjectOne.Unit
{
	public sealed class UnitFactory : Singleton<UnitFactory>
	{
		private const string SourceBase = "Base";

		private const string SourceSpecial = "Special";

		private const float DefaultMass = 1f;

		// 장비 장착 Aspect — 무상태이므로 단일 인스턴스 재사용 (Register 는 중복 방지됨)
		private readonly EquipmentAspect _equipmentAspect = new EquipmentAspect();

		// 히어로 프리팹 Addressable 주소 — 캐릭터가 하나뿐이라 테이블이 아닌 부트에서 주입받는다.
		private string _heroPrefabAddress = string.Empty;

		private static int _nextInstanceId = 1;

		public static int PeekNextInstanceId => _nextInstanceId;

		private UnitFactory()
		{
		}

		// 부트 시점에 GameBootstrapper 가 인스펙터 값으로 설정한다.
		public void SetHeroPrefabAddress(string address)
		{
			_heroPrefabAddress = address;
		}

		public async UniTask<UnitBase> CreateAsync(UnitType type, int id, Vector3 pos, Faction faction = Faction.None, CancellationToken ct = default(CancellationToken))
		{
			switch (type)
			{
			case UnitType.Hero:
				return await CreateHeroAsync(pos, (faction == Faction.None) ? Faction.Player : faction, false, ct);
			case UnitType.Monster:
				return await CreateMonsterAsync(id, 1, pos, (faction == Faction.None) ? Faction.Enemy : faction, ct);
			default:
				Debug.LogError($"[UnitFactory] 지원하지 않는 UnitType: {type}");
				return null;
			}
		}

		// autoControl=true 면 자동전투 AI 두뇌 주입, false 면 플레이어 직접조작(HeroController 유지)
		public async UniTask<Hero> CreateHeroAsync(Vector3 pos, Faction faction = Faction.Player, bool autoControl = false, CancellationToken ct = default(CancellationToken))
		{
			Hero hero = await SpawnAndComposeAsync<Hero>(UnitType.Hero, _heroPrefabAddress, "Hero", pos, faction, autoControl, ct);
			if (hero == null)
			{
				return null;
			}

			HeroAspectRegistry.Instance.Register(_equipmentAspect);
			HeroAspectRegistry.Instance.ApplyAll(hero);

			// 장비/레벨 등 최대치 변동 스탯이 모두 적용된 뒤 현재 게이지를 최대치로 재충전
			hero.Vitals.InitHp();

			hero.RefreshAnimationStats();
			EventManager.Instance.Publish(new UnitSpawnedEvent(hero, UnitType.Hero, hero.GetID(), 0));
			return hero;
		}

		public async UniTask<Monster> CreateMonsterAsync(int monsterId, int level, Vector3 pos, Faction faction = Faction.Enemy, CancellationToken ct = default(CancellationToken))
		{
			MonsterPool monsterPool = await MonsterPoolHub.Instance.GetOrCreatePoolAsync(monsterId, ct);
			if (monsterPool == null)
			{
				return null;
			}

			Monster monster = monsterPool.Spawn(pos, level);
			monster.RefreshAnimationStats();
			EventManager.Instance.Publish(new UnitSpawnedEvent(monster, UnitType.Monster, monster.GetID(), monsterId));
			return monster;
		}

		// 풀에서 꺼낸 몬스터의 레벨을 확정하고 스탯을 다시 만든다.
		// 같은 몬스터라도 스폰마다 레벨이 다를 수 있어(MonsterSpawn.Level / DungeonStage.MonsterLevel)
		// 풀 생성 시점의 1레벨 스탯을 그대로 쓸 수 없다.
		public void ApplyMonsterLevel(UnitBase unit, int monsterId, int level)
		{
			if (unit == null)
			{
				return;
			}

			int lv = level > 0 ? level : 1;
			if (unit.Level == lv)
			{
				return;		// 직전 스폰과 같은 레벨 — 재구성 불필요
			}

			Table_Monster.Row row = Table_Monster.Get(monsterId);
			int statGroupId = row?.StatGroupID ?? 0;

			StatContainer stats = StatContainerFactory.ForMonster(statGroupId, lv);
			unit.SetLevel(lv);
			unit.SetStats(stats);

			// Vitals 는 StatContainer 참조를 들고 있으므로 함께 교체한다.
			Vitals vitals = new Vitals(stats);
			vitals.InitHp();
			unit.SetVitals(vitals);
		}

		public void ReleaseMonster(Monster monster)
		{
			if (!(monster == null))
			{
				MonsterPool pool = MonsterPoolHub.Instance.GetPool(monster.GetTableID());
				if (pool != null)
				{
					pool.Despawn(monster);
				}
			}
		}

		private async UniTask<T> SpawnAndComposeAsync<T>(UnitType unitType, string prefabAddress, string displayName, Vector3 pos, Faction faction, bool autoControl, CancellationToken ct) where T : UnitBase
		{
			if (string.IsNullOrEmpty(prefabAddress))
			{
				Debug.LogError("[UnitFactory] 히어로 프리팹 Address 비어있음 — GameBootstrapper 인스펙터를 확인하세요.");
				return null;
			}

			GameObject val = await ResourceManager.Instance.AcquireAsync<GameObject>(prefabAddress, ct);
			if (val == null)
			{
				Debug.LogError(("[UnitFactory] 프리팹 로드 실패: " + prefabAddress));
				return null;
			}

			Transform root = UnitContainer.Instance.GetRoot(unitType);
			GameObject val2 = Object.Instantiate<GameObject>(val, pos, Quaternion.identity, root);
			T unit = val2.GetComponent<T>();
			if (unit == null)
			{
				Debug.LogError(("[UnitFactory] 프리팹에 " + typeof(T).Name + " 컴포넌트 없음: " + prefabAddress));
				Object.Destroy(val2);
				return null;
			}

			ComposeHero(unit, faction, autoControl);
			val2.name = $"{displayName}_{unit.GetID()}";
			return unit;
		}

		// 공통 구성 — 스탯/체력/버프/스킬컨테이너/이동체 초기화. 스킬 등록·인디케이터는 호출자가 수행.
		private void ComposeBase(UnitBase unit, int tableId, int level, StatContainer stats, Faction faction)
		{
			unit.SetIDs(_nextInstanceId++, tableId);
			unit.SetLevel(level);
			unit.SetStats(stats);
			Vitals vitals = new Vitals(stats);
			vitals.InitHp();
			unit.SetVitals(vitals);
			BuffContainer buffContainer = new BuffContainer(unit);
			unit.SetBuffContainer(buffContainer);
			SkillContainer skillContainer = new SkillContainer(unit);
			unit.SetSkillContainer(skillContainer);
			UnitMover component = unit.GetComponent<UnitMover>();
			if (component != null)
			{
				component.Initialize(unit.Radius, unit.ColliderOffset, 1f);
			}

			unit.SetFaction(faction);
		}

		// 몬스터 구성 — Monster.SkillIDs 기반 스킬 등록 (MonsterPool 에서 호출)
		public void ComposeUnit(UnitBase unit, int monsterId, int statGroupId, int level, Faction faction)
		{
			ComposeBase(unit, monsterId, level, StatContainerFactory.ForMonster(statGroupId, level), faction);
			RegisterMonsterSkills(unit.SkillContainer, monsterId);
			RefreshSkillIndicator(unit);
		}

		// 히어로 구성 — 캐릭터 레벨 기준 기본 스탯 + 자동전투 두뇌(옵션).
		private void ComposeHero(UnitBase unit, Faction faction, bool autoControl)
		{
			// 캐릭터 레벨은 스폰 시 확정 (실시간 반영 아님)
			int level = Account.Instance.Loadout.Level;
			ComposeBase(unit, 0, level, StatContainerFactory.ForCharacter(level), faction);

			// TODO(STEP 7) — 보유 스킬 등록은 무기 마스터리가 소유한다.
			// WeaponMastery.NormalAttackSkill(평타) + 스킬 트리의 SkillGrant 노드로 습득한 스킬.
			RefreshSkillIndicator(unit);

			if (autoControl == true)
			{
				// 이동/시선은 플레이어(HeroController)가 그대로 담당 — AI 는 스킬 자동 시전만.
				AiBrain brain = AiBrainFactory.CreateForHero(unit);
				if (brain != null)
				{
					unit.SetBrain(brain);
				}
			}
		}

		private static void RefreshSkillIndicator(UnitBase unit)
		{
			SkillIndicator skillIndicator = unit.GetComponent<SkillIndicator>();
			if (skillIndicator != null)
			{
				skillIndicator.ConfigureForHero();
				skillIndicator.SetSkills(unit.SkillContainer.GetAll());
			}
		}

		// 몬스터 보유 스킬 등록 — Monster.SkillIDs 배열 전체.
		// 어느 것이 기본공격인지는 배열 순서가 아니라 Skill.SkillCategory=Normal 로 판정한다.
		private static void RegisterMonsterSkills(SkillContainer sc, int monsterId)
		{
			Table_Monster.Row row = Table_Monster.Get(monsterId);
			if (row == null || row.SkillIDs == null)
			{
				return;
			}

			for (int i = 0; i < row.SkillIDs.Length; i++)
			{
				sc.Register(row.SkillIDs[i], SourceBase);
			}
		}
	}
}
