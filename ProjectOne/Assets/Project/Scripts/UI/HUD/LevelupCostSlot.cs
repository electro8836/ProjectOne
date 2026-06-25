using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 캐릭터 디테일 팝업의 레벨업 비용 1칸(Req). 아이콘/비용 텍스트(보유/필요)를 자체 관리한다.
	public class LevelupCostSlot : MonoBehaviour
	{
		[SerializeField] private Image _icon;		// Icon
		[SerializeField] private TMP_Text _cost;	// CostText (보유/필요, 색상 포함)

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		public async UniTask Bind(string iconAddress, string cost, CancellationToken ct)
		{
			_cost.text = cost;
			await setIcon(iconAddress, ct);
		}

		// 아이콘 주소가 바뀐 경우에만 이전 것을 해제하고 새로 로드한다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address))
			{
				_icon.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = IconAtlasCache.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_iconAddress = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 Bind 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
			}
		}

		private void releaseIcon()
		{
			if (!string.IsNullOrEmpty(_iconAddress) && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		private void OnDestroy()
		{
			releaseIcon();
		}
	}
}
