using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Summons
{
	// 소환물 수명 관리 (설계 7.3).
	//
	// 주인별·소환종류별 목록을 들고 MaxCount 상한을 지킨다.
	// 초과하면 가장 오래된 것부터 소멸시킨다 — 새로 부른 것이 안 나오면 조작감이 나빠진다.
	public class SummonManager : MonoSingleton<SummonManager>
	{
		protected override bool Persistent => false;

		private struct Key
		{
			public UnitBase owner;
			public EDT.Summon summonId;
		}

		private sealed class KeyComparer : IEqualityComparer<Key>
		{
			public bool Equals(Key a, Key b)
			{
				return a.owner == b.owner && a.summonId == b.summonId;
			}

			public int GetHashCode(Key k)
			{
				int ownerHash = (k.owner != null) ? k.owner.GetID() : 0;
				return (ownerHash * 397) ^ (int)k.summonId;
			}
		}

		private readonly Dictionary<Key, List<SummonUnit>> _byOwner = new Dictionary<Key, List<SummonUnit>>(new KeyComparer());

		// 소환 요청. 프리팹 로드가 끼어 있으므로 비동기다.
		// count 는 SkillEffect 의 P2(Count) — 한 번에 여러 마리를 부르는 경우가 있다.
		public async UniTask SpawnAsync(UnitBase owner, EDT.Summon summonId, int count, float duration, float radius,
			Vector3 center, CancellationToken ct = default(CancellationToken))
		{
			if (owner == null || summonId == EDT.Summon.None || count <= 0)
			{
				return;
			}

			SummonPool pool = await SummonPoolHub.Instance.GetOrCreatePoolAsync(summonId, ct);
			if (pool == null)
			{
				return;
			}

			// await 사이에 주인이 죽었을 수 있다.
			if (owner == null || owner.IsDead == true)
			{
				return;
			}

			Table_Summon.Row row = Table_Summon.Get(summonId);
			int maxCount = (row != null && row.MaxCount > 0) ? row.MaxCount : int.MaxValue;

			List<SummonUnit> list = getOrCreateList(owner, summonId);
			sweep(pool, list);

			for (int i = 0; i < count; i++)
			{
				// 상한 초과분은 가장 오래된 것부터 지운다 (설계 7.3).
				while (list.Count >= maxCount && list.Count > 0)
				{
					despawn(pool, list[0]);
					list.RemoveAt(0);
				}

				if (maxCount <= 0)
				{
					break;
				}

				SummonUnit unit = pool.Spawn(owner, center, duration, radius);
				if (unit == null)
				{
					break;
				}

				list.Add(unit);
			}
		}

		// 주인이 죽거나 씬을 뜰 때 — DieWithOwner 여부와 무관하게 전부 회수한다.
		public void ReleaseAllOf(UnitBase owner)
		{
			Dictionary<Key, List<SummonUnit>>.Enumerator e = _byOwner.GetEnumerator();
			List<Key> removeKeys = new List<Key>(4);
			while (e.MoveNext() == true)
			{
				if (e.Current.Key.owner != owner)
				{
					continue;
				}

				SummonPool pool = SummonPoolHub.Instance.GetPool(e.Current.Key.summonId);
				List<SummonUnit> list = e.Current.Value;
				for (int i = 0; i < list.Count; i++)
				{
					despawn(pool, list[i]);
				}

				list.Clear();
				removeKeys.Add(e.Current.Key);
			}

			for (int i = 0; i < removeKeys.Count; i++)
			{
				_byOwner.Remove(removeKeys[i]);
			}
		}

		public void ReleaseAll()
		{
			Dictionary<Key, List<SummonUnit>>.Enumerator e = _byOwner.GetEnumerator();
			while (e.MoveNext() == true)
			{
				SummonPool pool = SummonPoolHub.Instance.GetPool(e.Current.Key.summonId);
				List<SummonUnit> list = e.Current.Value;
				for (int i = 0; i < list.Count; i++)
				{
					despawn(pool, list[i]);
				}

				list.Clear();
			}

			_byOwner.Clear();
		}

		// 같은 주인·같은 종류 소환물 중 이 개체가 몇 번째인지와 총 몇 마리인지. 없으면 -1.
		//
		// 결과를 캐싱하면 안 된다 — sweep 이 죽은 개체를 목록 중간에서 걷어내고 상한 초과분은 앞에서 빠지므로
		// 형제가 하나 사라지면 뒤쪽 번호가 전부 당겨진다. 쓰는 순간에 물어야 한다.
		// 번호와 총수를 함께 돌려주는 것도 같은 이유다. 따로 물으면 그 사이에 목록이 바뀔 수 있다.
		public int GetIndexOf(SummonUnit unit, out int total)
		{
			total = 0;
			if (unit == null || unit.Owner == null)
			{
				return -1;
			}

			Key key;
			key.owner = unit.Owner;
			key.summonId = unit.SummonId;

			List<SummonUnit> list;
			if (_byOwner.TryGetValue(key, out list) == false)
			{
				return -1;
			}

			total = list.Count;
			return list.IndexOf(unit);
		}

		private List<SummonUnit> getOrCreateList(UnitBase owner, EDT.Summon summonId)
		{
			Key key;
			key.owner = owner;
			key.summonId = summonId;

			List<SummonUnit> list;
			if (_byOwner.TryGetValue(key, out list) == false)
			{
				list = new List<SummonUnit>(4);
				_byOwner[key] = list;
			}

			return list;
		}

		// 수명이 다했거나 파괴된 개체를 풀로 돌려보내고 목록에서 걷어낸다.
		// 이게 없으면 상한이 유령으로 채워지고 풀도 회수되지 않는다.
		private static void sweep(SummonPool pool, List<SummonUnit> list)
		{
			for (int i = list.Count - 1; i >= 0; i--)
			{
				SummonUnit unit = list[i];
				if (unit == null)
				{
					list.RemoveAt(i);
					continue;
				}

				if (unit.IsDead == true)
				{
					despawn(pool, unit);
					list.RemoveAt(i);
				}
			}
		}

		// 사망 처리는 SummonUnit 이 자기 틱에서 한다(수명 만료 · 주인 사망 · 피격 사망).
		// 풀 반환만 여기서 한 프레임 늦춰 처리한다 — 순회 중 비활성화는 위험하다.
		private void Update()
		{
			Dictionary<Key, List<SummonUnit>>.Enumerator e = _byOwner.GetEnumerator();
			while (e.MoveNext() == true)
			{
				List<SummonUnit> list = e.Current.Value;
				if (list.Count == 0)
				{
					continue;
				}

				sweep(SummonPoolHub.Instance.GetPool(e.Current.Key.summonId), list);
			}
		}

		private static void despawn(SummonPool pool, SummonUnit unit)
		{
			if (unit == null)
			{
				return;
			}

			if (pool != null)
			{
				pool.Despawn(unit);
				return;
			}

			Object.Destroy(unit.gameObject);
		}
	}
}
