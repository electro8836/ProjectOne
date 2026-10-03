using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Consumables;
using ProjectOne.Network;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.UI
{
	// 소모품 정보 팝업 Presenter — 보유 수량 조회와 선택 수량 계산을 담당한다.
	// 대상은 장비처럼 인스턴스 UID 가 아니라 **아이템 ID** 다. 소모품은 스택이라 인스턴스가 없다.
	//
	// 사용은 ConsumableUser 가 1개씩(지식의 서 — 서버 반영은 UseSkillPointItem), 파괴는 선택 수량만큼 한다.
	// 파괴는 로컬에서 먼저 빼고 서버 차감은 ItemSpendSender 가 백그라운드로 보낸다.
	public sealed class ConsumableInfoPresenter : Presenter<ConsumableInfoPopup>
	{
		private int _itemId;
		private Table_Item.Row _row;
		private bool _canUse;	// 소모품 데이터가 있는 아이템만 쓸 수 있다 — 재료는 파괴만 된다
		private int _owned;	// 보유 수량 — 선택 수량의 상한
		private int _count;	// 선택 수량

		protected override void OnInitialize()
		{
			view.OnPlusClicked += onPlusClicked;
			view.OnMinusClicked += onMinusClicked;
			view.OnUseClicked += onUseClicked;
			view.OnDeleteClicked += onDeleteClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnPlusClicked -= onPlusClicked;
			view.OnMinusClicked -= onMinusClicked;
			view.OnUseClicked -= onUseClicked;
			view.OnDeleteClicked -= onDeleteClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 팝업 표시 — 데이터 조회 후 View 에 그리기 지시, 닫힘까지 대기.
		// readOnly = true 면 디스플레이 경로다(결과창·상점) — 수량 조절·사용·파괴를 감춘다.
		public async UniTask ShowAsync(int itemId, bool readOnly, CancellationToken ct)
		{
			view.SetReadOnly(readOnly);

			Table_Item.Row row = Table_Item.Get(itemId);
			if (row == null)
			{
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				await view.WaitForCloseAsync(ct);
				return;
			}

			_itemId = itemId;
			_row = row;
			_owned = Account.Instance.Inventory.GetCount(itemId);

			ConsumableCatalog.BakedConsumable baked = ConsumableCatalog.Get(itemId);
			_canUse = baked != null && baked.isValid == true;

			// 보유하지 않은 아이템이면 선택 수량은 0 이고 조작 버튼이 전부 잠긴다.
			_count = (_owned > 0) ? 1 : 0;

			view.SetInfo(row);

			// 아이콘 로드가 끝난 뒤 한 번에 표시
			await view.BindItemSlotAsync(row, _owned, ct);
			applyCount();
			view.Reveal();

			await view.WaitForCloseAsync(ct);
		}

		// ── 입력 ──────────────────────────────────────────────────────────

		private void onPlusClicked()
		{
			if (_count >= _owned)
			{
				return;
			}

			_count++;
			applyCount();
		}

		private void onMinusClicked()
		{
			if (_count <= 1)
			{
				return;
			}

			_count--;
			applyCount();
		}

		private void onUseClicked()
		{
			if (_canUse == false || _owned <= 0)
			{
				return;
			}

			ConsumableUseResult result = ConsumableUser.Use(_itemId);
			if (result != ConsumableUseResult.Success)
			{
				// 무기 미착용·포인트 상한 등 — 안내 UI 는 실패 처리 정리 때 붙인다.
				Debug.LogWarning($"[ConsumablePopup] 사용 실패 itemId={_itemId} result={result}");
				return;
			}

			refreshOwned();
		}

		private void onDeleteClicked()
		{
			if (_owned <= 0 || _count <= 0)
			{
				return;
			}

			if (Account.Instance.Inventory.TrySpend(_itemId, _count) == false)
			{
				return;
			}

			ItemSpendSender.Instance.Enqueue(_itemId, _count);
			refreshOwned();
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		// 사용·파괴 후 보유 수량을 다시 읽는다. 다 썼으면 팝업을 닫는다.
		private void refreshOwned()
		{
			_owned = Account.Instance.Inventory.GetCount(_itemId);
			if (_owned <= 0)
			{
				view.CloseFromInput();
				return;
			}

			if (_count > _owned)
			{
				_count = _owned;
			}

			view.BindItemSlotAsync(_row, _owned, view.GetCancellationTokenOnDestroy()).Forget();
			applyCount();
		}

		// 선택 수량 표시와 버튼 잠금을 한 번에 맞춘다.
		// 경계(1 / 보유 수량)에 닿은 버튼은 숨기지 않고 잠그기만 한다.
		private void applyCount()
		{
			bool owned = _owned > 0;

			view.SetCount(_count);
			view.SetControlsInteractable(owned && _count < _owned, owned && _count > 1, owned && _canUse, owned);
		}
	}
}
