using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 출석 칸 1개 (UI Toolkit). 일차·보상 아이콘·이름·수량, 받았으면 체크, 오늘 칸이면 강조 틀.
	// 받을 수 있는 칸을 누르면 수령 요청, 아니면 보상 정보를 띄운다. 스타일은 UIToolkit/Popups/DailyBonus/DailyBonus.uss.
	public sealed class DailyBonusCell
	{
		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ownerToken;

		private readonly VisualElement _root;
		private readonly Label _day;
		private readonly VisualElement _icon;
		private readonly Label _count;
		private readonly Label _name;

		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>(2);
		private bool _isClaimable;

		public int DayCount { get; private set; }

		public event Action<DailyBonusCell> OnClaimClicked;

		public DailyBonusCell(VisualElement parent, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_icons = icons;
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("daily-cell");
			_root.RegisterCallback<ClickEvent>(onClicked);
			_root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			_day = addLabel("daily-cell__day");

			_icon = new VisualElement();
			_icon.AddToClassList("daily-cell__icon");
			_icon.pickingMode = PickingMode.Ignore;
			_root.Add(_icon);

			_count = addLabel("daily-cell__count");
			_name = addLabel("daily-cell__name");

			VisualElement check = new VisualElement();	// 받음 체크 — 보임은 USS(daily-cell--claimed)
			check.AddToClassList("daily-cell__check");
			check.pickingMode = PickingMode.Ignore;
			_root.Add(check);

			parent.Add(_root);
		}

		public UniTask BindAsync(in DailyBonusSlotData data, CancellationToken ct)
		{
			DayCount = data.dayCount;
			_isClaimable = data.isClaimable;

			_day.text = data.dayCount.ToString() + "일";
			_root.EnableInClassList("daily-cell--claimed", data.isClaimed);	// 칸 어둡게 + 체크(USS)
			_root.EnableInClassList("daily-cell--focus", data.isFocused);
			_root.EnableInClassList("daily-cell--claimable", data.isClaimable);

			_preview.Clear();
			RewardPreview.Build(data.rewardGroupId, _preview);
			if (_preview.Count == 0)
			{
				applyCount(1);
				_name.text = string.Empty;
				return _icons.SetAsync(_icon, string.Empty, ct);
			}

			RewardPreviewItem reward = _preview[0];
			if (reward.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(reward.currency);
				applyCount(reward.count);
				_name.text = (currency != null) ? currency.Name : string.Empty;
				return _icons.SetAsync(_icon, (currency != null) ? currency.Icon : string.Empty, ct);
			}

			Table_Item.Row item = Table_Item.Get(reward.itemId);
			applyCount((reward.equipment != null) ? 1 : reward.count);
			_name.text = (item != null) ? item.Name : string.Empty;
			return _icons.SetAsync(_icon, (item != null) ? item.Icon : string.Empty, ct);
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 1개는 숫자 없이 읽힌다 — 2개 이상만 적는다.
		private void applyCount(int count)
		{
			_count.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
			_count.text = CurrencyFormat.Short(count);
		}

		private Label addLabel(string className)
		{
			Label label = new Label();
			label.AddToClassList(className);
			label.pickingMode = PickingMode.Ignore;
			_root.Add(label);
			return label;
		}

		private void onClicked(ClickEvent evt)
		{
			if (_isClaimable == true)
			{
				if (OnClaimClicked != null)
				{
					OnClaimClicked.Invoke(this);
				}

				return;
			}

			if (_preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(_preview[0], _root, _ownerToken);
		}
	}
}
