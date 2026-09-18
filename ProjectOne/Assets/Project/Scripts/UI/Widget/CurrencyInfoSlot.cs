using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 재화 목록 팝업의 슬롯 1칸(UIPrefab_CurrencyInfoSlot).
	//
	// 상단 HUD 의 CurrencySlot 과 달리 표시할 재화가 인스펙터에 박혀 있지 않다 —
	// 어떤 재화를 몇 개 그릴지는 Presenter 가 정해서 BindAsync 로 넘겨준다.
	// 재화 변동 구독도 슬롯이 아니라 팝업 Presenter 가 소유한다(슬롯은 그리기만).
	public class CurrencyInfoSlot : MonoBehaviour
	{
		[SerializeField] private UIButton _button;		// 루트 — 설명 툴팁을 여는 클릭 영역
		[SerializeField] private Image _iconImage;		// Icon
		[SerializeField] private TMP_Text _countText;	// Count

		public event Action<CurrencyInfoSlot> OnClicked;

		private EDT.Currency _currency;
		private string _iconAddress;

		public EDT.Currency TargetCurrency
		{
			get { return _currency; }
		}

		private void Awake()
		{
			_button.OnClickEvent += onClicked;
		}

		private void OnDestroy()
		{
			_button.OnClickEvent -= onClicked;

			releaseIcon();
		}

		public UniTask BindAsync(CurrencySlotData data, CancellationToken ct)
		{
			_currency = data.currency;
			SetAmount(data.amount);

			return setIcon(data.iconAddress, ct);
		}

		// 재화가 변동했을 때 아이콘은 그대로 두고 수량만 갈아 끼운다.
		public void SetAmount(int amount)
		{
			_countText.text = CurrencySlot.FormatAmount(amount);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onClicked()
		{
			if (OnClicked != null)
			{
				OnClicked.Invoke(this);
			}
		}

		// PetInfoSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
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
				_iconImage.sprite = null;
				_iconImage.enabled = false;
				return;
			}

			// 아틀라스 스프라이트는 refcount 대상이 아니므로 _iconAddress 를 비워 releaseIcon 오작동을 막는다.
			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_iconImage.sprite = atlasSprite;
				_iconImage.enabled = true;
				_iconAddress = null;
				return;
			}

			// 아틀라스 미포함 — 로드 완료 전까지 숨겨 프리펩에 박힌 기본 스프라이트 깜빡임을 막는다.
			_iconImage.enabled = false;

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
				_iconImage.sprite = icon;
				_iconImage.enabled = true;
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
