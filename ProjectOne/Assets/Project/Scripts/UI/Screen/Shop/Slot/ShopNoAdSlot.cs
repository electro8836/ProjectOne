using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 광고 제거 상품 슬롯(GoodsType.NoAd).
	// 보상 그룹이 없는 상품이다 — 구매 효과는 클라이언트가 직접 해석한다(광고 노출 플래그).
	public class ShopNoAdSlot : ShopProductSlotBase
	{
		[Header("광고 제거")]
		[SerializeField] private TMP_Text _descText;	// DsecText (프리펩 이름의 오타를 그대로 둔다)

		protected override UniTask onBindAsync(CancellationToken ct)
		{
			if (_descText != null)
			{
				_descText.text = row.Desc;
			}

			return UniTask.CompletedTask;
		}
	}
}
