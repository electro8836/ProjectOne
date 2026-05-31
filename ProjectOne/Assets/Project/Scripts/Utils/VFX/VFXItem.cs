using UnityEngine;

namespace ProjectOne.Utils
{
	// VFXManager 가 풀링하는 VFX 인스턴스.
	// - one-shot : PlayOneShot 후 파티클이 모두 끝나면 스스로 매니저에 반환 (Update 폴링, 코루틴 미사용)
	// - 루프성   : PlayLooping 후 매니저가 Release 할 때까지 유지
	[DisallowMultipleComponent]
	public sealed class VFXItem : MonoBehaviour, IPoolable
	{
		private VFXManager _manager;
		private string _address;
		private ParticleSystem _ps;   // 대표 파티클 (생존 판정·재생·정지 모두 withChildren 으로 처리)
		private bool _autoReturn;     // one-shot 이면 true — Update 에서 종료 감지 후 반환

		public string Address => _address;

		// VFXManager 가 인스턴스 생성 직후 1회 호출
		public void Initialize(VFXManager manager, string address)
		{
			_manager = manager;
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
		}

		// 1회성 재생 — 파티클 종료 시 자동 반환
		public void PlayOneShot()
		{
			playParticles();
			_autoReturn = true;
		}

		// 루프성 재생 — 자동 반환 없음 (매니저 Release 까지 유지)
		public void PlayLooping()
		{
			playParticles();
			_autoReturn = false;
		}

		private void Update()
		{
			if (_autoReturn == false)
			{
				return;
			}

			// 코루틴 대신 파티클 생존 상태를 직접 폴링해 종료를 감지한다.
			if (_ps == null || _ps.IsAlive(true) == false)
			{
				_autoReturn = false;
				if (_manager != null)
				{
					_manager.ReturnToPool(this);
				}
			}
		}

		public void OnActivate()
		{
			// 활성화 직후 별도 처리 없음 — 실제 재생은 PlayOneShot/PlayLooping 에서
		}

		public void OnDeactivate()
		{
			_autoReturn = false;
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
