using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Unit.AI;

namespace ProjectOne.Unit
{
	public class MonsterPool : PoolBase<Monster>
	{
		private GameObject _prefab;

		private int _monsterId;

		public void Setup(GameObject prefab, int monsterId, int cap)
		{
			_prefab = prefab;
			_monsterId = monsterId;
			capacity = cap;
			if (maxCapacity < cap)
			{
				maxCapacity = cap;
			}
		}

		protected override Monster CreateItem()
		{
			GameObject val = Object.Instantiate<GameObject>(_prefab, transform);
			Monster component = val.GetComponent<Monster>();
			if (component == null)
			{
				Debug.LogError($"[MonsterPool] 프리팹에 Monster 컴포넌트 없음 (id={_monsterId})");
				return null;
			}

			Table_Monster.Row row = Table_Monster.Get(_monsterId);
			int baseStatId = row?.BaseStatID ?? 0;
			int skillSetId = row?.BaseSkillSet ?? 0;
			UnitFactory.Instance.ComposeUnit(component, _monsterId, baseStatId, skillSetId, Faction.Enemy);
			val.name = string.Format("{0}_{1}", (row != null) ? row.Name : "Monster", component.GetID());

			// 몬스터 타입별 자동전투 두뇌 주입 (접근형 — 보스도 3단계까지 폴백)
			AiBrain brain = AiBrainFactory.CreateForMonster(component, row?.MonsterType ?? MonsterTypes.Normal);
			if (brain != null)
			{
				component.SetBrain(brain);
			}

			return component;
		}

		public Monster Spawn(Vector3 pos)
		{
			Monster fromPool = GetFromPool();
			fromPool.OnSpawnReset(pos);
			fromPool.OnActivate();
			return fromPool;
		}

		public void Despawn(Monster monster)
		{
			Release(monster);
		}
	}
}
