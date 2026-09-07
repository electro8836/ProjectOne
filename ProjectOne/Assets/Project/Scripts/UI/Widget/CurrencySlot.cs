using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	public class CurrencySlot : MonoBehaviour
	{
		[SerializeField] private EDT.Currency _targetCurrency;

		[Header("UI 참조")]
		[SerializeField] private Image _iconImage;
		[SerializeField] private TMP_Text _amountText;

		private Action<ResourceChangeEvent> _onResourceChanged;

		private void Awake()
		{
			_onResourceChanged = onResourceChanged;
			EventManager.Instance.Subscribe<ResourceChangeEvent>(_onResourceChanged);
			refresh();
			loadIconAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(_onResourceChanged);

			if (ResourceManager.HasInstance)
			{
				Table_Currency.Row row = Table_Currency.Get(_targetCurrency);
				if (row != null && !string.IsNullOrEmpty(row.Icon))
				{
					ResourceManager.Instance.Release(row.Icon);
				}
			}
		}

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			if (evt.CurrencyType != _targetCurrency) 
			{
				return;
			}

			updateAmount(evt.CurrentAmount);
		}

		private void refresh()
		{
			if (!Currency.CurrencyManager.HasInstance) { return; }

			int amount = Currency.CurrencyManager.Instance.GetAmount(_targetCurrency);
			updateAmount(amount);
		}

		private async UniTaskVoid loadIconAsync(CancellationToken ct)
		{
			Table_Currency.Row row = Table_Currency.Get(_targetCurrency);
			if (row == null || string.IsNullOrEmpty(row.Icon)) { return; }

			// 아틀라스에 있으면 동기로 즉시 세팅 — 없으면 비동기 로드(OnDestroy 의 Release 와 짝).
			Sprite atlasSprite = AtlasManager.Instance.Get(row.Icon);
			if (atlasSprite != null)
			{
				_iconImage.sprite = atlasSprite;
				return;
			}

			Sprite icon = await ResourceManager.Instance.AcquireAsync<Sprite>(row.Icon, ct);
			if (icon != null)
			{
				_iconImage.sprite = icon;
			}
		}

		private void updateAmount(int amount)
		{
			_amountText.text = formatAmount(amount);
		}

		private static string formatAmount(int amount)
		{
			if (amount >= 1_000_000_000) 
			{
				return (amount / 1_000_000_000).ToString() + "B";
			}

			if (amount >= 1_000_000) 
			{
				return (amount / 1_000_000).ToString() + "M"; 
			}

			if (amount >= 1_000)
			{ 
				return (amount / 1_000).ToString() + "K"; 
			}

			return amount.ToString();
		}
	}
}
