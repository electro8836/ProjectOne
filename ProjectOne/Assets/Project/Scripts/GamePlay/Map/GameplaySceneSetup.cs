using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Unity.Cinemachine;
using ProjectOne.Resources;

namespace ProjectOne.Map
{
	// 게임플레이 씬(마을 / 필드 / 던전)이 공통으로 필요로 하는 배치.
	//
	// 씬은 비어 있고 코드가 띄운다는 규칙(맵 설계 8장) 때문에 세 디렉터가 같은 준비를 반복하게 된다.
	// 지금은 카메라 리그 하나뿐이지만, 세 곳에 복사해 두면 한 곳만 고치는 사고가 난다.
	public static class GameplaySceneSetup
	{
		// 씬 종속 가상 카메라. 추적 대상 지정은 CameraManager 가 UnitSpawnedEvent 로 알아서 한다.
		private const string GameplayCameraAddress = "CM_GameplayCam";

		private static GameObject _cameraRig;

		// 이미 떠 있으면 아무것도 하지 않는다. 씬 전환으로 파괴됐으면 다시 띄운다.
		public static async UniTask EnsureCameraAsync(CancellationToken ct)
		{
			if (_cameraRig != null)
			{
				return;
			}

			_cameraRig = await AddressableHelper.TryInstantiateAsync(GameplayCameraAddress, null, false, ct);
			if (_cameraRig == null)
			{
				Debug.LogWarning($"[GameplaySceneSetup] 카메라 리그를 찾지 못했습니다: {GameplayCameraAddress} — 카메라가 히어로를 따라가지 않습니다.");
			}

			ensureCinemachineBrain();
		}

		// vcam 이 계산한 결과는 CinemachineBrain 을 통해서만 실제 Camera 에 전달된다.
		// Brain 이 없으면 CameraManager 가 Follow 대상을 제대로 잡아도 화면은 미동도 하지 않는다.
		//
		// 씬마다 배치하지 않고 여기서 보장하는 이유는 EventSystem 과 같다 —
		// 씬이 여러 개라 하나만 빠뜨려도 그 씬에서 카메라가 죽는다.
		private static void ensureCinemachineBrain()
		{
			Camera main = Camera.main;
			if (main == null)
			{
				Debug.LogWarning("[GameplaySceneSetup] MainCamera 태그가 붙은 카메라가 없습니다 — 카메라 추적이 동작하지 않습니다.");
				return;
			}

			if (main.GetComponent<CinemachineBrain>() != null)
			{
				return;
			}

			main.gameObject.AddComponent<CinemachineBrain>();
		}
	}
}
