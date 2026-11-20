using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using ProjectOne.Field;
using ProjectOne.Loading;
using ProjectOne.Network;
using ProjectOne.Skill;

namespace ProjectOne.Flow
{
	// 월드 상태 — 월드 씬을 띄우고 그 안의 이어진 구역을 돌아다닌다.
	//
	// **클리어 개념이 없다.** 몬스터를 잡으면 일정 시간 후 재스폰되고, 마을 복귀는 UI 로 한다.
	// 월드 씬은 하나다. 월드마다 맵 프리팹(Table_World.MapPrefab)이 따로 있다.
	// 다른 월드로 갈 때도 이 상태로 다시 들어와 씬째 새로 띄운다 — 이전 월드의 상태를 남기지 않는다.
	public class WorldState : IGameState
	{
		public const string SceneName = "4.World";

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

			// 맵을 바꾸기 전에 필드 세션을 교체한다(남은 처치 정산 + 새 시드).
			await FieldSessionRotation.RotateAsync(ct);

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			WorldDirector director = WorldDirector.EnsureInstance();
			await director.Begin(_zoneId, _fromZoneId, ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);
			await LoadingManager.Instance.HideAsync();
		}

		public UniTask ExitAsync()
		{
			// 씬이 내려가기 전에 히어로 쿨타임을 보관한다 — 마을·다른 필드로 가면 이어받고, 던전 입장이면 버린다.
			HeroCooldownCarry.Capture();
			return UniTask.CompletedTask;
		}

		private void onSceneLoadProgress(float ratio)
		{
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, ratio);
		}
	}
}
