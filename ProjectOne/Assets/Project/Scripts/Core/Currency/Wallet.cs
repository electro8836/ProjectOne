using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.ServerData;

namespace ProjectOne.UserData
{
	// 재화 도메인 모델 — 보유 재화 수량을 보관/변경한다.
	// CurrencyData(직렬화 원본)와 _index(빠른 조회)를 동기 유지하며, 변경 시 자체 저장한다.
	public sealed class Wallet
	{
		private readonly CurrencyData _data;
		private readonly Dictionary<CurrencyInfo, int> _index = new Dictionary<CurrencyInfo, int>();

		public Wallet(CurrencyData data)
		{
			_data = (data != null) ? data : new CurrencyData();
			buildIndex();
		}

		// ── 공개 API ──────────────────────────────────────────────────

		// 보유 수량 — 미존재 시 0
		public int GetAmount(CurrencyInfo type)
		{
			int value;
			if (_index.TryGetValue(type, out value) == true)
			{
				return value;
			}

			return 0;
		}

		// 수량 설정 — 이전값과 같으면 무시, 변경 시 저장 + 변경 알림
		public void SetAmount(CurrencyInfo type, int amount)
		{
			int prev = GetAmount(type);
			if (prev == amount)
			{
				return;
			}

			setEntry(type, amount);
			save();
			EventManager.Instance.Publish(new ResourceChangeEvent(type, prev, amount));
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildIndex()
		{
			_index.Clear();
			for (int i = 0; i < _data.amounts.Count; i++)
			{
				CurrencyAmount entry = _data.amounts[i];
				if (entry == null || entry.currencyId == (int)CurrencyInfo.None)
				{
					continue;
				}

				_index[(CurrencyInfo)entry.currencyId] = entry.amount;
			}
		}

		// _data(직렬화용)와 _index(조회용) 동기 갱신
		private void setEntry(CurrencyInfo type, int amount)
		{
			_index[type] = amount;

			int id = (int)type;
			for (int i = 0; i < _data.amounts.Count; i++)
			{
				if (_data.amounts[i].currencyId == id)
				{
					_data.amounts[i].amount = amount;
					return;
				}
			}

			CurrencyAmount added = new CurrencyAmount();
			added.currencyId = id;
			added.amount = amount;
			_data.amounts.Add(added);
		}

		private void save()
		{
			ServerDataSystem.Repository.SaveAsync(ServerDataSystem.KeyCurrency, _data).Forget();
		}
	}
}
