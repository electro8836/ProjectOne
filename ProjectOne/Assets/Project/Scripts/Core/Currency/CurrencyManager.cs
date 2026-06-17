using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Currency
{
	// 재화 공개 API + 재생(Regen) 루프 구동. 상태/저장은 Account.Wallet(서버db 도메인)에 위임한다.
	public class CurrencyManager : MonoSingleton<CurrencyManager>
	{
		private Dictionary<CurrencyInfo, int> _regenElapsed;
		private CurrencyInfo[] _currencyKeys;
		private CancellationTokenSource _regenCts;

		protected override void Awake()
		{
			base.Awake();
			initKeys();
			initRegenElapsed();
			startRegenLoop();
		}

		protected override void OnApplicationQuit()
		{
			stopRegenLoop();
			base.OnApplicationQuit();
		}

		protected override void OnDestroy()
		{
			stopRegenLoop();
			base.OnDestroy();
		}

		// ── 공개 API ──────────────────────────────────────────────────

		public int GetAmount(CurrencyInfo type)
		{
			return Account.Instance.Wallet.GetAmount(type);
		}

		public void Add(CurrencyInfo type, int delta)
		{
			if (delta <= 0) { return; }

			Account.Instance.Wallet.SetAmount(type, GetAmount(type) + delta);
		}

		public bool TrySpend(CurrencyInfo type, int cost)
		{
			if (cost <= 0) { return false; }

			int current = GetAmount(type);
			if (current < cost) { return false; }

			Account.Instance.Wallet.SetAmount(type, current - cost);
			return true;
		}

		public void SetAmount(CurrencyInfo type, int amount)
		{
			Account.Instance.Wallet.SetAmount(type, amount);
		}

		// ── 초기화 ────────────────────────────────────────────────────

		private void initKeys()
		{
			CurrencyInfo[] allValues = (CurrencyInfo[])Enum.GetValues(typeof(CurrencyInfo));
			int count = 0;
			for (int i = 0; i < allValues.Length; i++)
			{
				if (allValues[i] != CurrencyInfo.None) { count++; }
			}

			_currencyKeys = new CurrencyInfo[count];
			int idx = 0;
			for (int i = 0; i < allValues.Length; i++)
			{
				if (allValues[i] != CurrencyInfo.None)
				{
					_currencyKeys[idx] = allValues[i];
					idx++;
				}
			}
		}

		private void initRegenElapsed()
		{
			_regenElapsed = new Dictionary<CurrencyInfo, int>();
			for (int i = 0; i < _currencyKeys.Length; i++)
			{
				_regenElapsed[_currencyKeys[i]] = 0;
			}
		}

		// ── 리젠 루프 ─────────────────────────────────────────────────

		private void startRegenLoop()
		{
			_regenCts = new CancellationTokenSource();
			runRegenLoopAsync(_regenCts.Token).Forget();
		}

		private void stopRegenLoop()
		{
			if (_regenCts != null)
			{
				_regenCts.Cancel();
				_regenCts.Dispose();
				_regenCts = null;
			}
		}

		private async UniTaskVoid runRegenLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				await UniTask.Delay(1000, cancellationToken: ct);
				if (ct.IsCancellationRequested) { break; }

				tickRegen();
			}
		}

		private void tickRegen()
		{
			for (int i = 0; i < _currencyKeys.Length; i++)
			{
				CurrencyInfo key = _currencyKeys[i];
				Table_CurrencyInfo.Row row = Table_CurrencyInfo.Get(key);
				if (row == null || !row.Regenable) { continue; }

				int current = Account.Instance.Wallet.GetAmount(key);
				if (current >= row.RegenMax) { continue; }

				_regenElapsed[key]++;
				if (_regenElapsed[key] < row.RegenInterval) { continue; }

				_regenElapsed[key] = 0;
				int newAmount = current + row.RegenAmount;
				if (newAmount > row.RegenMax) { newAmount = row.RegenMax; }

				Account.Instance.Wallet.SetAmount(key, newAmount);
			}
		}
	}
}
