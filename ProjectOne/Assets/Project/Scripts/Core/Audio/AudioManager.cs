using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using FMODUnity;
using ProjectOne.Utils;

namespace ProjectOne.Audio
{
	// 오디오 시스템 전역 진입점. 재생은 전부 FMOD Studio 이벤트로 한다.
	//
	// 이벤트 경로 규약
	//   SFX : event:/SFX/<이름>  — 테이블·테마의 SFX 값이 곧 <이름> 이다
	//   BGM : event:/BGM/<이름>
	//
	// 볼륨 그룹(Master/BGM/SFX)은 FMOD 버스(bus:/, bus:/BGM, bus:/SFX)의 볼륨으로 건다.
	// 버스에 걸기 때문에 이미 재생 중인 소리에도 즉시 반영된다.
	public class AudioManager : MonoSingleton<AudioManager>
	{
		private const string SfxEventPrefix = "event:/SFX/";
		private const string BgmEventPrefix = "event:/BGM/";
		private const string MasterBusPath  = "bus:/";
		private const string BgmBusPath     = "bus:/BGM";
		private const string SfxBusPath     = "bus:/SFX";
		private const string MasterBankPath = "bank:/Master";

		[Header("초기 볼륨")]
		[SerializeField, Range(0f, 1f)] private float _initMasterVolume = 1f;
		[SerializeField, Range(0f, 1f)] private float _initBgmVolume    = 1f;
		[SerializeField, Range(0f, 1f)] private float _initSfxVolume    = 1f;

		[Header("EffectSFX Throttle")]
		// 같은 EffectSFX(이름)를 이 시간(초) 윈도우 안에서 최대 _sfxThrottleMaxPerWindow 개까지만 재생.
		// 광역 스킬로 수십~수백 대상이 동시에 피격돼도 사운드가 겹쳐 찢어지지 않게 막는다.
		[SerializeField] private float _sfxThrottleWindow = 0.1f;
		[SerializeField] private int _sfxThrottleMaxPerWindow = 3;

		private float _masterVolume;
		private float _bgmVolume;
		private float _sfxVolume;

		private FMOD.Studio.Bus _masterBus;
		private FMOD.Studio.Bus _bgmBus;
		private FMOD.Studio.Bus _sfxBus;

		// 현재 재생 중인 BGM 인스턴스 (없으면 invalid)
		private FMOD.Studio.EventInstance _bgmInstance;

		// SFX 이름 → 이벤트 설명 캐시. 재생마다 경로 문자열을 만들고 조회하는 비용을 없앤다.
		private readonly Dictionary<string, FMOD.Studio.EventDescription> _sfxDescriptions = new Dictionary<string, FMOD.Studio.EventDescription>();

		// EffectSFX 이름별 throttle 상태 (윈도우 시작 시각 + 윈도우 내 재생 카운트)
		private readonly Dictionary<string, ThrottleEntry> _sfxThrottle = new Dictionary<string, ThrottleEntry>();

		// 이벤트가 없는(잘못된 이름) SFX — 재조회/경고 폭주 방지용 블랙리스트
		private readonly HashSet<string> _failedAddresses = new HashSet<string>();

		private struct ThrottleEntry
		{
			public float WindowStart;
			public int Count;
		}

		protected override void Awake()
		{
			base.Awake();

			loadVolumePrefs();
			applyBusVolumes();
		}

		// ── BGM API ──────────────────────────────────────────────────────

		// 현재 BGM 을 멈추고 새 BGM 을 시작한다.
		// 페이드 길이는 FMOD 이벤트의 AHDSR 로 정한다 — fadeDuration 은 0 이하면 즉시 정지, 아니면 이벤트 설정대로 페이드아웃.
		public void PlayBGM(string name, float fadeDuration = 1f)
		{
			StopBGM(fadeDuration);

			if (string.IsNullOrEmpty(name) == true)
			{
				return;
			}

			FMOD.Studio.EventDescription description;
			if (RuntimeManager.StudioSystem.getEvent(BgmEventPrefix + name, out description) != FMOD.RESULT.OK)
			{
				Debug.LogWarning($"[AudioManager] BGM 이벤트 없음 — name:{name}");
				return;
			}

			description.createInstance(out _bgmInstance);
			_bgmInstance.start();
		}

		// BGM 을 정지한다. fadeDuration 이 0 이하면 즉시, 아니면 이벤트 설정대로 페이드아웃.
		public void StopBGM(float fadeDuration = 1f)
		{
			if (_bgmInstance.isValid() == false)
			{
				return;
			}

			_bgmInstance.stop(toStopMode(fadeDuration));
			_bgmInstance.release();
			_bgmInstance.clearHandle();
		}

		// ── SFX API ──────────────────────────────────────────────────────

		// 이름 기반 oneshot SFX 재생 — SkillSFX 용. 매번 재생(throttle 없음).
		// baseVolume 은 이 재생 한 번의 볼륨이고, SFX·Master 그룹 볼륨은 버스가 곱한다.
		public void PlaySFX(string address, float baseVolume = 1f)
		{
			FMOD.Studio.EventDescription description;
			if (tryGetSfxDescription(address, out description) == false)
			{
				return;
			}

			FMOD.Studio.EventInstance instance;
			description.createInstance(out instance);
			instance.setVolume(baseVolume);
			instance.start();
			// 재생이 끝나면 FMOD 가 알아서 해제한다
			instance.release();
		}

		// 이름 기반 oneshot SFX 재생 — EffectSFX(피격음) 용.
		// 같은 이름이 윈도우 내 상한을 넘으면 무시 → 광역 피격 시 사운드 폭주 방지.
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

		// 이름 기반 루프성 SFX 재생 — RootSFX(버프 지속음) 용. 루프 여부는 FMOD 이벤트 쪽 설정이다.
		// 핸들을 반환하며, 호출자가 StopLoopSFX 로 정지해야 한다.
		public AudioSfxHandle PlayLoopSFX(string address, float baseVolume = 1f)
		{
			AudioSfxHandle handle = new AudioSfxHandle();

			FMOD.Studio.EventDescription description;
			if (tryGetSfxDescription(address, out description) == false)
			{
				handle.Released = true;
				return handle;
			}

			description.createInstance(out handle.Instance);
			handle.Instance.setVolume(baseVolume);
			handle.Instance.start();
			return handle;
		}

		// 루프성 SFX 정지 — 버프 종료 시 호출.
		public void StopLoopSFX(AudioSfxHandle handle)
		{
			if (handle == null || handle.Released == true)
			{
				return;
			}

			handle.Released = true;
			handle.Instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
			handle.Instance.release();
		}

		// 뱅크 로드가 끝날 때까지 기다린 뒤 샘플 데이터를 미리 올린다 — 첫 재생 끊김 방지. (부트 시 1회 호출)
		public async UniTask WaitForBanksAsync(CancellationToken ct = default)
		{
			await UniTask.WaitUntil(haveAllBanksLoaded, PlayerLoopTiming.Update, ct);

			// Awake 시점에 뱅크가 아직 없었다면 버스를 못 잡았을 수 있다 — 여기서 다시 건다.
			applyBusVolumes();

			FMOD.Studio.Bank bank;
			if (RuntimeManager.StudioSystem.getBank(MasterBankPath, out bank) == FMOD.RESULT.OK)
			{
				bank.loadSampleData();
			}
		}

		// ── 볼륨 제어 API ────────────────────────────────────────────────

		public void SetMasterVolume(float value)
		{
			_masterVolume = Mathf.Clamp01(value);
			applyBusVolumes();
			saveVolumePrefs();
		}

		public void SetBGMVolume(float value)
		{
			_bgmVolume = Mathf.Clamp01(value);
			applyBusVolumes();
			saveVolumePrefs();
		}

		public void SetSFXVolume(float value)
		{
			_sfxVolume = Mathf.Clamp01(value);
			applyBusVolumes();
			saveVolumePrefs();
		}

		public float GetMasterVolume() => _masterVolume;
		public float GetBGMVolume()    => _bgmVolume;
		public float GetSFXVolume()    => _sfxVolume;

		// ── 내부 유틸 ────────────────────────────────────────────────────

		private static bool haveAllBanksLoaded()
		{
			return RuntimeManager.HaveAllBanksLoaded;
		}

		private static FMOD.Studio.STOP_MODE toStopMode(float fadeDuration)
		{
			if (fadeDuration <= 0f)
			{
				return FMOD.Studio.STOP_MODE.IMMEDIATE;
			}

			return FMOD.Studio.STOP_MODE.ALLOWFADEOUT;
		}

		// 버스는 뱅크가 로드된 뒤에만 잡힌다. 아직 못 잡은 버스는 건너뛰고 다음 호출에서 다시 시도한다.
		private void applyBusVolumes()
		{
			applyBusVolume(MasterBusPath, ref _masterBus, _masterVolume);
			applyBusVolume(BgmBusPath,    ref _bgmBus,    _bgmVolume);
			applyBusVolume(SfxBusPath,    ref _sfxBus,    _sfxVolume);
		}

		private static void applyBusVolume(string path, ref FMOD.Studio.Bus bus, float volume)
		{
			if (bus.isValid() == false && RuntimeManager.StudioSystem.getBus(path, out bus) != FMOD.RESULT.OK)
			{
				return;
			}

			bus.setVolume(volume);
		}

		private void saveVolumePrefs()
		{
			PlayerPrefs.SetFloat("MasterVolume", _masterVolume);
			PlayerPrefs.SetFloat("BGMVolume",    _bgmVolume);
			PlayerPrefs.SetFloat("SFXVolume",    _sfxVolume);
			PlayerPrefs.Save();
		}

		private void loadVolumePrefs()
		{
			_masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("MasterVolume", _initMasterVolume));
			_bgmVolume    = Mathf.Clamp01(PlayerPrefs.GetFloat("BGMVolume",    _initBgmVolume));
			_sfxVolume    = Mathf.Clamp01(PlayerPrefs.GetFloat("SFXVolume",    _initSfxVolume));
		}

		// ── SFX 내부 (이벤트 조회 / throttle) ────────────────────────────

		// 이름의 SFX 이벤트 설명을 캐시에서 반환하거나, 없으면 FMOD 에서 조회해 캐시한다.
		// 이벤트가 없으면 1회만 경고하고 이후로는 조용히 실패한다.
		private bool tryGetSfxDescription(string address, out FMOD.Studio.EventDescription description)
		{
			description = default;
			if (string.IsNullOrEmpty(address) == true)
			{
				return false;
			}

			if (_sfxDescriptions.TryGetValue(address, out description) == true)
			{
				return true;
			}

			if (_failedAddresses.Contains(address) == true)
			{
				return false;
			}

			if (RuntimeManager.StudioSystem.getEvent(SfxEventPrefix + address, out description) != FMOD.RESULT.OK)
			{
				_failedAddresses.Add(address);
				Debug.LogWarning($"[AudioManager] SFX 이벤트 없음 — name:{address}");
				return false;
			}

			_sfxDescriptions.Add(address, description);
			return true;
		}

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

		// ── 정리 ──────────────────────────────────────────────────────────

		// 로비 전환 — 재생 중인 SFX(루프 포함)를 전부 정지. BGM 은 유지.
		public void Clear()
		{
			if (_sfxBus.isValid() == true)
			{
				_sfxBus.stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
			}

			_sfxThrottle.Clear();
		}

		// 종료 시에는 FMOD 를 건드리지 않는다 — RuntimeManager 가 먼저 내려갔을 수 있고,
		// 그 상태에서 RuntimeManager 에 접근하면 인스턴스를 다시 만들려 한다.
		protected override void OnDestroy()
		{
			_sfxDescriptions.Clear();
			_sfxThrottle.Clear();
			_failedAddresses.Clear();
			base.OnDestroy();
		}
	}
}
