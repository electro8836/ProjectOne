using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using ProjectOne.Utils;

namespace ProjectOne.Resources
{
	// 아웃게임 UI 아이콘 SpriteAtlas 캐시.
	// 부트에서 아틀라스를 로드해 상주시키고, 슬롯은 이름(=주소=파일명)으로 스프라이트를 동기 조회한다.
	// 개별 아이콘을 어드레서블로 따로 로드하지 않으므로, 화면 열 때 로드 대기/깜빡임 없이 같은 프레임에 표시된다.
	public class IconAtlasCache : Singleton<IconAtlasCache>
	{
		private readonly List<SpriteAtlas> _atlases = new List<SpriteAtlas>();

		// 이름 → 스프라이트. 미포함 이름은 null 로 캐시해 GetSprite 재호출을 막는다.
		private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

		protected IconAtlasCache() { }

		// 부트 시 1회 — 지정한 아틀라스들을 로드해 상주시킨다(핸들 수명은 ResourceManager 캐시가 소유).
		public async UniTask LoadAsync(string[] atlasAddresses, CancellationToken ct = default)
		{
			for (int i = 0; i < atlasAddresses.Length; i++)
			{
				SpriteAtlas atlas = await ResourceManager.Instance.AcquireAsync<SpriteAtlas>(atlasAddresses[i], ct);
				if (atlas != null && _atlases.Contains(atlas) == false)
				{
					_atlases.Add(atlas);
				}
			}
		}

		// 이름으로 스프라이트를 동기 조회한다. 어느 아틀라스에도 없으면 null(호출자가 비동기 폴백).
		public Sprite Get(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}

			if (_sprites.TryGetValue(name, out Sprite cached))
			{
				return cached;
			}

			Sprite sprite = null;
			for (int i = 0; i < _atlases.Count; i++)
			{
				sprite = _atlases[i].GetSprite(name);
				if (sprite != null)
				{
					break;
				}
			}

			_sprites[name] = sprite;
			return sprite;
		}
	}
}
