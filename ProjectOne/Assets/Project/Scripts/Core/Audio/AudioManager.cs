using System.Collections;
using UnityEngine;
using ProjectOne.Utils;

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

    private AudioChannel _masterChannel;
    private AudioChannel _bgmChannel;
    private AudioChannel _sfxChannel;

    private AudioSource _activeBgmSource;
    private AudioSource _inactiveBgmSource;
    private Coroutine _crossfadeCoroutine;

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
  }
}
