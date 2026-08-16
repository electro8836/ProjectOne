using EDT;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.Currency
{
	// 재화 공개 API. 상태/저장은 Account.Wallet(서버 도메인)에 위임한다.
	//
	// 신규 Table_Currency 는 ID/Name/Desc/Icon 뿐이다 — 재생(Regen) 컬럼이 사라졌다.
	// 시간 재생 재화 개념이 폐기되어 리젠 루프도 함께 제거했다 (기반테이블 7장).
	public class CurrencyManager : MonoSingleton<CurrencyManager>
	{
		public int GetAmount(EDT.Currency type)
		{
			return Account.Instance.Wallet.GetAmount(type);
		}

		public void Add(EDT.Currency type, int delta)
		{
			if (delta <= 0) { return; }

			Account.Instance.Wallet.SetAmount(type, GetAmount(type) + delta);
		}

		public bool TrySpend(EDT.Currency type, int cost)
		{
			if (cost <= 0) { return false; }

			int current = GetAmount(type);
			if (current < cost) { return false; }

			Account.Instance.Wallet.SetAmount(type, current - cost);
			return true;
		}

		public void SetAmount(EDT.Currency type, int amount)
		{
			Account.Instance.Wallet.SetAmount(type, amount);
		}
	}
}
