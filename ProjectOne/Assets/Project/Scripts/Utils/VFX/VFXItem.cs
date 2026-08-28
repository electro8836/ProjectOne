using UnityEngine;

namespace ProjectOne.Utils
{
	// VFXManager 가 풀링하는 VFX 인스턴스.
	// - one-shot : PlayOneShot 후 파티클 종료를 VFXManager 중앙 틱이 감지해 풀로 반환 (per-object Update 없음)
	// - 루프성   : PlayLooping 후 매니저가 Release 할 때까지 유지
	[DisallowMultipleComponent]
	public sealed class VFXItem : MonoBehaviour, IPoolable
	{
		private string _address;
		private ParticleSystem _ps;   // 대표 파티클 (생존 판정·재생·정지 모두 withChildren 으로 처리)

		// 정렬 기준 전환용 — 렌더러와 프리펩 원본 alignment 를 짝으로 들고 있는다.
		private ParticleSystemRenderer[] _renderers;
		private ParticleSystemRenderSpace[] _baseAlignments;
		private bool _aligned;

		public string Address => _address;

		// VFXManager 가 인스턴스 생성 직후 1회 호출
		public void Initialize(string address)
		{
			_address = address;
			if (_ps == null)
			{
				// 루트에 파티클이 있으면 그것을, 없으면 자식에서 첫 파티클을 대표로 사용
				_ps = GetComponent<ParticleSystem>();
				if (_ps == null)
				{
					_ps = GetComponentInChildren<ParticleSystem>(true);
				}
			}

			if (_renderers == null)
			{
				_renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
				_baseAlignments = new ParticleSystemRenderSpace[_renderers.Length];
				for (int i = 0; i < _renderers.Length; i++)
				{
					_baseAlignments[i] = _renderers[i].alignment;
				}
			}
		}

		// 파티클을 트랜스폼 기준으로 정렬할지 — 방향성 VFX 만 켠다.
		//
		// 프리펩 원본은 대개 View(카메라 기준)라 루트에 Z 회전을 줘도 파티클은 그대로 있는다.
		// Local 로 바꾸면 파티클이 트랜스폼 회전을 따라간다.
		// 끌 때 원본으로 되돌리는 이유 — 풀은 주소 단위로 공유되므로 한 번 Local 이 된 인스턴스가
		// 회전을 쓰지 않는 호출에 재사용될 수 있다.
		public void SetAlignedToTransform(bool aligned)
		{
			if (_renderers == null || _aligned == aligned)
			{
				return;
			}

			_aligned = aligned;
			for (int i = 0; i < _renderers.Length; i++)
			{
				_renderers[i].alignment = aligned ? ParticleSystemRenderSpace.Local : _baseAlignments[i];
			}
		}

		// 1회성 재생 — 종료 감지/반환은 VFXManager 중앙 틱이 IsFinished 로 처리
		public void PlayOneShot()
		{
			playParticles();
		}

		// 루프성 재생 — 자동 반환 없음 (매니저 Release 까지 유지)
		public void PlayLooping()
		{
			playParticles();
		}

		// 파티클이 모두 끝났는지 — VFXManager 중앙 틱이 매 프레임 검사
		public bool IsFinished()
		{
			return _ps == null || _ps.IsAlive(true) == false;
		}

		public void OnActivate()
		{
			// 활성화 직후 별도 처리 없음 — 실제 재생은 PlayOneShot/PlayLooping 에서
		}

		public void OnDeactivate()
		{
			if (_ps != null)
			{
				_ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			}
		}

		private void playParticles()
		{
			if (_ps == null)
			{
				return;
			}

			_ps.Clear(true);
			_ps.Play(true);
		}
	}
}
