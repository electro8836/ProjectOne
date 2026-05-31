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
		private Coroutine _lifeCoroutine;

		// Voice Stealing 판정 시 AudioSourcePool이 비교에 사용
		public float PlayStartTime => _playStartTime;

		private void Awake()
		{
			_audioSource = GetComponent<AudioSource>();
			_audioSource.playOnAwake = false;
		}

		public void Initialize(AudioClip clip, float effectiveVolume, AudioSourcePool pool)
		{
			_audioSource.clip = clip;
			_audioSource.volume = effectiveVolume;
			_ownerPool = pool;
			_isReleased = false;
			_playStartTime = Time.time;
		}

		public void OnActivate()
		{
			_audioSource.Play();
			_lifeCoroutine = StartCoroutine(waitForPlayback());
		}

		public void OnDeactivate()
		{
			if (_lifeCoroutine != null)
			{
				StopCoroutine(_lifeCoroutine);
				_lifeCoroutine = null;
			}

			_audioSource.Stop();
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
