using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 재화 슬롯 1칸 렌더 데이터 — Presenter 가 모아서 View 에 넘긴다(View 는 그리기만).
	public struct CurrencySlotData
	{
		public EDT.Currency currency;
		public string iconAddress;
		public int amount;
	}

	// 재화 목록 팝업 Presenter — 어떤 재화를 어떤 순서로 보여줄지 정한다.
	//
	// 표시 대상은 Currency enum 전체(None 제외)이고 아이콘은 Currency 테이블이 소유한다.
	// 열거 순서를 그대로 쓰므로 표시 순서가 실행할 때마다 흔들리지 않는다.
	public sealed class CurrencyListPopupPresenter : Presenter<CurrencyListPopup>
	{
		private readonly List<CurrencySlotData> _slotData = new List<CurrencySlotData>();

		public async UniTask ShowAsync(CancellationToken ct)
		{
			build();

			await view.RenderAsync(_slotData, ct);

			view.Reveal();
		}

		protected override void OnInitialize()
		{
			view.OnSlotClicked += onSlotClicked;

			EventManager.Instance.Subscribe<ResourceChangeEvent>(onResourceChanged);
		}

		protected override void OnDispose()
		{
			view.OnSlotClicked -= onSlotClicked;

			EventManager.Instance.Unsubscribe<ResourceChangeEvent>(onResourceChanged);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private void build()
		{
			_slotData.Clear();

			Array values = Enum.GetValues(typeof(EDT.Currency));
			for (int i = 0; i < values.Length; i++)
			{
				EDT.Currency currency = (EDT.Currency)values.GetValue(i);
				if (currency == EDT.Currency.None)
				{
					continue;
				}

				EDT.Table_Currency.Row row = EDT.Table_Currency.Get(currency);
				if (row == null)
				{
					Debug.LogError("[CurrencyListPopupPresenter] Currency 테이블에 " + currency.ToString() + " 행이 없습니다 — 목록에서 제외합니다.");
					continue;
				}

				CurrencySlotData data = new CurrencySlotData();
				data.currency = currency;
				data.iconAddress = row.Icon;
				data.amount = getAmount(currency);

				_slotData.Add(data);
			}
		}

		private static int getAmount(EDT.Currency currency)
		{
			if (ProjectOne.Currency.CurrencyManager.HasInstance == false)
			{
				return 0;
			}

			return ProjectOne.Currency.CurrencyManager.Instance.GetAmount(currency);
		}

		// 재화는 이름·설명뿐이라 정식 정보 팝업을 열지 않는다 — 누른 칸 위에 뜨는 툴팁으로 끝낸다
		// (던전 결과창·상점의 재화 슬롯과 같은 처리).
		private void onSlotClicked(EDT.Currency currency, RectTransform anchor)
		{
			EDT.Table_Currency.Row row = EDT.Table_Currency.Get(currency);
			if (row == null)
			{
				return;
			}

			UIManager.Instance.ShowSimplePopupAsync(row.Name + "\n" + row.Desc, anchor, view.GetDestroyToken()).Forget();
		}

		private void onResourceChanged(ResourceChangeEvent evt)
		{
			view.UpdateAmount(evt.CurrencyType, evt.CurrentAmount);
		}
	}
}
