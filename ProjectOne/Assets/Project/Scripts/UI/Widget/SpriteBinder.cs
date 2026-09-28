using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// Image 한 칸의 스프라이트 로드/해제 상태. 아틀라스에 있으면 동기로 끝내고, 아니면 참조카운트를 걸어 비동기로 받는다.
	//
	// 한 화면에 아이콘 칸이 여럿이면 칸마다 주소를 기억하고 해제해야 해서 같은 관용구가 반복된다 — 칸 하나를 한 벌로 묶었다.
	// 소유자는 파괴 시 Release 를 불러야 한다.
	public sealed class SpriteBinder
	{
		private readonly Image _image;
		private string _requested;	// 마지막으로 요청한 주소 — 늦게 도착한 로드가 덮어쓰지 않게 비교한다
		private string _acquired;	// 참조카운트 해제 대상. 아틀라스에서 꺼낸 경우엔 비어 있다

		public SpriteBinder(Image image)
		{
			_image = image;
		}

		public async UniTask SetAsync(string address, CancellationToken ct)
		{
			if (_image == null || _requested == address)
			{
				return;
			}

			Release();
			_requested = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_image.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_image.sprite = atlasSprite;
				_image.enabled = true;
				return;
			}

			// 프리팹 기본 스프라이트가 잠깐 비치는 것을 막는다.
			_image.enabled = false;

			(bool cancelled, Sprite sprite) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 그 사이 다른 주소가 요청됐으면 방금 받은 것은 곧바로 돌려준다.
			if (_requested != address)
			{
				if (sprite != null && ResourceManager.HasInstance)
				{
					ResourceManager.Instance.Release(address);
				}

				return;
			}

			if (sprite != null)
			{
				_acquired = address;
				_image.sprite = sprite;
				_image.enabled = true;
			}
		}

		public void Release()
		{
			if (string.IsNullOrEmpty(_acquired) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_acquired);
			}

			_acquired = null;
			_requested = null;
		}
	}
}
