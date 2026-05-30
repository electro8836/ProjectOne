using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.Audio
{
	// 볼륨 그룹(Master / BGM / SFX)을 나타내는 순수 C# 클래스.
	// 등록된 AudioSource에 볼륨 변경을 즉시 반영한다.
	public class AudioChannel
	{
		private float _volume;
		private readonly List<AudioSource> _registeredSources;

		public float Volume => _volume;

		public AudioChannel(float initialVolume)
		{
			_volume = Mathf.Clamp01(initialVolume);
			_registeredSources = new List<AudioSource>();
		}

		public void SetVolume(float value)
		{
			_volume = Mathf.Clamp01(value);
			for (int i = 0; i < _registeredSources.Count; i++)
			{
				if (_registeredSources[i] != null)
				{
					_registeredSources[i].volume = _volume;
				}
			}
		}

		// BGM AudioSource처럼 이 채널의 볼륨 변화를 직접 받아야 하는 소스를 등록한다.
		// SFX는 Spawn 시점에 effectiveVolume을 계산하므로 등록 불필요.
		public void RegisterSource(AudioSource source)
		{
			if (!_registeredSources.Contains(source))
			{
				_registeredSources.Add(source);
			}
		}

		public void UnregisterSource(AudioSource source)
		{
			for (int i = 0; i < _registeredSources.Count; i++)
			{
				if (_registeredSources[i] == source)
				{
					_registeredSources.RemoveAt(i);
					return;
				}
			}
		}
	}
}
