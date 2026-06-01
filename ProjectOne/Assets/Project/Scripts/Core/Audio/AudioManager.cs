using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ProjectOne.Utils;
using ProjectOne.Resources;

namespace ProjectOne.Audio
{
	// 오디오 시스템 전역 진입점.
	// BGM 크로스페이드, SFX 풀링, 볼륨 그룹(Master/BGM/SFX) 제어를 담당한다.
	public class AudioManager : MonoSingleton<AudioManager>
	{
		[Header("BGM")]
		[SerializeField] private AudioSource _bgmSourceA;
		[SerializeField] private AudioSource _bgmSourceB;

		[Header("SFX Pool")]
		[SerializeField] private AudioSourcePool _sfxPool;

		[Header("초기 볼륨")]
		[SerializeField, Range(0f, 1f)] private float _initMasterVolume = 1f;
		[SerializeField, Range(0f, 1f)] private float _initBgmVolume    = 1f;
		[SerializeField, Range(0f, 1f)] private float _initSfxVolume    = 1f;

		[Header("EffectSFX Throttle")]
		// 같은 EffectSFX(address)를 이 시간(초) 윈도우 안에서 최대 _sfxThrottleMaxPerWindow 개까지만 재생.
		// 광역 스킬로 수십~수백 대상이 동시에 피격돼도 사운드가 겹쳐 찢어지지 않게 막는다.
		[SerializeField] private float _sfxThrottleWindow = 0.1f;
		[SerializeField] private int _sfxThrottleMaxPerWindow = 3;

		private AudioChannel _masterChannel;
		private AudioChannel _bgmChannel;
		private AudioChannel _sfxChannel;

		private AudioSource _activeBgmSource;
		private AudioSource _inactiveBgmSource;
		private Coroutine _crossfadeCoroutine;

		// address → AudioClip 캐시. ResourceManager 로 Acquire, 매니저 수명 동안 유지 (VFXManager 와 동일 패턴)
		private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

		// EffectSFX address 별 throttle 상태 (윈도우 시작 시각 + 윈도우 내 재생 카운트)
		private readonly Dictionary<string, ThrottleEntry> _sfxThrottle = new Dictionary<string, ThrottleEntry>();

		// 로드에 실패한(잘못된 Addressable 키) address — 재시도/경고 폭주 방지용 블랙리스트
		private readonly HashSet<string> _failedAddresses = new HashSet<string>();

		// 라벨로 일괄 프리로드한 SFX 핸들 — OnDestroy 에서 ReleasePreloaded 로 반환
		private IList<AudioClip> _preloadedSfx;

		// 첫 클립 로드 때 캐시 (OnDestroy 에서 Release, Instance 게터 재생성 회피)
		private ResourceManager _resourceManager;

		// 매니저 파괴 진행 여부 — 비동기 콜백이 파괴 후 도착하는 경우 가드
		private bool _isQuitting;

		private struct ThrottleEntry
		{
			public float WindowStart;
			public int Count;
		}

		// BGM 실효 볼륨 = BGM채널 × Master채널
		private float effectiveBgmVolume => _bgmChannel.Volume * _masterChannel.Volume;

		protected override void Awake()
		{
			base.Awake();

			_masterChannel = new AudioChannel(_initMasterVolume);
			_bgmChannel    = new AudioChannel(_initBgmVolume);
			_sfxChannel    = new AudioChannel(_initSfxVolume);

			_activeBgmSource   = _bgmSourceA;
			_inactiveBgmSource = _bgmSourceB;

			// 인스펙터 설정과 무관하게 BGM 소스를 완전 2D로 고정 (위치 무관 동일 청취)
			applyTwoDSettings(_bgmSourceA);
			applyTwoDSettings(_bgmSourceB);

			// BGM 소스는 채널에 등록하여 볼륨 변경 시 즉시 반영
			_bgmChannel.RegisterSource(_bgmSourceA);
			_bgmChannel.RegisterSource(_bgmSourceB);

			loadVolumePrefs();
		}

		// ── BGM API ──────────────────────────────────────────────────────

		// 현재 BGM을 새 클립으로 크로스페이드한다. 페이드 중 재호출 시 현재 볼륨에서 이어받아 전환.
		public void PlayBGM(AudioClip clip, float fadeDuration = 1f)
		{
			if (_crossfadeCoroutine != null)
			{
				StopCoroutine(_crossfadeCoroutine);
			}

			_inactiveBgmSource.clip = clip;
			_inactiveBgmSource.volume = 0f;
			_inactiveBgmSource.Play();

			_crossfadeCoroutine = StartCoroutine(crossfadeRoutine(fadeDuration));
		}

		// BGM을 서서히 정지한다.
		public void StopBGM(float fadeDuration = 1f)
		{
			if (_crossfadeCoroutine != null)
			{
				StopCoroutine(_crossfadeCoroutine);
			}

			_crossfadeCoroutine = StartCoroutine(fadeOutRoutine(fadeDuration));
		}

		// ── SFX API ──────────────────────────────────────────────────────

		// SFX를 재생한다. baseVolume에 SFX·Master 볼륨을 곱해 최종 볼륨을 결정한다.
		public void PlaySFX(AudioClip clip, float baseVolume = 1f)
		{
			float effective = baseVolume * _sfxChannel.Volume * _masterChannel.Volume;
			_sfxPool.Spawn(clip, effective);
		}

		// address(Addressable) 기반 oneshot SFX 재생 — SkillSFX 용. 매번 재생(throttle 없음).
		// 캐시 적중이면 동기 재생, 최초 로드 필요할 때만 비동기.
		public void PlaySFX(string address, float baseVolume = 1f)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			float effective = baseVolume * _sfxChannel.Volume * _masterChannel.Volume;
			AudioClip clip;
			if (_clipCache.TryGetValue(address, out clip) == true)
			{
				if (clip != null)
				{
					_sfxPool.Spawn(clip, effective);
				}

				return;
			}

			playSfxAsync(address, effective).Forget();
		}

		// address 기반 oneshot SFX 재생 — EffectSFX(피격음) 용.
		// 같은 address 가 윈도우 내 상한을 넘으면 무시 → 광역 피격 시 사운드 폭주 방지.
		public void PlaySFXThrottled(string address, float baseVolume = 1f)
		{
			if (string.IsNullOrEmpty(address) == true)
			{
				return;
			}

			if (passThrottle(address) == false)
			{
				return;
			}

			PlaySFX(address, baseVolume);
		}

		// address 기반 루프성 SFX 재생 — RootSFX(버프 지속음) 용.
		// 핸들을 반환하며, 호출자가 StopLoopSFX 로 정지해야 한다.
		public AudioSfxHandle PlayLoopSFX(string address, float baseVolume = 1f)
		{
			AudioSfxHandle handle = new AudioSfxHandle(address);
			if (string.IsNullOrEmpty(address) == true)
			{
				handle.Released = true;
				return handle;
			}

			float effective = baseVolume * _sfxChannel.Volume * _masterChannel.Volume;
			AudioClip clip;
			if (_clipCache.TryGetValue(address, out clip) == true)
			{
				if (clip != null)
				{
					activateLoop(handle, clip, effective);
				}
				else
				{
					handle.Released = true;
				}

				return handle;
			}

			playLoopAsync(handle, effective).Forget();
			return handle;
		}

		// 루프성 SFX 정지 — 버프 종료 시 호출. 로드 대기 중이면 완료 콜백이 Released 를 보고 즉시 회수.
		public void StopLoopSFX(AudioSfxHandle handle)
		{
			if (handle == null || handle.Released == true)
			{
				return;
			}

			handle.Released = true;
			if (handle.Item != null)
			{
				handle.Item.ReturnToPool();
				handle.Item = null;
			}
		}

		// 라벨로 묶인 SFX 클립을 일괄 프리로드해 _clipCache 에 적재한다.
		// 주소=파일명 컨벤션이므로 이후 PlaySFX(파일명)가 비동기 로드 없이 동기 캐시 히트로 재생된다.
		// (부트 시 1회 호출 — TableLoader 의 "Tables" 라벨 프리로드와 동일 패턴)
		public async UniTask PreloadSFXByLabelAsync(string label = "SFX", CancellationToken ct = default)
		{
			if (_resourceManager == null)
			{
				_resourceManager = ResourceManager.Instance;
			}

			_preloadedSfx = await _resourceManager.PreloadByLabelAsync<AudioClip>(label, null, ct);
			for (int i = 0; i < _preloadedSfx.Count; i++)
			{
				AudioClip clip = _preloadedSfx[i];
				if (clip != null)
				{
					_clipCache[clip.name] = clip;
				}
			}
		}

		// ── 볼륨 제어 API ────────────────────────────────────────────────

		public void SetMasterVolume(float value)
		{
			_masterChannel.SetVolume(value);
			updateBgmSourceVolumes();
			saveVolumePrefs();
		}

		public void SetBGMVolume(float value)
		{
			_bgmChannel.SetVolume(value);
			updateBgmSourceVolumes();
			saveVolumePrefs();
		}

		// 기재생 SFX는 그대로 유지. 신규 재생부터 새 볼륨이 적용된다.
		public void SetSFXVolume(float value)
		{
			_sfxChannel.SetVolume(value);
			saveVolumePrefs();
		}

		public float GetMasterVolume() => _masterChannel.Volume;
		public float GetBGMVolume()    => _bgmChannel.Volume;
		public float GetSFXVolume()    => _sfxChannel.Volume;

		// ── 내부 코루틴 ──────────────────────────────────────────────────

		private IEnumerator crossfadeRoutine(float duration)
		{
			float startVolA = _activeBgmSource.volume;
			float startVolB = _inactiveBgmSource.volume;
			float elapsed   = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				float ebv = effectiveBgmVolume;
				_activeBgmSource.volume   = Mathf.Lerp(startVolA, 0f, t) * ebv;
				_inactiveBgmSource.volume = Mathf.Lerp(startVolB, 1f, t) * ebv;
				yield return null;
			}

			_activeBgmSource.Stop();
			swapBgmSources();
			_crossfadeCoroutine = null;
		}

		private IEnumerator fadeOutRoutine(float duration)
		{
			float startVol = _activeBgmSource.volume;
			float elapsed  = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				_activeBgmSource.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / duration));
				yield return null;
			}

			_activeBgmSource.Stop();
			_crossfadeCoroutine = null;
		}

		// ── 내부 유틸 ────────────────────────────────────────────────────

		// 위치 기반 3D 처리를 끄고 완전 2D로 고정한다. 인스펙터 설정과 무관하게 항상 동일하게 들리도록 보장.
		private static void applyTwoDSettings(AudioSource source)
		{
			source.spatialBlend = 0f;       // 완전 2D
			source.spatialize = false;      // 스페이셜라이저 플러그인 비활성
			source.dopplerLevel = 0f;       // 도플러 없음
			source.reverbZoneMix = 0f;      // 리버브 존 믹스 차단
			source.bypassReverbZones = true;
		}

		private void swapBgmSources()
		{
			AudioSource temp  = _activeBgmSource;
			_activeBgmSource   = _inactiveBgmSource;
			_inactiveBgmSource = temp;
		}

		// 크로스페이드 중이 아닐 때만 직접 갱신한다. 코루틴이 매 프레임 처리하기 때문.
		private void updateBgmSourceVolumes()
		{
			if (_crossfadeCoroutine != null)
			{
				return;
			}

			_activeBgmSource.volume   = effectiveBgmVolume;
			_inactiveBgmSource.volume = 0f;
		}

		private void saveVolumePrefs()
		{
			PlayerPrefs.SetFloat("MasterVolume", _masterChannel.Volume);
			PlayerPrefs.SetFloat("BGMVolume",    _bgmChannel.Volume);
			PlayerPrefs.SetFloat("SFXVolume",    _sfxChannel.Volume);
			PlayerPrefs.Save();
		}

		private void loadVolumePrefs()
		{
			float master = PlayerPrefs.GetFloat("MasterVolume", _initMasterVolume);
			float bgm    = PlayerPrefs.GetFloat("BGMVolume",    _initBgmVolume);
			float sfx    = PlayerPrefs.GetFloat("SFXVolume",    _initSfxVolume);

			_masterChannel.SetVolume(master);
			_bgmChannel.SetVolume(bgm);
			_sfxChannel.SetVolume(sfx);
		}

		// ── SFX 내부 (throttle / 비동기 로드) ────────────────────────────

		// 윈도우가 지났으면 리셋 후 통과, 윈도우 내 상한 미만이면 카운트 증가 후 통과, 상한 도달이면 차단.
		private bool passThrottle(string address)
		{
			float now = Time.time;
			ThrottleEntry entry;
			if (_sfxThrottle.TryGetValue(address, out entry) == false
				|| now - entry.WindowStart >= _sfxThrottleWindow)
			{
				entry.WindowStart = now;
				entry.Count = 1;
				_sfxThrottle[address] = entry;
				return true;
			}

			if (entry.Count >= _sfxThrottleMaxPerWindow)
			{
				return false;
			}

			entry.Count++;
			_sfxThrottle[address] = entry;
			return true;
		}

		// 루프성 활성: 핸들이 살아있으면 풀에서 loop 아이템을 스폰해 연결
		private void activateLoop(AudioSfxHandle handle, AudioClip clip, float effectiveVolume)
		{
			// 로드 대기 중 StopLoopSFX 됐거나, 종료 정리로 풀이 파괴됐으면 스폰하지 않음
			if (handle.Released == true || _sfxPool == null)
			{
				return;
			}

			handle.Item = _sfxPool.Spawn(clip, effectiveVolume, true);
		}

		private async UniTask playSfxAsync(string address, float effectiveVolume)
		{
			AudioClip clip = await loadClipAsync(address);
			// 종료 정리로 풀이 파괴된 경우(_isQuitting 세팅 전 재개 포함)도 가드
			if (clip == null || _isQuitting == true || _sfxPool == null)
			{
				return;
			}

			_sfxPool.Spawn(clip, effectiveVolume);
		}

		private async UniTask playLoopAsync(AudioSfxHandle handle, float effectiveVolume)
		{
			AudioClip clip = await loadClipAsync(handle.Address);
			if (clip == null || _isQuitting == true)
			{
				handle.Released = true;
				return;
			}

			activateLoop(handle, clip, effectiveVolume);
		}

		// address 의 AudioClip 을 캐시에서 반환하거나, 없으면 ResourceManager 로 로드 후 캐시
		private async UniTask<AudioClip> loadClipAsync(string address)
		{
			AudioClip cached;
			if (_clipCache.TryGetValue(address, out cached) == true)
			{
				return cached;
			}

			// 이전에 로드 실패한 잘못된 키는 재시도하지 않음
			if (_failedAddresses.Contains(address) == true)
			{
				return null;
			}

			if (_resourceManager == null)
			{
				_resourceManager = ResourceManager.Instance;
			}

			AudioClip clip;
			try
			{
				clip = await _resourceManager.AcquireAsync<AudioClip>(address);
			}
			catch (System.Exception e)
			{
				// 반드시 여기서 예외를 관측(observe)한다.
				// 방치하면 .Forget() 비동기의 미관측 예외가 UniTask 파이널라이저(백그라운드 스레드)에서
				// Debug.LogException 으로 로깅되며 Unity 네이티브 힙을 손상시켜 종료 시 크래시한다.
				_failedAddresses.Add(address);
				Debug.LogWarning($"[AudioManager] SFX 로드 실패 — address:{address} ({e.Message})");
				return null;
			}

			if (clip == null)
			{
				return null;
			}

			// 동시 첫 로드로 다른 호출이 먼저 등록했으면 중복 Acquire 분을 되돌림 (refCount 보정)
			if (_clipCache.ContainsKey(address) == false)
			{
				_clipCache.Add(address, clip);
			}
			else
			{
				_resourceManager.Release(address);
			}

			return clip;
		}

		// ── 정리 ──────────────────────────────────────────────────────────

		protected override void OnDestroy()
		{
			_isQuitting = true;

			// 라벨 프리로드분은 ReleasePreloaded 로 일괄 반환 (lazy 로드분과 소유 경로가 분리됨)
			// 종료 중 ResourceManager 재생성을 피하려 캐시된 _resourceManager 사용
			if (_preloadedSfx != null && _resourceManager != null)
			{
				_resourceManager.ReleasePreloaded(_preloadedSfx);
				_preloadedSfx = null;
			}

			// 캐시한 클립 주소마다 refCount 반환 (앱 종료 시엔 ResourceManager 가 이미 파괴됐을 수 있어 null 가드)
			if (_resourceManager != null)
			{
				List<string> keys = new List<string>(_clipCache.Keys);
				for (int i = 0; i < keys.Count; i++)
				{
					_resourceManager.Release(keys[i]);
				}
			}

			_clipCache.Clear();
			_sfxThrottle.Clear();
			_failedAddresses.Clear();
			base.OnDestroy();
		}
	}
}
