using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Items;
using ProjectOne.Reward;
using UnityEngine;

namespace ProjectOne.UI
{
	// 패키지 슬롯(GoodsType.Package). 여러 아이템을 한 묶음으로 파는 상품이다.
	//
	// 구성품은 RewardGroupID 로 묶인 보상 행들이다. 추첨하지 않고 RewardPreview 로 펼쳐
	// PackageGrid 에 진열한다. 칸을 누르면 던전 결과창과 같은 **읽기 전용** 팝업이 뜬다 —
	// 아직 내 것이 아닌 목록이라 장착·사용 같은 조작은 전부 막혀 있다.
	public class ShopPackageSlot : ShopProductSlotBase
	{
		// 한 줄이 5칸이라 두 줄까지만 보여준다. 넘치는 구성품은 잘라낸다.
		private const int MAX_DISPLAY_COUNT = 10;

		// PackageGrid 의 열 수 — 프리펩의 GridLayoutGroup constraintCount 와 맞춰야 한다.
		private const int COLUMN_COUNT = 5;

		// 구성품이 한 줄일 때의 높이. 프리펩 기본값과 같다.
		private const float BASE_HEIGHT = 350f;

		// 줄이 하나 늘 때마다 더해지는 높이.
		private const float ROW_HEIGHT = 150f;

		[Header("구성품")]
		[SerializeField] private RectTransform _packageGrid;		// PackageGrid — 구성품 5열
		[SerializeField] private ItemSlot _itemSlotPrefab;		// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private readonly List<ItemSlot> _slots = new List<ItemSlot>();
		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>();
		private readonly List<UniTask> _bindTasks = new List<UniTask>();

		// 클릭된 칸이 무엇이었는지 되짚는다 — OnClicked 의 (uid, itemId) 만으로는
		// 재화와 아이템을 구분할 수 없다 (ItemSlot 주석 참조).
		private readonly Dictionary<ItemSlot, RewardPreviewItem> _slotItems = new Dictionary<ItemSlot, RewardPreviewItem>();

		protected override void OnDestroy()
		{
			for (int i = 0; i < _slots.Count; i++)
			{
				if (_slots[i] != null)
				{
					_slots[i].OnClicked -= onSlotClicked;
				}
			}

			base.OnDestroy();
		}

		protected override async UniTask onBindAsync(CancellationToken ct)
		{
			_preview.Clear();
			_slotItems.Clear();
			_bindTasks.Clear();

			RewardPreview.Build(row.RewardGroupID, _preview);

			int count = _preview.Count < MAX_DISPLAY_COUNT ? _preview.Count : MAX_DISPLAY_COUNT;
			applyHeight(count);

			for (int i = 0; i < count; i++)
			{
				ItemSlot slot = getOrCreateSlot(i);
				slot.gameObject.SetActive(true);

				RewardPreviewItem item = _preview[i];
				_slotItems[slot] = item;
				_bindTasks.Add(bindSlot(slot, item, ct));
			}

			for (int i = count; i < _slots.Count; i++)
			{
				_slots[i].gameObject.SetActive(false);
			}

			await UniTask.WhenAll(_bindTasks).SuppressCancellationThrow();
		}

		// 구성품이 한 줄을 넘을 때마다 슬롯도 그만큼 길어져야 한다 — 안 그러면 둘째 줄이 슬롯 밖으로 나간다.
		// Content 의 VerticalLayoutGroup 이 자식 높이를 제어하지 않으므로 sizeDelta 가 그대로 반영된다.
		private void applyHeight(int count)
		{
			RectTransform rect = transform as RectTransform;
			if (rect == null)
			{
				return;
			}

			int extraRows = (count > 0) ? (count - 1) / COLUMN_COUNT : 0;

			Vector2 size = rect.sizeDelta;
			size.y = BASE_HEIGHT + ROW_HEIGHT * extraRows;
			rect.sizeDelta = size;
		}

		// ── 내부: 슬롯 ────────────────────────────────────────────────────

		private ItemSlot getOrCreateSlot(int index)
		{
			if (index < _slots.Count)
			{
				return _slots[index];
			}

			ItemSlot slot = Instantiate(_itemSlotPrefab, _packageGrid);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);
			return slot;
		}

		private UniTask bindSlot(ItemSlot slot, RewardPreviewItem item, CancellationToken ct)
		{
			if (item.equipment != null)
			{
				return slot.BindEquipmentAsync(item.equipment, false, _gradeColors, ct);
			}

			if (item.type == RewardType.Currency)
			{
				return slot.BindCurrencyAsync(item.currency, item.count, _gradeColors, ct);
			}

			Table_Item.Row itemRow = Table_Item.Get(item.itemId);
			if (itemRow == null)
			{
				slot.gameObject.SetActive(false);
				return UniTask.CompletedTask;
			}

			return slot.BindItemAsync(itemRow, item.count, _gradeColors, ct);
		}

		// ── 내부: 클릭 ────────────────────────────────────────────────────

		// 구성품 클릭 — 진열된 것은 내 것이 아니므로 읽기 전용 팝업만 연다.
		private void onSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			RewardPreviewItem item;
			if (_slotItems.TryGetValue(sender, out item) == false)
			{
				return;
			}

			ShopRewardPopup.Show(item, sender, this.GetCancellationTokenOnDestroy());
		}
	}
}
