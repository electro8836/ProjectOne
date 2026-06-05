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

		[Tooltip("개발용: 체크하면 몬스터 스킬 발동 시 범위 인디케이터(주황) 표시")]
		[SerializeField]
		private bool _showSkillIndicators = false;

		[Tooltip("몬스터 id별 스폰 설정 목록 — 일반/엘리트/보스 각각 엔트리 하나씩 등록")]
		[SerializeField]
		private List<SpawnData> _spawnList = new List<SpawnData>();

		private readonly Dictionary<int, ActiveEntry> _active = new Dictionary<int, ActiveEntry>();

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
					spawnData.timer = 0f;
				}
				else if (spawnData.aliveCount < spawnData.maxCount)
				{
					spawnData.timer += deltaTime;
					if (spawnData.timer >= spawnData.spawnInterval)
					{
						requestSpawns(spawnData, spawnData.maxCount - spawnData.aliveCount);
						spawnData.timer = 0f;
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

				if (_showSkillIndicators && monster.GetComponent<SkillIndicator>() == null)
				{
					SkillIndicator ind = monster.gameObject.AddComponent<SkillIndicator>();
					ind.SetDevColor(new Color(1f, 0.65f, 0.1f, 0.5f));
					ind.SetSkills(monster.SkillContainer.GetAll());
				}
			}
		}

		private void OnUnitDied(UnitDiedEvent e)
		{
			if (e.UnitType == UnitType.Monster && _active.TryGetValue(e.InstanceID, out var value))
			{
				_active.Remove(e.InstanceID);
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
