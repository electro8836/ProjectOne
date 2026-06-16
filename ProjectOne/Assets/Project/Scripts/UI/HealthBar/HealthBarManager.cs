using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.UI
{
	// 히어로/몬스터 체력바 통합 진입점. 전투 씬의 WorldUICanvas 하위에 배치되어 씬과 생명주기를 함께한다.
	// - 히어로: 플레이어 1명. 상시 표시. UnitSpawnedEvent 로 생성, UnitDiedEvent 로 제거.
	// - 몬스터: 피격(DamageTakenEvent) 시 표시, 사망할 때까지 유지. 다수이므로 풀링.
	// 값/위치는 활성 바만 LateUpdate 에서 폴링 갱신한다(Vitals 에 변경 이벤트가 없음).
	public class HealthBarManager : MonoBehaviour
	{
		[SerializeField] private HeroHealthBarItem _heroBarPrefab;
		[SerializeField] private Transform _barRoot;            // 바 부모(WorldCanvas)
		[SerializeField] private MonsterHealthBarPool _monsterPool;
		[SerializeField] private float _offsetY = 0.2f;         // 위치 공식: Radius * 2 + offset (데미지텍스트와 동일)
		[SerializeField] private int _monsterHardCap = 40;      // 동시 활성 몬스터 바 최대 수

		private HeroHealthBarItem _heroBar;                     // 단일(플레이어 1명)
		private readonly Dictionary<int, MonsterHealthBarItem> _monsterBars = new Dictionary<int, MonsterHealthBarItem>(64);

		private void Awake()
		{
			EventManager.Instance.Subscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Subscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Subscribe<UnitDiedEvent>(onUnitDied);
		}

		private void Start()
		{
			// 매니저가 히어로보다 늦게 깬 경우 보강 — 이미 스폰된 히어로가 있으면 바 생성
			if (UnitContainer.HasInstance == false)
			{
				return;
			}

			IReadOnlyList<UnitBase> heroes = UnitContainer.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				UnitBase hero = heroes[i];
				if (hero != null && _heroBar == null)
				{
					spawnHeroBar(hero);
				}
			}
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitSpawnedEvent>(onUnitSpawned);
			EventManager.Instance.Unsubscribe<DamageTakenEvent>(onDamageTaken);
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(onUnitDied);
		}

		// UnitContainer(ExecutionOrder -100) 가 위치 캐시를 먼저 갱신한 뒤 실행되어 최신 위치를 읽는다.
		private void LateUpdate()
		{
			updateHeroBar();
			updateMonsterBars();
		}

		private void updateHeroBar()
		{
			if (_heroBar == null)
			{
				return;
			}

			UnitBase unit = _heroBar.Unit;
			if (unit == null || unit.IsDead == true)
			{
				destroyHeroBar();
				return;
			}

			_heroBar.SetWorldPosition(computeBarPos(unit));
			_heroBar.RefreshValues();
		}

		private void updateMonsterBars()
		{
			IReadOnlyList<MonsterHealthBarItem> active = _monsterPool.ActiveItems;
			for (int i = active.Count - 1; i >= 0; i--)
			{
				MonsterHealthBarItem bar = active[i];
				UnitBase unit = bar.Unit;
				if (unit == null || unit.IsDead == true)
				{
					int id = (unit != null) ? unit.GetID() : -1;
					_monsterPool.RemoveActive(bar);
					if (id >= 0)
					{
						_monsterBars.Remove(id);
					}

					continue;
				}

				bar.SetWorldPosition(computeBarPos(unit));
				bar.RefreshValue();
			}
		}

		private void onUnitSpawned(UnitSpawnedEvent e)
		{
			if (e.UnitType == UnitType.Hero && e.Unit != null && _heroBar == null)
			{
				spawnHeroBar(e.Unit);
			}
		}

		private void onDamageTaken(DamageTakenEvent e)
		{
			if (e.Target == null || e.Target.GetUnitType() != UnitType.Monster)
			{
				return;
			}

			showMonsterBar(e.Target);
		}

		private void onUnitDied(UnitDiedEvent e)
		{
			if (e.UnitType == UnitType.Hero)
			{
				destroyHeroBar();
				return;
			}

			if (e.UnitType == UnitType.Monster)
			{
				MonsterHealthBarItem bar;
				if (_monsterBars.TryGetValue(e.InstanceID, out bar) == true)
				{
					_monsterBars.Remove(e.InstanceID);
					_monsterPool.RemoveActive(bar);
				}
			}
		}

		private void spawnHeroBar(UnitBase hero)
		{
			_heroBar = Instantiate(_heroBarPrefab, _barRoot);
			_heroBar.Bind(hero);
			_heroBar.SetWorldPosition(computeBarPos(hero));
			_heroBar.RefreshValues();
		}

		private void destroyHeroBar()
		{
			if (_heroBar != null)
			{
				Destroy(_heroBar.gameObject);
				_heroBar = null;
			}
		}

		private void showMonsterBar(UnitBase monster)
		{
			int id = monster.GetID();
			if (_monsterBars.ContainsKey(id) == true)
			{
				return;
			}

			// hardCap 초과 시 가장 오래된 바 회수 (ActiveItems[0] = 추가 순 최선두)
			if (_monsterBars.Count >= _monsterHardCap)
			{
				IReadOnlyList<MonsterHealthBarItem> active = _monsterPool.ActiveItems;
				if (active.Count > 0)
				{
					MonsterHealthBarItem oldest = active[0];
					int oldId = (oldest.Unit != null) ? oldest.Unit.GetID() : -1;
					_monsterPool.RemoveActive(oldest);
					if (oldId >= 0)
					{
						_monsterBars.Remove(oldId);
					}
				}
			}

			MonsterHealthBarItem bar = _monsterPool.Spawn(monster);
			bar.SetWorldPosition(computeBarPos(monster));
			bar.RefreshValue();
			_monsterBars[id] = bar;
		}

		// 위치 공식: 루트에서 반지름 * 2 + 오프셋 만큼 y축으로 올린다 (데미지텍스트/브레이크와 동일).
		private Vector3 computeBarPos(UnitBase unit)
		{
			float upOffset = unit.Radius * 2f + _offsetY;
			return unit.transform.position + Vector3.up * upOffset;
		}
	}
}
