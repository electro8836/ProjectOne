using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Unity.Cinemachine;
using ProjectOne.Resources;
using ProjectOne.Summons;
using ProjectOne.Unit;

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

		// 이전 씬에서 남은 유닛을 걷어낸다. **디렉터가 히어로를 새로 스폰하기 전에 불러야 한다.**
		//
		// UnitManager 는 영속이고 컨테이너가 그 자식이라 DontDestroyOnLoad 다 — 씬을 옮겨도 유닛이 살아남는다.
		// 그런데 TownDirector/FieldDirector 는 진입할 때마다 히어로를 새로 만든다.
		// 이걸 부르지 않으면 마을↔필드를 오갈 때마다 히어로가 한 마리씩 쌓인다.
		//
		// 풀 허브를 함께 비우는 이유 — ClearAll 이 Destroy 라서, 풀이 파괴된 인스턴스를 물고 있으면
		// 다음 스폰에서 깨진다. 둘은 반드시 쌍으로 움직인다.
		public static void ClearGameplayUnits()
		{
			if (UnitManager.HasInstance == true)
			{
				UnitManager.Instance.ClearAll();
			}

			if (MonsterSpawnManager.HasInstance == true)
			{
				MonsterSpawnManager.Instance.Clear();
			}

			if (SummonManager.HasInstance == true)
			{
				SummonManager.Instance.ReleaseAll();
			}

			MonsterPoolHub.Instance.Clear();
			SummonPoolHub.Instance.Clear();
		}

		// 이미 떠 있으면 아무것도 하지 않는다. 씬 전환으로 파괴됐으면 다시 띄운다.
		public static async UniTask EnsureCameraAsync(CancellationToken ct)
		{
			if (_cameraRig != null)
			{
				return;
			}

			// 씬 전환으로 파괴된 이전 리그의 Addressable 핸들을 먼저 해제한다.
			// 파괴돼도 핸들은 살아 있어서, 이걸 빠뜨리면 씬을 옮길 때마다 핸들이 하나씩 쌓인다.
			AddressableHelper.ReleaseInstance(_cameraRig);

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
