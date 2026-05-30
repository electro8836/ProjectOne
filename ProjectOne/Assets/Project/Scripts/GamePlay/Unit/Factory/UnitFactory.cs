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
	public sealed class UnitFactory : Singleton<UnitFactory>
	{
		private const string SourceBase = "Base";

		private const float DefaultMass = 1f;

		private static int _nextInstanceId = 1;

		public static int PeekNextInstanceId => _nextInstanceId;

		private UnitFactory()
		{
		}

		public async UniTask<UnitBase> CreateAsync(UnitType type, int id, Vector3 pos, Faction faction = Faction.None, CancellationToken ct = default(CancellationToken))
		{
			switch (type)
			{
			case UnitType.Hero:
				return await CreateHeroAsync(id, pos, (faction == Faction.None) ? Faction.Player : faction, ct);
			case UnitType.Monster:
				return await CreateMonsterAsync(id, pos, (faction == Faction.None) ? Faction.Enemy : faction, ct);
			default:
				Debug.LogError($"[UnitFactory] 지원하지 않는 UnitType: {type}");
				return null;
			}
		}

		public async UniTask<Hero> CreateHeroAsync(int characterId, Vector3 pos, Faction faction = Faction.Player, CancellationToken ct = default(CancellationToken))
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
				Table_SkinInfo.Row row2 = Table_SkinInfo.Get(row.SkinID);
				if (row2 != null)
				{
					skinAddress = row2.Path;
				}
			}
			Hero hero = await SpawnAndComposeAsync<Hero>(UnitType.Hero, row.Path, row.Name, characterId, row.BaseStatID, row.SkillSetID, skinAddress, pos, faction, ct);
			if (hero == null)
			{
				return null;
			}
			HeroAspectRegistry.Instance.ApplyAll(hero);
			hero.RefreshAnimationStats();
			EventManager.Instance.Publish(new UnitSpawnedEvent(hero, UnitType.Hero, hero.GetID(), characterId));
			return hero;
		}

		public async UniTask<Monster> CreateMonsterAsync(int monsterId, Vector3 pos, Faction faction = Faction.Enemy, CancellationToken ct = default(CancellationToken))
		{
			MonsterPool monsterPool = await MonsterPoolHub.Instance.GetOrCreatePoolAsync(monsterId, ct);
			if (monsterPool == null)
			{
				return null;
			}
			Monster monster = monsterPool.Spawn(pos);
			monster.RefreshAnimationStats();
			EventManager.Instance.Publish(new UnitSpawnedEvent(monster, UnitType.Monster, monster.GetID(), monsterId));
			return monster;
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

		private async UniTask<T> SpawnAndComposeAsync<T>(UnitType unitType, string prefabAddress, string displayName, int id, int baseStatId, int skillSetId, string skinAddress, Vector3 pos, Faction faction, CancellationToken ct) where T : UnitBase
		{
			if (string.IsNullOrEmpty(prefabAddress))
			{
				Debug.LogError($"[UnitFactory] 프리팹 Address 비어있음 (id={id})");
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
			val2.name = $"{displayName}_{id}";
			T unit = val2.GetComponent<T>();
			if (unit == null)
			{
				Debug.LogError(("[UnitFactory] 프리팹에 " + typeof(T).Name + " 컴포넌트 없음: " + prefabAddress));
				Object.Destroy(val2);
				return null;
			}
			ComposeUnit(unit, id, baseStatId, skillSetId, faction);
			if (!string.IsNullOrEmpty(skinAddress))
			{
				await ApplySkinAsync(unit, skinAddress, ct);
			}
			return unit;
		}

		public void ComposeUnit(UnitBase unit, int tableId, int baseStatId, int skillSetId, Faction faction)
		{
			unit.SetIDs(_nextInstanceId++, tableId);
			StatContainer stats = StatContainerFactory.FromBaseStatID(baseStatId);
			unit.SetStats(stats);
			Vitals vitals = new Vitals(stats);
			vitals.InitHp();
			vitals.InitBreakGage();
			unit.SetVitals(vitals);
			BuffContainer buffContainer = new BuffContainer(unit);
			unit.SetBuffContainer(buffContainer);
			SkillContainer skillContainer = new SkillContainer(unit);
			RegisterBaseSkills(skillContainer, skillSetId);
			unit.SetSkillContainer(skillContainer);
			UnitMover component = unit.GetComponent<UnitMover>();
			if (component != null)
			{
				component.Initialize(unit.Radius, 1f);
			}
			unit.SetFaction(faction);
		}

		private static void RegisterBaseSkills(SkillContainer sc, int skillSetId)
		{
			if (skillSetId > 0)
			{
				Table_SkillSet.Row row = Table_SkillSet.Get(skillSetId);
				if (row != null)
				{
					sc.Register(row.Skill_1, "Base");
					sc.Register(row.Skill_2, "Base");
					sc.Register(row.Skill_3, "Base");
					sc.Register(row.Skill_4, "Base");
				}
			}
		}

		private static async UniTask ApplySkinAsync(UnitBase unit, string skinAddress, CancellationToken ct)
		{
			RuntimeAnimatorController val = await ResourceManager.Instance.AcquireAsync<RuntimeAnimatorController>(skinAddress, ct);
			if (!(val == null))
			{
				UnitAnimator component = unit.GetComponent<UnitAnimator>();
				if (component != null)
				{
					component.SetController(val);
				}
			}
		}
	}
}
