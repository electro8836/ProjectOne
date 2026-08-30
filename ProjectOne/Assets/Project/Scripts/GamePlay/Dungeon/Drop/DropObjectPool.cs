using UnityEngine;
using EDT;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 드랍오브젝트 프리팹 1종(타입)에 대한 풀. DropManager 가 타입별로 생성하고
	// Setup 으로 프리팹/타입/용량을 주입한다. (ProjectilePool/MonsterPool 패턴)
	public class DropObjectPool : PoolBase<DropObject>
	{
		private GameObject _prefab;
		private DropObjectType _type;

		// 허브 생성 직후 1회 — 프리팹/타입/용량 주입 (Awake/prewarm 전에 SetActive(false) 상태에서 호출)
		public void Setup(GameObject prefab, DropObjectType type, int cap)
		{
			_prefab = prefab;
			_type = type;
			capacity = cap;
			if (maxCapacity < cap)
			{
				maxCapacity = cap;
			}
		}

		protected override DropObject CreateItem()
		{
			GameObject go = Object.Instantiate(_prefab, transform);
			DropObject drop = go.GetComponent<DropObject>();
			if (drop == null)
			{
				Debug.LogError($"[DropObjectPool] 프리팹에 DropObject 컴포넌트 없음 (type={_type})");
				return null;
			}

			return drop;
		}

		// 외부 API — 순서(Get → 위치 설정 → Initialize → OnActivate)를 내부에서 보장
		public DropObject Spawn(Vector3 pos)
		{
			DropObject drop = GetFromPool();
			drop.transform.position = pos;
			drop.Initialize(this);
			drop.OnActivate();
			return drop;
		}
	}
}
