using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Audio
{
	// SFX AudioSource 풀. PoolBase<T>를 상속하며 Voice Stealing 로직을 포함한다.
	public class AudioSourcePool : PoolBase<AudioSourceItem>
	{
		[SerializeField] private AudioSourceItem _prefab;
		// 동시에 재생 가능한 최대 SFX 수. 초과 시 가장 오래된 소리를 강제 종료한다.
		[SerializeField] private int _maxVoices = 16;

		private readonly List<AudioSourceItem> _activeItems = new List<AudioSourceItem>();

		protected override AudioSourceItem CreateItem()
		{
			return Instantiate(_prefab, transform);
		}

		// 외부 API. AudioManager.PlaySFX()가 호출한다.
		public AudioSourceItem Spawn(AudioClip clip, float effectiveVolume)
		{
			if (_activeItems.Count >= _maxVoices)
			{
				stealOldestVoice();
			}

			AudioSourceItem item = GetFromPool();
			item.Initialize(clip, effectiveVolume, this);
			item.OnActivate();
			_activeItems.Add(item);
			return item;
		}

		// AudioSourceItem.ReturnToPool()이 PoolBase.Release 대신 이 메서드를 통해 반환.
		// _activeItems 동기화를 보장하기 위해 반드시 이 경로를 사용해야 한다.
		public void ReturnItem(AudioSourceItem item)
		{
			removeFromActiveList(item);
			Release(item);
		}

		private void stealOldestVoice()
		{
			if (_activeItems.Count == 0)
			{
				return;
			}

			AudioSourceItem oldest = _activeItems[0];
			for (int i = 1; i < _activeItems.Count; i++)
			{
				if (_activeItems[i].PlayStartTime < oldest.PlayStartTime)
				{
					oldest = _activeItems[i];
				}
			}
			// ReturnToPool 경유하여 _isReleased 플래그와 _activeItems 동기화
			oldest.ReturnToPool();
		}

		private void removeFromActiveList(AudioSourceItem item)
		{
			for (int i = 0; i < _activeItems.Count; i++)
			{
				if (_activeItems[i] == item)
				{
					_activeItems.RemoveAt(i);
					return;
				}
			}
		}
	}
}
