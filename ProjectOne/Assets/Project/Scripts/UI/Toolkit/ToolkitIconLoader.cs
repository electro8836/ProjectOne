using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// UI Toolkit 요소에 아이콘(스프라이트)을 배경으로 입힌다. 화면 하나가 하나를 들고 쓴다.
	//
	// 아틀라스에 있으면 즉시, 없으면 Addressable 로 로드한다.
	// Addressable 로 받은 것은 주소당 한 번만 Acquire 해 두고 화면이 닫힐 때 ReleaseAll 로 한꺼번에 돌려준다
	// (요소마다 Acquire/Release 짝을 맞추는 대신 화면 단위로 묶는다).
	public sealed class ToolkitIconLoader
	{
		// Addressable 로 Acquire 한 스프라이트. 아틀라스 스프라이트는 refcount 대상이 아니라 담지 않는다.
		private readonly Dictionary<string, Sprite> _loaded = new Dictionary<string, Sprite>();
		private readonly List<string> _loadedAddresses = new List<string>();

		// 요소마다 마지막으로 요청한 주소 — 로드 중 다른 주소로 바뀌었으면 늦게 온 결과를 버린다.
		private readonly Dictionary<VisualElement, string> _requested = new Dictionary<VisualElement, string>();

		public async UniTask SetAsync(VisualElement target, string address, CancellationToken ct)
		{
			_requested[target] = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				target.style.backgroundImage = new StyleBackground(StyleKeyword.None);
				return;
			}

			Sprite sprite = AtlasManager.Instance.Get(address);
			if (sprite == null)
			{
				_loaded.TryGetValue(address, out sprite);
			}

			if (sprite != null)
			{
				target.style.backgroundImage = new StyleBackground(sprite);
				return;
			}

			// 로드 전까지 비워 둔다 — 이전 아이콘이 잠깐 남아 보이는 것을 막는다.
			target.style.backgroundImage = new StyleBackground(StyleKeyword.None);

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true || icon == null)
			{
				return;
			}

			// 같은 주소를 두 요소가 동시에 요청했으면 두 번째 Acquire 는 바로 돌려준다.
			if (_loaded.ContainsKey(address) == true)
			{
				ResourceManager.Instance.Release(address);
			}
			else
			{
				_loaded.Add(address, icon);
				_loadedAddresses.Add(address);
			}

			string current;
			if (_requested.TryGetValue(target, out current) == true && current == address)
			{
				target.style.backgroundImage = new StyleBackground(icon);
			}
		}

		public void ReleaseAll()
		{
			if (ResourceManager.HasInstance == true)
			{
				for (int i = 0; i < _loadedAddresses.Count; i++)
				{
					ResourceManager.Instance.Release(_loadedAddresses[i]);
				}
			}

			_loaded.Clear();
			_loadedAddresses.Clear();
			_requested.Clear();
		}
	}
}
