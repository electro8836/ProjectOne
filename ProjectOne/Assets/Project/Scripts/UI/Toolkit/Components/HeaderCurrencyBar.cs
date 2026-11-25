using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 창 제목 줄의 재화 캡슐(최대 3칸) — 아이콘 + 수량. UXML 의 currency-0 ~ currency-2 를 채우고 남는 칸은 숨긴다.
	// 재화 변경 이벤트를 스스로 구독하므로, 소유 화면은 만들고(OnBind) 닫힐 때 Dispose 만 부르면 된다.
	public sealed class HeaderCurrencyBar
	{
		private const int SLOT_COUNT = 3;

		private readonly EDT.Currency[] _currencies = new EDT.Currency[SLOT_COUNT];
		private readonly Label[] _texts = new Label[SLOT_COUNT];
		private readonly int[] _lastAmounts = new int[SLOT_COUNT];
		private int _count;

		public HeaderCurrencyBar(VisualElement root, EDT.Currency[] currencies, ToolkitIconLoader icons, CancellationToken ct)
		{
			_count = (currencies != null) ? currencies.Length : 0;
			if (_count > SLOT_COUNT)
			{
				_count = SLOT_COUNT;
			}

			for (int i = 0; i < SLOT_COUNT; i++)
			{
				VisualElement slot = root.Q("currency-" + i);
				if (i >= _count)
				{
					slot.style.display = DisplayStyle.None;
					continue;
				}

				_currencies[i] = currencies[i];
				_texts[i] = slot.Q<Label>(null, "currency-capsule__text");
				_lastAmounts[i] = -1;

				Table_Currency.Row row = Table_Currency.Get(currencies[i]);
				string address = (row != null) ? row.Icon : string.Empty;
				icons.SetAsync(slot.Q(null, "currency-capsule__icon"), address, ct).Forget();
			}

			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);

			if (Currency.CurrencyManager.HasInstance == true)
			{
				for (int i = 0; i < _count; i++)
				{
					apply(i, Currency.CurrencyManager.Instance.GetAmount(_currencies[i]));
				}
			}
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			for (int i = 0; i < _count; i++)
			{
				if (_currencies[i] == evt.CurrencyType)
				{
					apply(i, evt.CurrentAmount);
				}
			}
		}

		// 값이 바뀐 칸만 글자를 다시 쓴다.
		private void apply(int index, int amount)
		{
			if (_lastAmounts[index] == amount)
			{
				return;
			}

			_lastAmounts[index] = amount;
			_texts[index].text = CurrencyFormat.Short(amount);
		}
	}
}
