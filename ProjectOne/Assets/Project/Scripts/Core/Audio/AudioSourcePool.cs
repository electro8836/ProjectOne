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
		// loop=true 면 루프성 SFX(RootSFX)로 재생되며 Voice Stealing 대상에서 제외된다.
		public AudioSourceItem Spawn(AudioClip clip, float effectiveVolume, bool loop = false)
		{
			if (_activeItems.Count >= _maxVoices)
			{
				stealOldestVoice();
			}

			AudioSourceItem item = GetFromPool();
			item.Initialize(clip, effectiveVolume, this, loop);
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

		// 재생 중 SFX 전부 정지·회수 후 풀을 초기 개수로 리셋 (로비 전환 시)
		public void ResetAll()
		{
			for (int i = _activeItems.Count - 1; i >= 0; i--)
			{
				AudioSourceItem item = _activeItems[i];
				if (item != null)
				{
					item.ReturnToPool();
				}
			}

			_activeItems.Clear();
			ResetPool();
		}

		private void stealOldestVoice()
		{
			// 루프성(RootSFX)은 강제 종료 대상에서 제외 — oneshot 중 가장 오래된 것만 뺏는다.
			AudioSourceItem oldest = null;
			for (int i = 0; i < _activeItems.Count; i++)
			{
				if (_activeItems[i].IsLooping == true)
				{
					continue;
				}

				if (oldest == null || _activeItems[i].PlayStartTime < oldest.PlayStartTime)
				{
					oldest = _activeItems[i];
				}
			}

			// oneshot 이 하나도 없으면(전부 루프) 뺏지 않는다 — 신규 재생분은 풀 초과 생성으로 허용
			if (oldest == null)
			{
				return;
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
