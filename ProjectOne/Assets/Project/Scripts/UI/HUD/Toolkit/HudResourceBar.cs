using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 메인 HUD 오른쪽 위 재화 표시(골드·다이아). 재화 변경 이벤트를 스스로 구독한다.
	public sealed class HudResourceBar
	{
		private readonly Label _goldText;
		private readonly Label _diaText;

		private int _lastGold = -1;
		private int _lastDia = -1;

		public HudResourceBar(VisualElement root, ToolkitIconLoader icons, CancellationToken ct)
		{
			_goldText = root.Q<Label>("gold-text");
			_diaText = root.Q<Label>("dia-text");

			setIcon(root.Q("gold-icon"), EDT.Currency.Gold, icons, ct);
			setIcon(root.Q("dia-icon"), EDT.Currency.Dia, icons, ct);

			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);

			if (Currency.CurrencyManager.HasInstance == true)
			{
				apply(EDT.Currency.Gold, Currency.CurrencyManager.Instance.GetAmount(EDT.Currency.Gold));
				apply(EDT.Currency.Dia, Currency.CurrencyManager.Instance.GetAmount(EDT.Currency.Dia));
			}
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		private static void setIcon(VisualElement target, EDT.Currency currency, ToolkitIconLoader icons, CancellationToken ct)
		{
			Table_Currency.Row row = Table_Currency.Get(currency);
			string address = (row != null) ? row.Icon : string.Empty;
			icons.SetAsync(target, address, ct).Forget();
		}

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			apply(evt.CurrencyType, evt.CurrentAmount);
		}

		// 값이 바뀐 재화만 글자를 다시 쓴다.
		private void apply(EDT.Currency currency, int amount)
		{
			if (currency == EDT.Currency.Gold)
			{
				if (_lastGold != amount)
				{
					_lastGold = amount;
					_goldText.text = CurrencySlot.FormatAmount(amount);
				}
			}
			else if (currency == EDT.Currency.Dia)
			{
				if (_lastDia != amount)
				{
					_lastDia = amount;
					_diaText.text = CurrencySlot.FormatAmount(amount);
				}
			}
		}
	}
}
