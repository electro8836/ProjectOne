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

namespace ProjectOne.Unit
{
	public sealed class UnitFactory : Singleton<UnitFactory>
	{
		private const string SourceBase = "Base";

		private const string SourceSpecial = "Special";

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
				return await CreateHeroAsync(id, pos, (faction == Faction.None) ? Faction.Player : faction, false, ct);
			case UnitType.Monster:
				return await CreateMonsterAsync(id, pos, (faction == Faction.None) ? Faction.Enemy : faction, ct);
			default:
				Debug.LogError($"[UnitFactory] 지원하지 않는 UnitType: {type}");
				return null;
			}
		}

		// autoControl=true 면 자동전투 AI 두뇌 주입(NPC/PVP), false 면 플레이어 직접조작(HeroController 유지)
		public async UniTask<Hero> CreateHeroAsync(int characterId, Vector3 pos, Faction faction = Faction.Player, bool autoControl = false, CancellationToken ct = default(CancellationToken))
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

			Hero hero = await SpawnAndComposeAsync<Hero>(UnitType.Hero, row.Path, row.Name, row, skinAddress, pos, faction, autoControl, ct);
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

		private async UniTask<T> SpawnAndComposeAsync<T>(UnitType unitType, string prefabAddress, string displayName, Table_Character.Row charRow, string skinAddress, Vector3 pos, Faction faction, bool autoControl, CancellationToken ct) where T : UnitBase
		{
			if (string.IsNullOrEmpty(prefabAddress))
			{
				Debug.LogError($"[UnitFactory] 프리팹 Address 비어있음 (id={charRow.ID})");
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
			val2.name = $"{displayName}_{charRow.ID}";
			T unit = val2.GetComponent<T>();
			if (unit == null)
			{
				Debug.LogError(("[UnitFactory] 프리팹에 " + typeof(T).Name + " 컴포넌트 없음: " + prefabAddress));
				Object.Destroy(val2);
				return null;
			}

			ComposeHero(unit, charRow, faction, autoControl);
			if (!string.IsNullOrEmpty(skinAddress))
			{
				await ApplySkinAsync(unit, skinAddress, ct);
			}

			return unit;
		}

		// 공통 구성 — 스탯/체력/버프/스킬컨테이너/이동체 초기화. 스킬 등록·인디케이터는 호출자가 수행.
		private void ComposeBase(UnitBase unit, int tableId, int baseStatId, Faction faction)
		{
			unit.SetIDs(_nextInstanceId++, tableId);
			StatContainer stats = StatContainerFactory.FromBaseStatID(baseStatId);
			unit.SetStats(stats);
			Vitals vitals = new Vitals(stats);
			vitals.InitHp();
			vitals.InitBreakGage();
			vitals.InitStamina();
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

		// 몬스터 구성 — SkillSet 기반 스킬 등록 (MonsterPool 에서 호출)
		public void ComposeUnit(UnitBase unit, int tableId, int baseStatId, int skillSetId, Faction faction)
		{
			ComposeBase(unit, tableId, baseStatId, faction);
			RegisterBaseSkills(unit.SkillContainer, skillSetId);
			RefreshSkillIndicator(unit);
		}

		// 히어로 구성 — SkillSet 기반 스킬 등록. autoControl 이면 자동전투 두뇌 주입 + 입력 컨트롤러 비활성화.
		private void ComposeHero(UnitBase unit, Table_Character.Row row, Faction faction, bool autoControl)
		{
			ComposeBase(unit, row.ID, row.BaseStatID, faction);
			SkillContainer sc = unit.SkillContainer;
			if (row.BaseSkillSet > 0)
			{
				Table_SkillSet.Row skillSetRow = Table_SkillSet.Get(row.BaseSkillSet);
				if (skillSetRow != null)
				{
					sc.Register(skillSetRow.BaseAttackSkill, SourceBase);
					sc.Register(skillSetRow.Skill_1, SourceBase);
					sc.Register(skillSetRow.Skill_2, SourceBase);
					sc.Register(skillSetRow.Skill_3, SourceBase);
					sc.Register(skillSetRow.Skill_4, SourceBase);
					sc.Register(skillSetRow.SpecialSkill_1, SourceSpecial);
					sc.Register(skillSetRow.SpecialSkill_2, SourceSpecial);
				}
			}
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
				skillIndicator.SetSkills(unit.SkillContainer.GetAll());
			}
		}

		private static void RegisterBaseSkills(SkillContainer sc, int skillSetId)
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
			sc.Register(row.BaseAttackSkill, "Base");
			sc.Register(row.Skill_1, "Base");
			sc.Register(row.Skill_2, "Base");
			sc.Register(row.Skill_3, "Base");
			sc.Register(row.Skill_4, "Base");
			sc.Register(row.SpecialSkill_1, "Base");
			sc.Register(row.SpecialSkill_2, "Base");
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
