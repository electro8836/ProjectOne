using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 아이템 정보 팝업의 옵션 1줄. Icon(아이콘) + Text_Title(제목) + Text_Desc(설명)을 채운다.
	// 스탯/스킬/특성 옵션 공용 — 셋 다 아이콘·제목·설명을 모두 가진다.
	public class EquipmentOptionSlot : MonoBehaviour
	{
		[SerializeField] private Image _iconImage;	// Icon
		[SerializeField] private TMP_Text _titleText;	// Text_Title
		[SerializeField] private TMP_Text _valueText;	// Text_Desc

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		// 아이콘 주소·제목·설명을 채운다. 아이콘 로드가 끝난 뒤 반환한다.
		public async UniTask SetAsync(string iconAddress, string title, string desc, CancellationToken ct)
		{
			_titleText.text = title;
			_valueText.text = desc;

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
				_iconImage.sprite = null;
				return;
			}

			// 아틀라스에 있으면 동기로 즉시 세팅(같은 프레임). refcount 대상이 아니므로 _iconAddress 를 비운다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_iconImage.sprite = atlasSprite;
				_iconAddress = null;
				return;
			}

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 Set 되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_iconImage.sprite = icon;
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
