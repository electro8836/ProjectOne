using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using EDT;
using ProjectOne.Field;
using ProjectOne.Loading;
using ProjectOne.Network;
using ProjectOne.UI;

namespace ProjectOne.Flow
{
	// 월드 상태 — 월드 씬 하나를 띄우고 그 안의 이어진 구역을 돌아다닌다.
	//
	// **클리어 개념이 없다.** 몬스터를 잡으면 일정 시간 후 재스폰되고, 마을 복귀는 UI 로 한다.
	// 월드마다 씬이 따로 있다(Table_World.SceneName). 다른 월드로 갈 때는 이 상태로 다시 들어온다.
	public class WorldState : IGameState
	{
		// 진입할 구역 (Table_Zone.ID = Map.ID)
		private readonly int _zoneId;

		// 관문으로 넘어왔으면 출발 구역 — 도착 위치를 역방향 관문에서 찾는다. 0 이면 구역 시작 지점.
		private readonly int _fromZoneId;

		public WorldState(int zoneId, int fromZoneId = 0)
		{
			_zoneId = zoneId;
			_fromZoneId = fromZoneId;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			if (LoadingManager.Instance.IsShowing == false)
			{
				await LoadingManager.Instance.ShowAsync(LoadingFlow.ToField, ct);
			}

			string sceneName = GetSceneName(_zoneId);
			if (string.IsNullOrEmpty(sceneName) == true)
			{
				await LoadingManager.Instance.HideAsync();
				return;
			}

			// 맵을 바꾸기 전에 필드 세션을 교체한다(남은 처치 정산 + 새 시드).
			await FieldSessionRotation.RotateAsync(ct);

			await SceneManager.LoadSceneAsync(sceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			WorldDirector director = WorldDirector.EnsureInstance();
			await director.Begin(_zoneId, _fromZoneId, ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);
			await LoadingManager.Instance.HideAsync();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		// 구역이 속한 월드의 씬 이름. 테이블이 비어 있으면 에러를 남기고 null.
		public static string GetSceneName(int zoneId)
		{
			Table_Zone.Row zone = Table_Zone.Get(zoneId);
			if (zone == null)
			{
				Debug.LogError($"[WorldState] Table_Zone.Get({zoneId}) == null");
				return null;
			}

			Table_World.Row world = Table_World.Get(zone.WorldID);
			if (world == null || string.IsNullOrEmpty(world.SceneName) == true)
			{
				Debug.LogError($"[WorldState] 구역 {zoneId} 의 월드 {zone.WorldID} 에 SceneName 이 없습니다.");
				return null;
			}

			return world.SceneName;
		}

		private void onSceneLoadProgress(float ratio)
		{
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, ratio);
		}
	}
}
