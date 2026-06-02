using UnityEngine;
using Unity.Cinemachine;

namespace ProjectOne.CameraSystem
{
	// 씬 종속 가상 카메라(CM_GameplayCam)에 붙어, 자기 생명주기에 맞춰
	// 영속 CameraManager에 vcam을 등록/해제하는 얇은 컴포넌트.
	// - Battle 씬 진입(활성화) → SetVcam
	// - Battle 씬 이탈(비활성화/파괴) → ClearVcam
	// 이로써 single-load 씬 전환에도 매니저의 vcam 참조가 끊기지 않는다.
	[RequireComponent(typeof(CinemachineCamera))]
	public sealed class GameplayCameraRig : MonoBehaviour
	{
		private CinemachineCamera _vcam;

		private void Awake()
		{
			_vcam = GetComponent<CinemachineCamera>();
		}

		private void OnEnable()
		{
			CameraManager.Instance.SetVcam(_vcam);
		}

		private void OnDisable()
		{
			CameraManager.Instance.ClearVcam(_vcam);
		}
	}
}
