using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Projectile
{
	// 발사체 프리팹 1종에 대한 풀. ProjectileManager 가 주소별로 생성하고 Setup 으로 프리팹을 주입한다.
	public class ProjectilePool : PoolBase<Projectile>
	{
		private GameObject _prefab;

		// 허브 생성 직후 1회 — 프리팹/용량 주입 (Awake/prewarm 전에 SetActive(false) 상태에서 호출)
		public void Setup(GameObject prefab, int cap)
		{
			_prefab = prefab;
			capacity = cap;
			if (maxCapacity < cap)
			{
				maxCapacity = cap;
			}
		}

		protected override Projectile CreateItem()
		{
			GameObject go = Object.Instantiate(_prefab, transform);
			Projectile proj = go.GetComponent<Projectile>();
			if (proj == null)
			{
				Debug.LogError("[ProjectilePool] 프리팹에 Projectile 컴포넌트 없음");
				return null;
			}

			return proj;
		}

		// 외부 API — 순서(Get → Initialize → OnActivate)를 내부에서 보장
		public Projectile Spawn(ProjectileData data)
		{
			Projectile proj = GetFromPool();
			proj.Initialize(data, this);
			proj.OnActivate();
			return proj;
		}
	}
}
