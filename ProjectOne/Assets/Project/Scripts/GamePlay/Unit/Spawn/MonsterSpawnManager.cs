using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Event;

namespace ProjectOne.Unit
{
	public class MonsterSpawnManager : MonoSingleton<MonsterSpawnManager>
	{
		// 전투 전용 — 전투씬 수명에만 존재(로비 등으로 따라가지 않음)
		protected override bool Persistent => false;

		private readonly struct ActiveEntry
		{
			public readonly Monster monster;

			public readonly SpawnData owner;

			public ActiveEntry(Monster monster, SpawnData owner)
			{
				this.monster = monster;
				this.owner = owner;
			}
		}

		[Tooltip("사망 애니메이션 재생 후 풀로 반환하기까지의 지연(초)")]
		[SerializeField]
		private float _returnDelay = 1.5f;

		[Tooltip("개발용: 체크하면 몬스터가 캐스팅 외 전체 스킬도 인디케이터(주황) 표시. 기본(false)은 캐스팅 스킬만 항시 표시")]
		[SerializeField]
		private bool _showSkillIndicators = false;

		[Tooltip("몬스터 id별 스폰 설정 목록 — 일반/엘리트/보스 각각 엔트리 하나씩 등록")]
		[SerializeField]
		private List<SpawnData> _spawnList = new List<SpawnData>();

		private readonly Dictionary<int, ActiveEntry> _active = new Dictionary<int, ActiveEntry>();

		// ClearAlive 시 Dictionary 순회용 임시 버퍼 (재사용)
		private readonly List<ActiveEntry> _clearBuffer = new List<ActiveEntry>();

		// 1회성(웨이브/레이드) 스폰으로 현재 살아있는 몬스터 수 — SpawnOneShot 시 즉시 증가, 사망 시 감소
		private int _oneShotAlive;

		// 1회성 스폰 몬스터의 생존 수 (웨이브 클리어 판정용)
		public int ActiveCount => _oneShotAlive;

		protected override void Awake()
		{
			base.Awake();
			EventManager.Instance.Subscribe<UnitDiedEvent>(OnUnitDied);
		}

		protected override void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<UnitDiedEvent>(OnUnitDied);
			base.OnDestroy();
		}

		public void RegisterSpawn(int monsterId, Vector3 center, float radius, int min, int max, float interval)
		{
			SpawnData spawnData = new SpawnData();
			spawnData.monsterId = monsterId;
			spawnData.center = center;
			spawnData.radius = radius;
			spawnData.minCount = min;
			spawnData.maxCount = max;
			spawnData.spawnInterval = interval;
			_spawnList.Add(spawnData);
		}

		public void Register(SpawnData data)
		{
			if (data != null)
			{
				_spawnList.Add(data);
			}
		}

		public void Clear()
		{
			_spawnList.Clear();
			_active.Clear();
			_oneShotAlive = 0;
		}

		// 스테이지 전환/클리어 시 — 분할 소환을 멈추고, 살아있는 몬스터를 사망이 아닌 풀 반환으로 즉시 제거한다.
		// (사망 처리하면 사망 연출/킬카운트가 발생하므로, 잔존 정리에는 직접 풀 반환을 쓴다.)
		public void ClearAlive()
		{
			_spawnList.Clear();

			_clearBuffer.Clear();
			_clearBuffer.AddRange(_active.Values);
			for (int i = 0; i < _clearBuffer.Count; i++)
			{
				UnitFactory.Instance.ReleaseMonster(_clearBuffer[i].monster);
			}

			_clearBuffer.Clear();
			_active.Clear();
			_oneShotAlive = 0;
		}

		// 1회성 소환 — 충원(sustain) 없이 한 마리만 즉시 스폰한다. 웨이브/레이드 모드용.
		// 활성 수는 호출 즉시 증가하므로, 소환이 아직 끝나기 전에도 ActiveCount 에 반영된다.
		public void SpawnOneShot(int monsterId, Vector3 pos)
		{
			if (monsterId <= 0)
			{
				return;
			}

			_oneShotAlive++;
			spawnOneShot(monsterId, pos).Forget();
		}

		private async UniTaskVoid spawnOneShot(int monsterId, Vector3 pos)
		{
			CancellationToken cancellationTokenOnDestroy = this.GetCancellationTokenOnDestroy();
			Monster monster = await UnitFactory.Instance.CreateMonsterAsync(monsterId, pos, Faction.Enemy, cancellationTokenOnDestroy);
			if (monster == null)
			{
				_oneShotAlive--;
				return;
			}

			_active[monster.GetID()] = new ActiveEntry(monster, null);

			// 몬스터는 항상 인디케이터 추가 — 기본은 캐스팅 스킬만 항시 표시(설정 무관), dev 플래그면 전체 스킬도 표시
			if (monster.GetComponent<SkillIndicator>() == null)
			{
				SkillIndicator ind = monster.gameObject.AddComponent<SkillIndicator>();
				ind.SetDevColor(new Color(1f, 0.65f, 0.1f, 0.5f));
				ind.ConfigureForMonster(_showSkillIndicators);
				ind.SetSkills(monster.SkillContainer.GetAll());
			}
		}

		private void Update()
		{
			float deltaTime = Time.deltaTime;
			for (int i = 0; i < _spawnList.Count; i++)
			{
				SpawnData spawnData = _spawnList[i];
				if (spawnData == null || spawnData.monsterId <= 0)
				{
					continue;
				}

				if (spawnData.aliveCount < spawnData.minCount)
				{
					requestSpawns(spawnData, spawnData.minCount - spawnData.aliveCount);
					spawnData.spawnTimer.Reset();
				}
				else if (spawnData.aliveCount < spawnData.maxCount)
				{
					if (spawnData.spawnTimer.Tick(deltaTime, spawnData.spawnInterval) > 0)
					{
						requestSpawns(spawnData, spawnData.maxCount - spawnData.aliveCount);
					}
				}
			}
		}

		private void requestSpawns(SpawnData data, int count)
		{
			for (int i = 0; i < count; i++)
			{
				spawnOne(data).Forget();
			}
		}

		private async UniTaskVoid spawnOne(SpawnData data)
		{
			data.aliveCount++;
			Vector3 pos = data.RandomPosition();
			CancellationToken cancellationTokenOnDestroy = this.GetCancellationTokenOnDestroy();
			Monster monster = await UnitFactory.Instance.CreateMonsterAsync(data.monsterId, pos, Faction.Enemy, cancellationTokenOnDestroy);
			if (monster == null)
			{
				data.aliveCount--;
			}
			else
			{
				_active[monster.GetID()] = new ActiveEntry(monster, data);

				// 몬스터는 항상 인디케이터 추가 — 기본은 캐스팅 스킬만 항시 표시(설정 무관), dev 플래그면 전체 스킬도 표시
				if (monster.GetComponent<SkillIndicator>() == null)
				{
					SkillIndicator ind = monster.gameObject.AddComponent<SkillIndicator>();
					ind.SetDevColor(new Color(1f, 0.65f, 0.1f, 0.5f));
					ind.ConfigureForMonster(_showSkillIndicators);
					ind.SetSkills(monster.SkillContainer.GetAll());
				}
			}
		}

		private void OnUnitDied(UnitDiedEvent e)
		{
			if (e.UnitType == UnitType.Monster && _active.TryGetValue(e.InstanceID, out var value))
			{
				_active.Remove(e.InstanceID);

				// 1회성 스폰(owner == null)은 사망 즉시 생존 수에서 제외 (웨이브 클리어 판정 반영)
				if (value.owner == null)
				{
					_oneShotAlive--;
				}

				returnAfterDelay(value).Forget();
			}
		}

		private async UniTaskVoid returnAfterDelay(ActiveEntry entry)
		{
			CancellationToken cancellationTokenOnDestroy = this.GetCancellationTokenOnDestroy();
			await UniTask.Delay(TimeSpan.FromSeconds(_returnDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, cancellationTokenOnDestroy);
			UnitFactory.Instance.ReleaseMonster(entry.monster);
			if (entry.owner != null)
			{
				entry.owner.aliveCount--;
			}
		}
	}
}
