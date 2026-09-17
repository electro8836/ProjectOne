using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 스탯 팝업의 슬롯 1칸(UIPrefab_StatSlot). 스탯 아이콘과 "이름 : 값" 한 줄을 표시한다.
	//
	// 클릭 입력이 없다 — 이 목록은 보기 전용이라 버튼도 이벤트도 두지 않는다 (MasteryInfoSlot 과 같은 이유).
	public class StatSlot : MonoBehaviour
	{
		[Header("내용")]
		[SerializeField] private Image _statIcon;		// StatIcon
		[SerializeField] private TMP_Text _statValue;	// StatValue

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		public UniTask BindAsync(StatSlotData data, CancellationToken ct)
		{
			_statValue.text = data.text;

			return setIcon(data.iconAddress, ct);
		}

		private void OnDestroy()
		{
			releaseIcon();
		}

		// ── 내부: 아이콘 ───────────────────────────────────────────────────

		// MasteryInfoSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
		// StatIcon_* 은 Atlas_Icon 에 포함되어 있어 실제로는 동기 경로로 끝난다.
		private async UniTask setIcon(string address, CancellationToken ct)
		{
			if (_iconAddress == address)
			{
				return;
			}

			releaseIcon();
			_iconAddress = address;

			if (string.IsNullOrEmpty(address) == true)
			{
				_statIcon.sprite = null;
				_statIcon.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_statIcon.sprite = atlasSprite;
				_statIcon.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_statIcon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true)
			{
				return;
			}

			// 로드 중 다른 주소로 다시 바인딩되었으면 덮어쓰지 않는다 (늦은 로드 방지)
			if (_iconAddress != address)
			{
				return;
			}

			if (icon != null)
			{
				_statIcon.sprite = icon;
				_statIcon.enabled = true;
			}
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance)
			{
				ResourceManager.Instance.Release(_iconAddress);
			}

			_iconAddress = null;
		}
	}
}
