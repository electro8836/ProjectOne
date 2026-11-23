using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;
using ProjectOne.UserData;
using ProjectOne.Utils;

namespace ProjectOne.UI
{
	// 퀘스트 목록 줄 1개 (UI Toolkit). 왼쪽 단계 줄(번호 원 + 다음 줄로 이어지는 세로 선)과 카드(상태 칩·이름·설명·보상 칸)를 그린다.
	// 진행 중이면 보라 카드 + 진행도, 클리어면 초록 카드 + 보상 칸 흐리게 + 체크(완료·진행 중 보상 칸은 흰 틀), 받을 수 있으면 "받기" 표시 — 그 칸을 누르면 완료 처리한다.
	// 스타일은 UIToolkit/Popups/Quest/Quest.uss.
	public sealed class QuestCell
	{
		private const string CHIP_CLEARED = "완료";
		private const string CHIP_CURRENT = "진행 중";

		private readonly VisualElement _root;
		private readonly VisualElement _line;
		private readonly Label _number;
		private readonly Label _chip;
		private readonly Label _name;
		private readonly Label _desc;
		private readonly VisualElement _progress;
		private readonly Label _progressLabel;
		private readonly Label _progressCount;
		private readonly VisualElement _progressFill;
		private readonly VisualElement _reward;
		private readonly ItemCellView _rewardCell;
		private readonly VisualElement _check;
		private readonly Label _claim;
		private readonly CancellationToken _ownerToken;

		private readonly List<RewardPreviewItem> _preview = new List<RewardPreviewItem>(2);
		private int _questId;
		private bool _isClaimable;
		private bool _whiteFrame;

		public VisualElement Root
		{
			get { return _root; }
		}

		public QuestCell(VisualElement parent, ItemGradeColorTable colors, ToolkitIconLoader icons, CancellationToken ownerToken)
		{
			_ownerToken = ownerToken;

			_root = new VisualElement();
			_root.AddToClassList("quest-row");

			// 단계 줄 — 선을 먼저 넣어 번호 원 아래에 깔리게 한다
			VisualElement rail = new VisualElement();
			rail.AddToClassList("quest-row__rail");
			rail.pickingMode = PickingMode.Ignore;
			_line = new VisualElement();
			_line.AddToClassList("quest-row__line");
			_line.pickingMode = PickingMode.Ignore;
			rail.Add(_line);
			_number = new Label();
			_number.AddToClassList("quest-row__number");
			_number.pickingMode = PickingMode.Ignore;
			rail.Add(_number);
			_root.Add(rail);

			VisualElement card = new VisualElement();
			card.AddToClassList("quest-row__card");
			_root.Add(card);

			VisualElement text = new VisualElement();
			text.AddToClassList("quest-row__text");
			VisualElement head = new VisualElement();
			head.AddToClassList("quest-row__head");
			_chip = new Label();
			_chip.AddToClassList("quest-row__chip");
			_name = new Label();
			_name.AddToClassList("quest-row__name");
			head.Add(_chip);
			head.Add(_name);
			_desc = new Label();
			_desc.AddToClassList("quest-row__desc");
			text.Add(head);
			text.Add(_desc);

			_progress = new VisualElement();
			_progress.AddToClassList("quest-row__progress");
			VisualElement progressHead = new VisualElement();
			progressHead.AddToClassList("quest-row__progress-head");
			_progressLabel = new Label();
			_progressLabel.AddToClassList("quest-row__progress-text");
			_progressCount = new Label();
			_progressCount.AddToClassList("quest-row__progress-text");
			progressHead.Add(_progressLabel);
			progressHead.Add(_progressCount);
			VisualElement bar = new VisualElement();
			bar.AddToClassList("quest-row__bar");
			_progressFill = new VisualElement();
			_progressFill.AddToClassList("quest-row__bar-fill");
			bar.Add(_progressFill);
			_progress.Add(progressHead);
			_progress.Add(bar);
			text.Add(_progress);
			card.Add(text);

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

			card.Add(_reward);
			parent.Add(_root);
		}

		// isLast: 마지막 줄이면 아래로 이어지는 선을 숨긴다.
		public UniTask BindAsync(in QuestSlotData data, bool isLast, CancellationToken ct)
		{
			_questId = data.questId;
			_isClaimable = data.isClaimable;
			_whiteFrame = data.isCleared || data.isCurrent;

			_root.EnableInClassList("quest-row--current", data.isCurrent);
			_root.EnableInClassList("quest-row--cleared", data.isCleared);
			_root.EnableInClassList("quest-row--locked", data.isCurrent == false && data.isCleared == false);
			_line.style.display = isLast ? DisplayStyle.None : DisplayStyle.Flex;

			_number.text = data.questId.ToString();
			_name.text = data.name;
			_desc.text = data.desc;

			if (data.isCleared == true)
			{
				_chip.text = CHIP_CLEARED;
				_chip.style.display = DisplayStyle.Flex;
			}
			else if (data.isCurrent == true)
			{
				_chip.text = CHIP_CURRENT;
				_chip.style.display = DisplayStyle.Flex;
			}
			else
			{
				_chip.style.display = DisplayStyle.None;
			}

			_progress.style.display = data.isCurrent ? DisplayStyle.Flex : DisplayStyle.None;
			_check.style.display = (data.isCleared && data.isClaimable == false) ? DisplayStyle.Flex : DisplayStyle.None;
			_claim.style.display = data.isClaimable ? DisplayStyle.Flex : DisplayStyle.None;

			return bindRewardAsync(data.rewardGroupId, ct);
		}

		// 진행 중 줄의 진행도 — 목표 종류 · 현재/필요 · 막대
		public void SetProgress(string label, int current, int required)
		{
			_progressLabel.text = label;
			_progressCount.text = $"{current}/{required}";
			float ratio = (required > 0) ? Mathf.Clamp01((float)current / required) : 0f;
			_progressFill.style.width = Length.Percent(ratio * 100f);
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
				UniTask currencyTask = _rewardCell.BindCurrencyAsync(reward.currency, reward.count, ct);
				_rewardCell.SetCountText(NumberFormat.ToShort(reward.count));	// 칸이 작아 큰 수는 999K 처럼 줄인다
				return applyFrame(currencyTask);
			}

			if (reward.equipment != null)
			{
				return applyFrame(_rewardCell.BindEquipmentAsync(reward.equipment, false, ct));
			}

			Table_Item.Row row = Table_Item.Get(reward.itemId);
			if (row == null)
			{
				_reward.style.display = DisplayStyle.None;
				return UniTask.CompletedTask;
			}

			UniTask itemTask = _rewardCell.BindItemAsync(row, reward.count, ct);
			if (reward.count > 1)
			{
				_rewardCell.SetCountText(NumberFormat.ToShort(reward.count));
			}

			return applyFrame(itemTask);
		}

		// 완료·진행 중이면 보상 칸 틀을 흰색으로 — 등급색은 Bind 의 동기 구간에서 인라인으로 칠해지므로 그 뒤에 덮어쓴다
		private UniTask applyFrame(UniTask bindTask)
		{
			if (_whiteFrame == true)
			{
				_rewardCell.Root.style.unityBackgroundImageTintColor = Color.white;
			}

			return bindTask;
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
