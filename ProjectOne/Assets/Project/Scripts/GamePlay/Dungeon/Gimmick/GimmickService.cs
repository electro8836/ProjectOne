using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Resources;
using ProjectOne.Unit;
using ProjectOne.Utils;

namespace ProjectOne.Dungeon
{
	// 던전 기믹 스폰/회수 서비스(전투씬 수명). "언제 띄울지"는 각 StageMode 가 결정하고,
	// 여기서는 "어떻게"(히어로 위치 인스턴스화 + 일괄 회수)만 담당한다.
	// 프리팹은 ResourceManager 로 캐시(참조카운트)하고 인스턴스는 Instantiate 로 만든다. (DropManager 패턴)
	public sealed class GimmickService : MonoSingleton<GimmickService>
	{
		// 던전 상점 기믹 Addressable 주소 — 각 모드가 스폰 요청 시 사용
		public const string ShopGimmickAddress = "Prefab_DungeonShop";

		protected override bool Persistent => false;

		// 현재 떠 있는 기믹 인스턴스
		private readonly List<DungeonGimmick> _active = new List<DungeonGimmick>();

		// 캐시한 프리팹 (주소 → 프리팹). Clear 시 주소로 Release
		private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

		// 히어로 위치에 기믹 1개를 스폰한다. 히어로가 없으면 무시.
		public async UniTask SpawnAtHeroAsync(string address)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			if (findFirstAliveHero() == null)
			{
				return;
			}

			GameObject prefab = await acquirePrefabAsync(address);
			if (prefab == null)
			{
				return;
			}

			// await 사이 히어로가 사라졌을 수 있으니 위치를 다시 확인
			UnitBase hero = findFirstAliveHero();
			if (hero == null)
			{
				return;
			}

			GameObject go = Instantiate(prefab, hero.transform.position, Quaternion.identity, transform);
			DungeonGimmick gimmick = go.GetComponent<DungeonGimmick>();
			if (gimmick == null)
			{
				Debug.LogError($"[GimmickService] 프리팹에 DungeonGimmick 컴포넌트 없음 ({address})");
				Destroy(go);
				return;
			}

			_active.Add(gimmick);
		}

		// 현재 떠 있는 기믹을 모두 회수한다.
		public void DespawnAll()
		{
			for (int i = 0; i < _active.Count; i++)
			{
				DungeonGimmick gimmick = _active[i];
				if (gimmick != null)
				{
					gimmick.OnDespawn();
					Destroy(gimmick.gameObject);
				}
			}

			_active.Clear();
		}

		// 던전 종료 시 호출 — 기믹 회수 + 캐시한 프리팹 핸들 해제.
		public void Clear()
		{
			DespawnAll();

			Dictionary<string, GameObject>.Enumerator e = _prefabs.GetEnumerator();
			while (e.MoveNext())
			{
				ResourceManager.Instance.Release(e.Current.Key);
			}

			_prefabs.Clear();
		}

		private async UniTask<GameObject> acquirePrefabAsync(string address)
		{
			GameObject cached;
			if (_prefabs.TryGetValue(address, out cached) == true)
			{
				return cached;
			}

			(bool canceled, GameObject loaded) = await ResourceManager.Instance
				.AcquireAsync<GameObject>(address, this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
			if (canceled == true || loaded == null)
			{
				return null;
			}

			// await 사이 중복 로드 방지 — 이미 캐시됐으면 방금 획득한 핸들만 해제
			if (_prefabs.ContainsKey(address) == true)
			{
				ResourceManager.Instance.Release(address);
				return _prefabs[address];
			}

			_prefabs[address] = loaded;
			return loaded;
		}

		private static UnitBase findFirstAliveHero()
		{
			if (UnitContainer.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase h = heroes[i];
				if (h != null && h.IsDead == false)
				{
					return h;
				}
			}

			return null;
		}
	}
}
