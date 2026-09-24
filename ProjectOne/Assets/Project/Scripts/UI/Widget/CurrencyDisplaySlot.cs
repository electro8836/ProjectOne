using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.Event;
using ProjectOne.Resources;
using ProjectOne.Upgrade;

namespace ProjectOne.UI
{
	// 재화 아이콘 + "x수량" 1칸(UIPrefab_CurrencyDisplaySlot).
	//
	// 두 가지로 쓴다.
	//   고정 모드 : 인스펙터에 재화 타입을 넣으면 CurrencySlot 처럼 보유량을 스스로 구독해 표시한다.
	//   바인딩 모드 : 타입을 None 으로 두고 BindAsync 로 비용 1건을 받아 필요량을 표시한다 (부족하면 빨간색).
	public class CurrencyDisplaySlot : MonoBehaviour
	{
		[SerializeField] private EDT.Currency _targetCurrency;	// None 이면 바인딩 모드

		[Header("UI 참조")]
		[SerializeField] private Image _iconImage;		// Icon
		[SerializeField] private TMP_Text _countText;	// Count

		[Header("부족 표시")]
		[SerializeField] private Color _notEnoughColor = Color.red;

		// 프리펩에 박힌 기본 색 — 부족 표시를 풀 때 되돌린다.
		private Color _normalColor;

		// 현재 로드한 아이콘 주소 (Acquire/Release 짝 맞춤용)
		private string _iconAddress;

		private void Awake()
		{
			_normalColor = _countText.color;

			if (_targetCurrency == EDT.Currency.None)
			{
				return;
			}

			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);
			refreshOwned();

			Table_Currency.Row row = Table_Currency.Get(_targetCurrency);
			setIcon((row != null) ? row.Icon : string.Empty, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void OnDestroy()
		{
			if (_targetCurrency != EDT.Currency.None)
			{
				EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
			}

			releaseIcon();
		}

		// 비용 1건 바인딩 — 필요량을 표시하고 보유량이 모자라면 빨간색으로 칠한다.
		public UniTask BindAsync(UpgradeCost cost, CancellationToken ct)
		{
			setCount(cost.amount);
			_countText.color = cost.IsEnough ? _normalColor : _notEnoughColor;

			Table_Currency.Row row = Table_Currency.Get(cost.currency);
			return setIcon((row != null) ? row.Icon : string.Empty, ct);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			if (evt.CurrencyType != _targetCurrency)
			{
				return;
			}

			setCount(evt.CurrentAmount);
		}

		private void refreshOwned()
		{
			if (Currency.CurrencyManager.HasInstance == false)
			{
				return;
			}

			setCount(Currency.CurrencyManager.Instance.GetAmount(_targetCurrency));
		}

		private void setCount(int amount)
		{
			_countText.text = "x" + amount.ToString();
		}

		// CurrencyInfoSlot 과 같은 규칙 — 아틀라스에 있으면 동기로 즉시, 없으면 Addressable 로 로드한다.
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
