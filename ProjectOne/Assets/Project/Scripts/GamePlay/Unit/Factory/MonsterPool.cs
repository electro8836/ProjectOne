using UnityEngine;
using EDT;
using ProjectOne.Monsters;
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

			component.SetMonsterType(row?.MonsterType ?? MonsterType.None);

			// AIType 이 FSM/BT 선택까지 겸한다 (몬스터 설계 5장).
			// AIGroupID 가 비었으면 row 가 null 이고 접근형으로 폴백한다 — 경고는 MonsterCatalog 가 낸다.
			AiBrain brain = AiBrainFactory.CreateForMonster(component, MonsterCatalog.GetAI(_monsterId));
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

			// 리쉬(복귀)와 필드 리젠의 기준점 — 풀 재사용이므로 스폰마다 다시 찍는다.
			fromPool.SetSpawnOrigin(pos);
			fromPool.OnActivate();
			return fromPool;
		}

		public void Despawn(Monster monster)
		{
			Release(monster);
		}
	}
}
