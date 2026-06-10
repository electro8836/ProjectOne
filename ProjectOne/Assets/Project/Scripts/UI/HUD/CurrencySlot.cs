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
		[SerializeField] private CurrencyInfo _targetCurrency;

		[Header("UI 참조")]
		[SerializeField] private Image _iconImage;
		[SerializeField] private TMP_Text _amountText;
		[SerializeField] private UIButton _navigationButton;

		private Action<ResourceChangeEvent> _onResourceChanged;
		private bool _isRegenable;
		private int _regenMax;
		private int _navigationLink;

		private void Awake()
		{
			cacheRowData();

			_onResourceChanged = onResourceChanged;
			EventManager.Instance.Subscribe<ResourceChangeEvent>(_onResourceChanged);
			_navigationButton.OnClickEvent += onNavigationClicked;
			refresh();
			loadIconAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(_onResourceChanged);
			_navigationButton.OnClickEvent -= onNavigationClicked;

			Table_CurrencyInfo.Row row = Table_CurrencyInfo.Get(_targetCurrency);
			if (row != null && !string.IsNullOrEmpty(row.Icon))
			{
				ResourceManager.Instance.Release(row.Icon);
			}
		}

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			if (evt.CurrencyType != _targetCurrency) { return; }

			updateAmount(evt.CurrentAmount);
		}

		private void onNavigationClicked()
		{
			// TODO: _navigationLink 값으로 획득 안내 팝업 열기
		}

		// 테이블 메타데이터(충전형 여부·최대치·획득 링크)를 1회 캐싱
		private void cacheRowData()
		{
			Table_CurrencyInfo.Row row = Table_CurrencyInfo.Get(_targetCurrency);
			if (row == null)
			{
				return;
			}

			_isRegenable = row.Regenable;
			_regenMax = row.RegenMax;
			_navigationLink = row.NavigationLink;
		}

		private void refresh()
		{
			if (!Currency.CurrencyManager.HasInstance) { return; }

			int amount = Currency.CurrencyManager.Instance.GetAmount(_targetCurrency);
			updateAmount(amount);
		}

		private async UniTaskVoid loadIconAsync(CancellationToken ct)
		{
			Table_CurrencyInfo.Row row = Table_CurrencyInfo.Get(_targetCurrency);
			if (row == null || string.IsNullOrEmpty(row.Icon)) { return; }

			Sprite icon = await ResourceManager.Instance.AcquireAsync<Sprite>(row.Icon, ct);
			if (icon != null)
			{
				_iconImage.sprite = icon;
			}
		}

		private void updateAmount(int amount)
		{
			if (_isRegenable)
			{
				_amountText.text = amount.ToString() + "/" + _regenMax.ToString();
				return;
			}

			_amountText.text = formatAmount(amount);
		}

		private static string formatAmount(int amount)
		{
			if (amount >= 1_000_000_000) { return (amount / 1_000_000_000).ToString() + "B"; }

			if (amount >= 1_000_000) { return (amount / 1_000_000).ToString() + "M"; }

			if (amount >= 1_000) { return (amount / 1_000).ToString() + "K"; }

			return amount.ToString();
		}
	}
}
