using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using EDT;
using ProjectOne.Field;
using ProjectOne.Loading;
using ProjectOne.UI;

namespace ProjectOne.Flow
{
	// 필드 상태 — 액트 하나의 필드 그리드맵을 전부 띄워두고 그 안을 돌아다닌다.
	//
	// **클리어 개념이 없다.** 몬스터를 잡으면 일정 시간 후 재스폰되고, 마을 복귀는 UI 로 한다 (맵 설계 1.2).
	// 액트를 옮길 때는 씬을 바꾸지 않고 그리드맵만 통째로 교체한다.
	public class FieldState : IGameState
	{
		public const string SceneName = "4.Field";


		// 진입할 필드 (Map.ID = Field.ID)
		private readonly int _fieldId;

		public FieldState(int fieldId)
		{
			_fieldId = fieldId;
		}

		public async UniTask EnterAsync(CancellationToken ct)
		{
			if (LoadingManager.Instance.IsShowing == false)
			{
				await LoadingManager.Instance.ShowAsync(LoadingFlow.ToField, ct);
			}

			await SceneManager.LoadSceneAsync(SceneName).ToUniTask(Progress.Create<float>(onSceneLoadProgress), cancellationToken: ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 0f);
			FieldDirector director = FieldDirector.EnsureInstance();
			await director.Begin(_fieldId, ct);

			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneReady, 1f);
			await LoadingManager.Instance.HideAsync();
		}

		public UniTask ExitAsync()
		{
			return UniTask.CompletedTask;
		}

		private void onSceneLoadProgress(float ratio)
		{
			LoadingManager.Instance.SetPhaseProgress(LoadingPhase.SceneLoad, ratio);
		}
	}
}
