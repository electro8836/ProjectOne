using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 스테이지 선택 UI 의 후보 슬롯 1칸. StageInfo_1/2 에 부착.
	// 모드 아이콘/이름/보상 텍스트를 표시하고, 선택됨(Select) 오브젝트를 토글한다.
	public class StageSelectSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;
		[SerializeField] private Image _modeIcon;
		[SerializeField] private TMP_Text _modeNameText;
		[SerializeField] private TMP_Text _rewardText;
		[SerializeField] private GameObject _selectMark;

		// 참조카운트 해제용 아이콘 주소 추적 (아틀라스 스프라이트는 null 로 두어 대상 제외)
		private string _iconAddress;

		public UIButton Button => _button;

		public void SetSelected(bool selected)
		{
			if (_selectMark != null)
			{
				_selectMark.SetActive(selected);
			}
		}

		public void SetTexts(string modeName, string rewardText)
		{
			if (_modeNameText != null)
			{
				_modeNameText.text = modeName;
			}

			if (_rewardText != null)
			{
				_rewardText.text = rewardText;
			}
		}

		// 아틀라스 우선(동기), 미포함 시 비동기 로드. EquipmentSlot 패턴.
		public async UniTask SetIconAsync(string address, CancellationToken ct)
		{
			if (_modeIcon == null || _iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_iconAddress = null;
				_modeIcon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_modeIcon.sprite = atlasSprite;
				_modeIcon.enabled = true;
				_iconAddress = null;   // 아틀라스 스프라이트는 refcount 대상 아님
				return;
			}

			_modeIcon.enabled = false;
			(bool cancelled, Sprite icon) = await ResourceManager.Instance
				.AcquireAsync<Sprite>(address, ct)
				.SuppressCancellationThrow();

			if (cancelled == true)
			{
				return;
			}

			// 로드 완료 사이에 주소가 바뀌었으면 버림
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_modeIcon.sprite = icon;
				_modeIcon.enabled = true;
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
