using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Reward;
using TMPro;
using UnityEngine;

namespace ProjectOne.UI
{
	// 확률표의 한 칸 — 아이템 하나와 그것이 나올 확률.
	// 진열 전용이라 클릭은 받지 않는다.
	public class ItemPoolSlot : MonoBehaviour
	{
		[SerializeField] private RectTransform _itemSlotRoot;	// ItemSlotRoot
		[SerializeField] private TMP_Text _probabilityText;	// Probability/Text

		private ItemSlot _itemSlot;

		public UniTask BindAsync(RewardChance chance, ItemSlot slotPrefab, ItemGradeColorTable colors, CancellationToken ct)
		{
			if (_probabilityText != null)
			{
				_probabilityText.text = (chance.chance * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
			}

			if (_itemSlot == null)
			{
				if (slotPrefab == null || _itemSlotRoot == null)
				{
					return UniTask.CompletedTask;
				}

				_itemSlot = Instantiate(slotPrefab, _itemSlotRoot);
				_itemSlot.StretchToParent();
			}

			_itemSlot.gameObject.SetActive(true);

			if (chance.equipment != null)
			{
				return _itemSlot.BindEquipmentAsync(chance.equipment, false, colors, ct);
			}

			Table_Item.Row row = Table_Item.Get(chance.itemId);
			if (row == null)
			{
				_itemSlot.gameObject.SetActive(false);
				return UniTask.CompletedTask;
			}

			// 확률표에서는 개수가 의미 없다 — 수량 표시는 비워 둔다.
			return _itemSlot.BindItemAsync(row, 0, colors, ct);
		}
	}
}
