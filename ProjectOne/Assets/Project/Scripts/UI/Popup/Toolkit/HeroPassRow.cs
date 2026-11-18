using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 히어로 패스 레벨 한 줄 (UI Toolkit) — 왼쪽 일반 보상, 가운데 레벨과 진행 선, 오른쪽 패스 보상(미구매면 잠금).
	// 받을 수 있는 칸을 누르면 수령 요청, 아니면 보상 정보. 스타일은 UIToolkit/Popups/HeroPass/HeroPass.uss.
	public sealed class HeroPassRow
	{
		// 보상 칸 하나 — 아이콘·수량·받음 표시.
		private sealed class RewardBox
		{
			public VisualElement root;
			public VisualElement icon;
			public Label count;
			public VisualElement check;
			public readonly List<RewardPreviewItem> preview = new List<RewardPreviewItem>(2);
			public bool isClaimable;
			public bool advanced;
		}

		private readonly ToolkitIconLoader _icons;
		private readonly CancellationToken _ownerToken;

		private readonly VisualElement _root;
		private readonly Label _level;
		private readonly VisualElement _lineFill;
		private readonly RewardBox _normal;
		private readonly RewardBox _advanced;
		private readonly VisualElement _lock;

		public int Level { get; private set; }

		public VisualElement Root
		{
			get { return _root; }
		}

		public event Action<HeroPassRow, bool> OnClaimClicked;

		public HeroPassRow(VisualElement parent, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_icons = icons;
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("pass-row");

			_normal = createBox(false);
			_root.Add(_normal.root);

			VisualElement middle = new VisualElement();
			middle.AddToClassList("pass-row__middle");
			VisualElement line = new VisualElement();
			line.AddToClassList("pass-row__line");
			_lineFill = new VisualElement();
			_lineFill.AddToClassList("pass-row__line-fill");
			line.Add(_lineFill);
			middle.Add(line);
			_level = new Label();
			_level.AddToClassList("pass-row__level");
			middle.Add(_level);
			_root.Add(middle);

			_advanced = createBox(true);
			_lock = new VisualElement();
			_lock.AddToClassList("pass-row__lock");
			_lock.pickingMode = PickingMode.Ignore;
			_advanced.root.Add(_lock);
			_root.Add(_advanced.root);

			parent.Add(_root);
		}

		public UniTask BindAsync(in HeroPassRewardSlotData data, CancellationToken ct)
		{
			Level = data.level;
			_level.text = data.level.ToString();

			// 미도달 0 / 현재 레벨 0.5 / 지난 레벨 1 — 진행 선을 위에서부터 그만큼 채운다.
			_lineFill.style.height = Length.Percent(Mathf.Clamp01(data.sliderValue) * 100f);
			_root.EnableInClassList("pass-row--reached", data.sliderValue > 0f);

			_lock.style.display = data.isLocked ? DisplayStyle.Flex : DisplayStyle.None;

			UniTask normalTask = bindBoxAsync(_normal, data.normalGroupId, data.normalClaimed, data.normalClaimable, ct);
			UniTask advancedTask = bindBoxAsync(_advanced, data.advancedGroupId, data.advancedClaimed, data.advancedClaimable, ct);
			return UniTask.WhenAll(normalTask, advancedTask);
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private RewardBox createBox(bool advanced)
		{
			RewardBox box = new RewardBox();
			box.advanced = advanced;
			box.root = new VisualElement();
			box.root.AddToClassList("pass-reward");
			box.root.AddToClassList(advanced ? "pass-reward--advanced" : "pass-reward--normal");
			box.root.userData = box;
			box.root.RegisterCallback<ClickEvent>(onBoxClicked);
			box.root.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)

			box.icon = new VisualElement();
			box.icon.AddToClassList("pass-reward__icon");
			box.icon.pickingMode = PickingMode.Ignore;
			box.root.Add(box.icon);

			box.count = new Label();
			box.count.AddToClassList("pass-reward__count");
			box.count.pickingMode = PickingMode.Ignore;
			box.root.Add(box.count);

			box.check = new VisualElement();
			box.check.AddToClassList("pass-reward__check");
			box.check.pickingMode = PickingMode.Ignore;
			box.root.Add(box.check);
			return box;
		}

		private UniTask bindBoxAsync(RewardBox box, int groupId, bool claimed, bool claimable, CancellationToken ct)
		{
			box.isClaimable = claimable;
			box.check.style.display = claimed ? DisplayStyle.Flex : DisplayStyle.None;
			box.root.EnableInClassList("pass-reward--claimable", claimable);

			box.preview.Clear();
			RewardPreview.Build(groupId, box.preview);
			if (box.preview.Count == 0)
			{
				box.count.text = string.Empty;
				return _icons.SetAsync(box.icon, string.Empty, ct);
			}

			RewardPreviewItem reward = box.preview[0];
			if (reward.type == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get(reward.currency);
				box.count.text = CurrencySlot.FormatAmount(reward.count);
				return _icons.SetAsync(box.icon, (currency != null) ? currency.Icon : string.Empty, ct);
			}

			Table_Item.Row item = Table_Item.Get(reward.itemId);
			int count = (reward.equipment != null) ? 1 : reward.count;
			box.count.text = count > 1 ? CurrencySlot.FormatAmount(count) : string.Empty;
			return _icons.SetAsync(box.icon, (item != null) ? item.Icon : string.Empty, ct);
		}

		private void onBoxClicked(ClickEvent evt)
		{
			RewardBox box = ((VisualElement)evt.currentTarget).userData as RewardBox;
			if (box == null)
			{
				return;
			}

			if (box.isClaimable == true)
			{
				if (OnClaimClicked != null)
				{
					OnClaimClicked.Invoke(this, box.advanced);
				}

				return;
			}

			if (box.preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(box.preview[0], box.root, _ownerToken);
		}
	}
}
