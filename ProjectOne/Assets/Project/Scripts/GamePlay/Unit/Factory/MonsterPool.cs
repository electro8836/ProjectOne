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
			int statGroupId = row?.StatGroupID ?? 0;

			// 풀 생성 시점에는 스폰 레벨을 모르므로 1레벨로 구성한다.
			// 실제 레벨은 Spawn(pos, level) 에서 스탯만 갈아끼운다.
			UnitFactory.Instance.ComposeUnit(component, _monsterId, statGroupId, 1, Faction.Enemy);
			val.name = string.Format("{0}_{1}", (row != null) ? row.Name : "Monster", component.GetID());

			// 몬스터 타입별 자동전투 두뇌 주입 (접근형 — 보스도 3단계까지 폴백)
			AiBrain brain = AiBrainFactory.CreateForMonster(component, row?.MonsterType ?? MonsterType.Normal);
			if (brain != null)
			{
				component.SetBrain(brain);
			}

			return component;
		}

		// level 은 MonsterSpawn.Level(또는 DungeonStage.MonsterLevel 오버라이드)이 정한다.
		// 풀에서 꺼낸 개체의 레벨이 다르면 스탯을 다시 만든다.
		public Monster Spawn(Vector3 pos, int level)
		{
			Monster fromPool = GetFromPool();
			UnitFactory.Instance.ApplyMonsterLevel(fromPool, _monsterId, level);
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
