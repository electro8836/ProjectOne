using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Utils;

namespace ProjectOne.Currency
{
	public class CurrencyManager : MonoSingleton<CurrencyManager>
	{
		private Dictionary<CurrencyInfo, int> _amounts;
		private Dictionary<CurrencyInfo, int> _regenElapsed;
		private CurrencyInfo[] _currencyKeys;
		private CancellationTokenSource _regenCts;

		protected override void Awake()
		{
			base.Awake();
			initKeys();
			initAmounts();
			loadAll();
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
			int value;
			if (_amounts.TryGetValue(type, out value))
			{
				return value;
			}

			return 0;
		}

		public void Add(CurrencyInfo type, int delta)
		{
			if (delta <= 0) { return; }

			setAmount(type, _amounts[type] + delta);
		}

		public bool TrySpend(CurrencyInfo type, int cost)
		{
			if (cost <= 0) { return false; }

			if (_amounts[type] < cost) { return false; }

			setAmount(type, _amounts[type] - cost);
			return true;
		}

		public void SetAmount(CurrencyInfo type, int amount)
		{
			setAmount(type, amount);
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

		private void initAmounts()
		{
			_amounts = new Dictionary<CurrencyInfo, int>();
			_regenElapsed = new Dictionary<CurrencyInfo, int>();
			for (int i = 0; i < _currencyKeys.Length; i++)
			{
				_amounts[_currencyKeys[i]] = 0;
				_regenElapsed[_currencyKeys[i]] = 0;
			}
		}

		// ── PlayerPrefs 저장/로드 ─────────────────────────────────────

		private static string makePrefsKey(CurrencyInfo type)
		{
			return "Currency_" + type.ToString();
		}

		private void loadAll()
		{
			for (int i = 0; i < _currencyKeys.Length; i++)
			{
				CurrencyInfo key = _currencyKeys[i];
				_amounts[key] = PlayerPrefs.GetInt(makePrefsKey(key), 0);
			}
		}

		private void saveOne(CurrencyInfo type)
		{
			PlayerPrefs.SetInt(makePrefsKey(type), _amounts[type]);
			PlayerPrefs.Save();
		}

		// ── 내부 핵심 로직 ────────────────────────────────────────────

		private void setAmount(CurrencyInfo type, int newAmount)
		{
			if (!_amounts.ContainsKey(type)) { return; }

			int prev = _amounts[type];
			if (prev == newAmount) { return; }

			_amounts[type] = newAmount;
			saveOne(type);
			EventManager.Instance.Publish(new ResourceChangeEvent(type, prev, newAmount));
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

				if (_amounts[key] >= row.RegenMax) { continue; }

				_regenElapsed[key]++;
				if (_regenElapsed[key] < row.RegenInterval) { continue; }

				_regenElapsed[key] = 0;
				int newAmount = _amounts[key] + row.RegenAmount;
				if (newAmount > row.RegenMax) { newAmount = row.RegenMax; }

				setAmount(key, newAmount);
			}
		}
	}
}
