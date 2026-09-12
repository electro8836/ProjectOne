using UnityEngine;

namespace ProjectOne.UI
{
	// 영웅 패스 슬롯(GoodsType.HeroPass).
	// 보상 그룹이 없는 상품이다 — 구매 효과는 클라이언트가 직접 해석한다(패스 보유 상태).
	//
	// 한 번 사면 끝나는 상품이라 남은 횟수를 숫자로 보여주지 않는다 —
	// 소진되면 베이스가 가격 자리를 "구매함" 으로 바꾸고 버튼을 비활성으로 남긴다(숨기지 않는다).
	public class ShopPassSlot : ShopProductSlotBase
	{
		[Header("패스")]
		[SerializeField] private UIButton _infoButton;	// InfoButton — 패스 혜택 설명

		protected override void Awake()
		{
			base.Awake();

			if (_infoButton != null)
			{
				_infoButton.OnClickEvent += onInfoClicked;
			}
		}

		protected override void OnDestroy()
		{
			if (_infoButton != null)
			{
				_infoButton.OnClickEvent -= onInfoClicked;
			}

			base.OnDestroy();
		}

		// TODO(팝업) — 패스 혜택 설명 팝업으로 교체한다.
		private void onInfoClicked()
		{
			if (row == null)
			{
				return;
			}

			Debug.Log($"[Shop] 패스 설명 요청 goodsId={row.ID} name={row.Name} desc={row.Desc}");
		}
	}
}
