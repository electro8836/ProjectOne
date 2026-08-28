using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 소모품 정보 팝업 Presenter — 보유 수량 조회와 선택 수량 계산을 담당한다.
	// 대상은 장비처럼 인스턴스 UID 가 아니라 **아이템 ID** 다. 소모품은 스택이라 인스턴스가 없다.
	//
	// 사용/파괴는 아직 로그만 남긴다(연결 예정).
	public sealed class ConsumableInfoPresenter : Presenter<ConsumableInfoPopup>
	{
		private int _itemId;
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
		public async UniTask ShowAsync(int itemId, CancellationToken ct)
		{
			Table_Item.Row row = Table_Item.Get(itemId);
			if (row == null)
			{
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				await view.WaitForCloseAsync(ct);
				return;
			}

			_itemId = itemId;
			_owned = Account.Instance.Inventory.GetCount(itemId);

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
			UnityEngine.Debug.Log("[ConsumablePopup] 사용 버튼 — 미연결 itemId=" + _itemId + " count=" + _count);
		}

		private void onDeleteClicked()
		{
			UnityEngine.Debug.Log("[ConsumablePopup] 파괴 버튼 — 미연결 itemId=" + _itemId + " count=" + _count);
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		// 선택 수량 표시와 버튼 잠금을 한 번에 맞춘다.
		// 경계(1 / 보유 수량)에 닿은 버튼은 숨기지 않고 잠그기만 한다.
		private void applyCount()
		{
			bool owned = _owned > 0;

			view.SetCount(_count);
			view.SetControlsInteractable(owned && _count < _owned, owned && _count > 1, owned, owned);
		}
	}
}
