using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Utils;
using ProjectOne.Event;

namespace ProjectOne.Unit
{
	// 몬스터 스폰 실행기 — "무엇을 어디에" 받아 실제로 만들고, 사망 시 풀로 돌려준다.
	//
	// **누구를 언제 스폰할지는 호출자가 정한다.** 필드는 FieldMonsterSpawner(개체 단위 리젠),
	// 던전은 SpawnGroupRunner(슬롯 배치)가 각자의 규칙으로 소비한다 (몬스터 설계 8장).
	//
	// 구버전의 min/max 충원(sustain) 경로는 설계의 개체 단위 리젠과 모델이 달라 제거했다.
	public class MonsterSpawnManager : MonoSingleton<MonsterSpawnManager>
	{
		// 전투 전용 — 전투씬 수명에만 존재(로비 등으로 따라가지 않음)
		protected override bool Persistent => false;

		[Tooltip("사망 애니메이션 재생 후 풀로 반환하기까지의 지연(초)")]
		[SerializeField]
		private float _returnDelay = 1.5f;

		[Tooltip("개발용: 체크하면 몬스터가 캐스팅 외 전체 스킬도 인디케이터(주황) 표시. 기본(false)은 캐스팅 스킬만 항시 표시")]
		[SerializeField]
		private bool _showSkillIndicators = false;

		private readonly Dictionary<int, Monster> _active = new Dictionary<int, Monster>();

		// ClearAlive 시 Dictionary 순회용 임시 버퍼 (재사용)
		private readonly List<Monster> _clearBuffer = new List<Monster>();

		// 살아있는 몬스터 수 — 웨이브 클리어 판정용. 스폰 요청 즉시 증가한다(비동기 생성 대기 중에도 반영).
		private int _alive;

		public int ActiveCount => _alive;

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

		public void Clear()
		{
			_active.Clear();
			_alive = 0;
		}

		// 스테이지 전환/클리어 시 — 살아있는 몬스터를 사망이 아닌 풀 반환으로 즉시 제거한다.
		// (사망 처리하면 사망 연출/킬카운트/경험치가 발생하므로, 잔존 정리에는 직접 풀 반환을 쓴다.)
		public void ClearAlive()
		{
			_clearBuffer.Clear();
			_clearBuffer.AddRange(_active.Values);
			for (int i = 0; i < _clearBuffer.Count; i++)
			{
				UnitFactory.Instance.ReleaseMonster(_clearBuffer[i]);
			}

			_clearBuffer.Clear();
			_active.Clear();
			_alive = 0;
		}

		// 한 마리 스폰. 위치는 호출자가 정한다 — 필드는 스폰 포인트, 던전은 슬롯.
		// rewardGroupId 는 MonsterSpawn.RewardGroupID(지역 드랍)다. 0 이면 지역 드랍이 없다.
		public void SpawnOneShot(int monsterId, int level, Vector3 pos, int rewardGroupId = 0)
		{
			SpawnOneShotAsync(monsterId, level, pos, rewardGroupId).Forget();
		}

		// 생성된 개체를 돌려주는 형태. 필드 리젠은 어느 슬롯의 몬스터인지 알아야 하므로 이쪽을 쓴다.
		// UnitSpawnedEvent 로 되받으면 동시 스폰 시 요청과 결과가 어긋난다.
		public async UniTask<Monster> SpawnOneShotAsync(int monsterId, int level, Vector3 pos, int rewardGroupId = 0)
		{
			if (monsterId <= 0)
			{
				return null;
			}

			// 활성 수는 요청 즉시 증가한다 — 생성 대기 중에도 웨이브 클리어 판정에 반영되어야 한다.
			_alive++;

			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			Monster monster = await UnitFactory.Instance.CreateMonsterAsync(monsterId, level, pos, Faction.Enemy, ct);
			if (monster == null)
			{
				_alive--;
				return null;
			}

			// 풀 재사용이므로 스폰마다 덮어써야 한다.
			monster.SetSpawnRewardGroup(rewardGroupId);

			_active[monster.GetID()] = monster;

			// 몬스터는 항상 인디케이터 추가 — 기본은 캐스팅 스킬만 항시 표시(설정 무관), dev 플래그면 전체 스킬도 표시
			if (monster.GetComponent<SkillIndicator>() == null)
			{
				SkillIndicator ind = monster.gameObject.AddComponent<SkillIndicator>();
				ind.SetDevColor(new Color(1f, 0.15f, 0.15f, 0.45f));
				ind.ConfigureForMonster(_showSkillIndicators);
				ind.SetSkills(monster.SkillContainer.GetAll());
			}

			return monster;
		}

		private void OnUnitDied(UnitDiedEvent e)
		{
			Monster monster;
			if (e.UnitType != UnitType.Monster || _active.TryGetValue(e.InstanceID, out monster) == false)
			{
				return;
			}

			_active.Remove(e.InstanceID);
			_alive--;

			returnAfterDelay(monster).Forget();
		}

		private async UniTaskVoid returnAfterDelay(Monster monster)
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			await UniTask.Delay(TimeSpan.FromSeconds(_returnDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, ct);
			UnitFactory.Instance.ReleaseMonster(monster);
		}
	}
}
