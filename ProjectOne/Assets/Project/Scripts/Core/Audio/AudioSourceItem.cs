using System.Collections;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Audio
{
	// AudioSource를 IPoolable로 래핑한 풀링 대상 MonoBehaviour.
	// Projectile.cs와 동일한 구조로 설계되었다.
	[RequireComponent(typeof(AudioSource))]
	public class AudioSourceItem : MonoBehaviour, IPoolable
	{
		private AudioSource _audioSource;
		private AudioSourcePool _ownerPool;
		private float _playStartTime;
		private bool _isReleased;
		private bool _isLooping;
		private Coroutine _lifeCoroutine;

		// Voice Stealing 판정 시 AudioSourcePool이 비교에 사용
		public float PlayStartTime => _playStartTime;

		// 루프성 SFX(RootSFX 등)는 Voice Stealing 대상에서 제외하기 위해 AudioSourcePool이 확인
		public bool IsLooping => _isLooping;

		private void Awake()
		{
			_audioSource = GetComponent<AudioSource>();
			_audioSource.playOnAwake = false;
			applyTwoDSettings(_audioSource);
		}

		// 위치 기반 3D 처리를 끄고 완전 2D로 고정한다. 인스펙터 설정과 무관하게 항상 동일하게 들리도록 보장.
		private static void applyTwoDSettings(AudioSource source)
		{
			source.spatialBlend = 0f;       // 완전 2D
			source.spatialize = false;      // 스페이셜라이저 플러그인 비활성
			source.dopplerLevel = 0f;       // 도플러 없음
			source.reverbZoneMix = 0f;      // 리버브 존 믹스 차단
			source.bypassReverbZones = true;
		}

		public void Initialize(AudioClip clip, float effectiveVolume, AudioSourcePool pool, bool loop = false)
		{
			_audioSource.clip = clip;
			_audioSource.volume = effectiveVolume;
			_audioSource.loop = loop;
			_ownerPool = pool;
			_isReleased = false;
			_isLooping = loop;
			_playStartTime = Time.time;
		}

		public void OnActivate()
		{
			_audioSource.Play();

			// 루프성은 재생이 끝나지 않으므로 자동 반환 코루틴을 돌리지 않는다. StopLoopSFX 로만 회수.
			if (_isLooping == false)
			{
				_lifeCoroutine = StartCoroutine(waitForPlayback());
			}
		}

		public void OnDeactivate()
		{
			if (_lifeCoroutine != null)
			{
				StopCoroutine(_lifeCoroutine);
				_lifeCoroutine = null;
			}

			_audioSource.Stop();
			_audioSource.loop = false;
			_audioSource.clip = null;
		}

		// PoolBase.Release를 직접 호출하지 않고 AudioSourcePool.ReturnItem을 경유.
		// _activeItems 목록 동기화와 _isReleased 이중 반환 방지를 위해 필수.
		public void ReturnToPool()
		{
			if (_isReleased)
			{
				return;
			}

			_isReleased = true;
			_ownerPool.ReturnItem(this);
		}

		private IEnumerator waitForPlayback()
		{
			yield return new WaitWhile(isPlaying);
			ReturnToPool();
		}

		private bool isPlaying()
		{
			return _audioSource.isPlaying;
		}
	}
}
