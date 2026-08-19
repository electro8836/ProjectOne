using UnityEngine;
using EDT;
using ProjectOne.Utils;
using ProjectOne.Unit.AI;

namespace ProjectOne.Unit
{
	public class SummonPool : PoolBase<SummonUnit>
	{
		private GameObject _prefab;

		private EDT.Summon _summonId;

		private Table_Summon.Row _row;

		public void Setup(GameObject prefab, EDT.Summon summonId, Table_Summon.Row row, int cap)
		{
			_prefab = prefab;
			_summonId = summonId;
			_row = row;
			capacity = cap;
			if (maxCapacity < cap)
			{
				maxCapacity = cap;
			}
		}

		protected override SummonUnit CreateItem()
		{
			GameObject val = Object.Instantiate<GameObject>(_prefab, transform);
			SummonUnit component = val.GetComponent<SummonUnit>();
			if (component == null)
			{
				Debug.LogError($"[SummonPool] 프리팹에 SummonUnit 컴포넌트 없음 (id={_summonId})");
				return null;
			}

			component.SetSummonRow(_summonId, _row);

			// 소환물은 주인 진영을 따르지만 풀 생성 시점에는 주인을 모른다.
			// 실제 진영은 Spawn 에서 주인 것으로 덮어쓴다.
			UnitFactory.Instance.ComposeSummon(component, _row, Faction.Player);
			val.name = string.Format("{0}_{1}", _summonId, component.GetID());

			AiBrain brain = AiBrainFactory.CreateForSummon(component, _row);
			if (brain != null)
			{
				component.SetBrain(brain);
			}

			return component;
		}

		public SummonUnit Spawn(UnitBase owner, Vector3 pos, float duration, float radius)
		{
			SummonUnit fromPool = GetFromPool();
			if (fromPool == null)
			{
				return null;
			}

			if (owner != null)
			{
				fromPool.SetFaction(owner.Faction);
			}

			fromPool.OnSpawnReset(pos);
			fromPool.SetSpawnContext(owner, duration, radius);
			fromPool.OnActivate();
			return fromPool;
		}

		public void Despawn(SummonUnit summon)
		{
			Release(summon);
		}
	}
}
