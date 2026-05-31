using UnityEngine;

namespace ProjectOne
{
	/// <summary>
	/// 파티클 시스템이 붙은 프리펩의 전체 크기를 배율 하나로 조정한다.
	/// 자식 ParticleSystem 의 ScalingMode 를 Hierarchy 로 강제해
	/// 루트 transform 의 스케일이 파티클 크기·속도·위치에 모두 반영되도록 한다.
	/// </summary>
	public class ParticleScaler : MonoBehaviour
	{
		[SerializeField] private float _scale = 1f;

		private void Awake()
		{
			ApplyScale();
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			ApplyScale();
		}
#endif

		// 자식 파티클 전체에 Hierarchy 스케일 모드를 적용하고 루트 스케일을 갱신한다.
		private void ApplyScale()
		{
			ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
			for (int i = 0; i < systems.Length; i++)
			{
				ParticleSystem.MainModule main = systems[i].main;
				main.scalingMode = ParticleSystemScalingMode.Hierarchy;
			}

			transform.localScale = Vector3.one * _scale;
		}
	}
}
