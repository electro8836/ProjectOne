using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 보유 재료 1칸.
	public sealed class MaterialSlotData
	{
		public int itemId;
		public int owned;
	}

	// 보유 재료 팝업 Presenter — 어떤 재료를 보일지 테이블에서 찾고, 칸을 누르면 아이템 정보 팝업(설명·획득처)을 띄운다.
	//
	// 대상: 제작법의 재료 칸(Mat1~4)이나 결과에 한 번이라도 나오는 Material 아이템(보물상자 열쇠처럼 제작과 무관한 재료는 빠진다).
	public sealed class MaterialListPopupPresenter : Presenter<MaterialListPopup>
	{
		private const string CONSUMABLE_POPUP_ADDRESS = "UIPrefab_ConsumablePopup";

		private readonly List<MaterialSlotData> _slots = new List<MaterialSlotData>();
		private CancellationToken _ct;

		public async UniTask ShowAsync(CancellationToken ct)
		{
			_ct = ct;
			build();

			await view.RenderAsync(_slots, ct);

			view.Reveal();
		}

		protected override void OnInitialize()
		{
			view.OnSlotClicked += onSlotClicked;
		}

		protected override void OnDispose()
		{
			view.OnSlotClicked -= onSlotClicked;
		}

		// ── 내부: 목록 ────────────────────────────────────────────────────

		private void build()
		{
			_slots.Clear();

			HashSet<int> ids = new HashSet<int>();
			Dictionary<int, Table_ForgeRecipe.Row>.Enumerator e = Table_ForgeRecipe.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				Table_ForgeRecipe.Row recipe = e.Current.Value;
				addIfMaterial(ids, recipe.ResultItemID);
				addIfMaterial(ids, recipe.Mat1_ID);
				addIfMaterial(ids, recipe.Mat2_ID);
				addIfMaterial(ids, recipe.Mat3_ID);
				addIfMaterial(ids, recipe.Mat4_ID);
			}

			List<int> sorted = new List<int>(ids);
			sorted.Sort();
			for (int i = 0; i < sorted.Count; i++)
			{
				MaterialSlotData slot = new MaterialSlotData();
				slot.itemId = sorted[i];
				slot.owned = Account.Instance.Inventory.GetCount(slot.itemId);
				_slots.Add(slot);
			}
		}

		private static void addIfMaterial(HashSet<int> ids, int itemId)
		{
			if (itemId == 0)
			{
				return;
			}

			Table_Item.Row item = Table_Item.Get(itemId);
			if (item != null && item.MainCategory == ItemMainCategory.Material)
			{
				ids.Add(itemId);
			}
		}

		// ── 내부: 칸 클릭 ─────────────────────────────────────────────────

		private void onSlotClicked(int index)
		{
			if (index < 0 || index >= _slots.Count)
			{
				return;
			}

			showItemInfoAsync(_slots[index].itemId).Forget();
		}

		// 아이템 정보 팝업에서 '조합하기'나 사용처 줄로 닫히면 이 팝업도 그 제작법을 돌려주며 닫힌다.
		private async UniTaskVoid showItemInfoAsync(int itemId)
		{
			int recipeId = await UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, itemId, true, _ct, true);
			if (recipeId != 0)
			{
				view.CloseWithRecipe(recipeId);
			}
		}
	}
}
