using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 퀘스트 목록 줄 1개 (UI Toolkit). 번호·이름·설명과 보상 칸 하나를 그린다.
	// 진행 중이면 틀 강조, 클리어면 흐리게 + 체크, 받을 수 있으면 "받기" 표시 — 그 칸을 누르면 완료 처리한다.
	// 스타일은 UIToolkit/Popups/Quest/Quest.uss.
	public sealed class QuestCell
	{
		private readonly VisualElement _root;
		private readonly Label _number;
		private readonly Label _name;
		private readonly Label _desc;
		private readonly VisualElement _reward;
		private readonly ItemCellView _rewardCell;
		private readonly VisualElement _check;
		private readonly Label _claim;
		private readonly CancellationToken _ownerToken;

		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>(2);
		private int _questId;
		private bool _isClaimable;

		public VisualElement Root
		{
			get { return _root; }
		}

		public QuestCell(VisualElement parent, ItemGradeColorTable colors, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("quest-row");

			_number = new Label();
			_number.AddToClassList("quest-row__number");
			_root.Add(_number);

			VisualElement text = new VisualElement();
			text.AddToClassList("quest-row__text");
			_name = new Label();
			_name.AddToClassList("quest-row__name");
			_desc = new Label();
			_desc.AddToClassList("quest-row__desc");
			text.Add(_name);
			text.Add(_desc);
			_root.Add(text);

			_reward = new VisualElement();
			_reward.AddToClassList("quest-row__reward");
			_rewardCell = new ItemCellView(_reward, colors, icons);
			_rewardCell.OnClicked += onRewardClicked;

			_check = new VisualElement();
			_check.AddToClassList("quest-row__check");
			_check.pickingMode = PickingMode.Ignore;
			_reward.Add(_check);

			_claim = new Label("받기");
			_claim.AddToClassList("quest-row__claim");
			_claim.pickingMode = PickingMode.Ignore;
			_reward.Add(_claim);

			_root.Add(_reward);
			parent.Add(_root);
		}

		public UniTask BindAsync(in QuestSlotData data, CancellationToken ct)
		{
			_questId = data.questId;
			_isClaimable = data.isClaimable;

			_root.EnableInClassList("quest-row--current", data.isCurrent);
			_root.EnableInClassList("quest-row--cleared", data.isCleared);

			_number.text = data.questId.ToString();
			_name.text = data.name;
			_desc.text = data.desc;

			_check.style.display = (data.isCleared && data.isClaimable == false) ? DisplayStyle.Flex : DisplayStyle.None;
			_claim.style.display = data.isClaimable ? DisplayStyle.Flex : DisplayStyle.None;

			return bindRewardAsync(data.rewardGroupId, ct);
		}

		public void SetShown(bool shown)
		{
			_root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// 보상은 그룹의 첫 구성 하나만 보여 준다.
		private UniTask bindRewardAsync(int rewardGroupId, CancellationToken ct)
		{
			_preview.Clear();
			RewardPreview.Build(rewardGroupId, _preview);
			if (_preview.Count == 0)
			{
				_reward.style.display = DisplayStyle.None;
				return UniTask.CompletedTask;
			}

			_reward.style.display = DisplayStyle.Flex;
			RewardPreviewItem reward = _preview[0];

			if (reward.type == RewardType.Currency)
			{
				return _rewardCell.BindCurrencyAsync(reward.currency, reward.count, ct);
			}

			if (reward.equipment != null)
			{
				return _rewardCell.BindEquipmentAsync(reward.equipment, false, ct);
			}

			Table_Item.Row row = Table_Item.Get(reward.itemId);
			if (row == null)
			{
				_reward.style.display = DisplayStyle.None;
				return UniTask.CompletedTask;
			}

			return _rewardCell.BindItemAsync(row, reward.count, ct);
		}

		// 받을 수 있으면 완료 처리, 아니면 보상 정보.
		private void onRewardClicked(ItemCellView sender, long uid, int itemId)
		{
			if (_isClaimable == true)
			{
				Account.Instance.Quests.TryComplete(_questId);
				return;
			}

			if (_preview.Count == 0)
			{
				return;
			}

			ShopRewardPopup.Show(_preview[0], sender.Root, _ownerToken);
		}
	}
}
