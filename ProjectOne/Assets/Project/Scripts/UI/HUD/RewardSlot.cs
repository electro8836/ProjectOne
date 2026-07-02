using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 던전 결과창 보상 슬롯 1칸(Prefab_RewardSlot).
	// RewardItem 아이콘/수량을 표시하고, 추가(엑스트라) 스테이지 보상이면 Bonus 오브젝트를 켠다.
	// (슬롯 클릭 팝업은 프리팹에 버튼만 두고 이후 작업.)
	public class RewardSlot : MonoBehaviour
	{
		[SerializeField] private Image _icon;
		[SerializeField] private TMP_Text _countText;
		[SerializeField] private GameObject _bonus;

		// 참조카운트 해제용 아이콘 주소 추적 (아틀라스 스프라이트는 null 로 두어 대상 제외)
		private string _iconAddress;

		public async UniTask BindAsync(int rewardItemId, int count, bool isBonus, CancellationToken ct)
		{
			if (_countText != null)
			{
				_countText.text = count.ToString();
			}

			if (_bonus != null)
			{
				_bonus.SetActive(isBonus);
			}

			Table_RewardItem.Row item = Table_RewardItem.Get(rewardItemId);
			await setIconAsync(item != null ? item.Icon : string.Empty, ct);
		}

		// 아틀라스 우선(동기), 미포함 시 비동기 로드. StageSelectSlot/EquipmentSlot 패턴.
		private async UniTask setIconAsync(string address, CancellationToken ct)
		{
			if (_icon == null || _iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_iconAddress = null;
				_icon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_icon.sprite = atlasSprite;
				_icon.enabled = true;
				_iconAddress = null;   // 아틀라스 스프라이트는 refcount 대상 아님
				return;
			}

			_icon.enabled = false;
			(bool cancelled, Sprite icon) = await ResourceManager.Instance
				.AcquireAsync<Sprite>(address, ct)
				.SuppressCancellationThrow();

			if (cancelled == true || _iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_icon.sprite = icon;
				_icon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance == true)
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
