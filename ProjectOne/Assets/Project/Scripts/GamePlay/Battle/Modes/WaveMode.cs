using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using EDT;
using ProjectOne.Event;
using ProjectOne.Unit;

namespace ProjectOne.Battle
{
	// 웨이브형 몬스터 모드 — MapInfo.SpawnCount 만큼의 웨이브를 순차 진행한다.
	// 웨이브 N → MapInfo.SpawnInfo_0N → Table_MonsterSpawnInfo 행을 소환.
	// 웨이브 몬스터 전멸 → (마지막이 아니면) 대기 후 다음 웨이브. 마지막 웨이브 클리어 시 승리.
	public sealed class WaveMode : MapBattleModeBase
	{
		// 웨이브 간 대기 시간(초) — 고정. 대기 중 메인HUD에 스킵 버튼이 노출된다.
		private const float WaveWaitSeconds = 30f;

		private bool _skipRequested;

		// 메인HUD 스킵 버튼 → BattleDirector 경유로 호출. 대기 중이면 즉시 다음 웨이브로.
		public void RequestSkipWait()
		{
			_skipRequested = true;
		}

		protected override async UniTaskVoid RunAsync(Table_MapInfo.Row map, CancellationToken ct)
		{
			int totalWaves = map.SpawnCount;
			for (int wave = 1; wave <= totalWaves; wave++)
			{
				int spawnInfoId = GetSpawnInfoId(map, wave);
				Table_MonsterSpawnInfo.Row spawnInfo = Table_MonsterSpawnInfo.Get(spawnInfoId);
				if (spawnInfo == null)
				{
					Debug.LogError($"[WaveMode] SpawnInfo 없음: wave={wave}, id={spawnInfoId}");
					continue;
				}

				EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, false, 0f));

				int total = CountPlannedMonsters(spawnInfo);
				Debug.Log($"[Wave] 웨이브 {wave}/{totalWaves} 시작 — 몬스터 {total}마리");

				await SpawnInfoRunner.SpawnAsync(spawnInfo, MonsterSpawnCenter, MonsterSpawnRadius, ct);

				// 이 웨이브 몬스터가 전멸할 때까지 대기 (처치 진행 로그)
				await WaitWaveClearAsync(wave, total, ct);
				Debug.Log($"[Wave] 웨이브 {wave} 클리어");

				// 마지막 웨이브가 아니면 다음 웨이브까지 대기(스킵 가능)
				if (wave < totalWaves)
				{
					await WaitBetweenWavesAsync(wave, totalWaves, ct);
				}
			}

			_result = BattleResult.Victory;
		}

		// 이 웨이브 몬스터가 전멸할 때까지 폴링하며 처치 수 변화를 로그로 출력
		private async UniTask WaitWaveClearAsync(int wave, int total, CancellationToken ct)
		{
			int lastAlive = -1;
			while (true)
			{
				int alive = MonsterSpawnManager.Instance.ActiveCount;
				if (alive != lastAlive)
				{
					Debug.Log($"[Wave] 웨이브 {wave} 진행 — 처치 {total - alive}/{total} (남은 {alive})");
					lastAlive = alive;
				}

				if (alive <= 0)
				{
					break;
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
		}

		private async UniTask WaitBetweenWavesAsync(int wave, int totalWaves, CancellationToken ct)
		{
			_skipRequested = false;
			EventManager.Instance.Publish(new WaveStateChangedEvent(wave, totalWaves, true, WaveWaitSeconds));
			Debug.Log($"[Wave] 웨이브 {wave} 종료 — 다음 웨이브까지 {WaveWaitSeconds:0}초 대기 (스페이스바: 스킵)");

			float elapsed = 0f;
			int lastShown = -1;
			while (elapsed < WaveWaitSeconds && _skipRequested == false)
			{
				if (IsSkipKeyPressed() == true)
				{
					_skipRequested = true;
					break;
				}

				int remaining = Mathf.CeilToInt(WaveWaitSeconds - elapsed);
				if (remaining != lastShown)
				{
					Debug.Log($"[Wave] 다음 웨이브까지 {remaining}초");
					lastShown = remaining;
				}

				await UniTask.Yield(PlayerLoopTiming.Update, ct);
				elapsed += Time.deltaTime;
			}

			if (_skipRequested == true)
			{
				Debug.Log("[Wave] 대기 스킵 — 다음 웨이브 시작");
			}
		}

		// 스페이스바 스킵 — 새 Input System 기준(구 Input API는 비활성)
		private static bool IsSkipKeyPressed()
		{
			Keyboard kb = Keyboard.current;
			return kb != null && kb.spaceKey.wasPressedThisFrame;
		}

		// 이 SpawnInfo가 소환할 총 몬스터 수 (유효 테이블의 MonsterCount 합)
		private static int CountPlannedMonsters(Table_MonsterSpawnInfo.Row row)
		{
			int total = 0;
			if (row.MonsterID_01 > 0)
			{
				total += row.MonsterCount_01;
			}
			if (row.MonsterID_02 > 0)
			{
				total += row.MonsterCount_02;
			}
			if (row.MonsterID_03 > 0)
			{
				total += row.MonsterCount_03;
			}

			return total;
		}

		// 웨이브 번호(1-based) → MapInfo.SpawnInfo_0N
		private static int GetSpawnInfoId(Table_MapInfo.Row map, int wave)
		{
			switch (wave)
			{
			case 1:
				return map.SpawnInfo_01;
			case 2:
				return map.SpawnInfo_02;
			case 3:
				return map.SpawnInfo_03;
			case 4:
				return map.SpawnInfo_04;
			case 5:
				return map.SpawnInfo_05;
			default:
				return 0;
			}
		}
	}
}
