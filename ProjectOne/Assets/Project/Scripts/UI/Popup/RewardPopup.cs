using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 획득 보상 팝업(UIPrefab_RewardPopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Reward/RewardPopup.uxml.
	// 받은 보상을 아이템 칸으로 늘어놓고, 칸을 누르면 읽기 전용 정보를 띄운다. 딤이나 확인으로 닫는다.
	public class RewardPopup : ToolkitScreen
	{
		[SerializeField] private ItemGradeColorTable _gradeColors;	// 등급 색상 SO

		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();
		private readonly List<GrantedReward> _rewards = new List<GrantedReward>();
		private UniTaskCompletionSource _tcs;
		private ScrollView _grid;

		protected override void OnBind(VisualElement root)
		{
			_grid = root.Q<ScrollView>("grid");
			_grid.AddManipulator(new ScrollDragManipulator(_grid));
			root.Q("dim").RegisterCallback<ClickEvent>(onDimClicked);
			root.Q<Button>("confirm").clicked += onConfirmClicked;
		}

		protected override void OnDestroy()
		{
			_icons.ReleaseAll();
			base.OnDestroy();
		}

		public async UniTask ShowAsync(IReadOnlyList<GrantedReward> rewards, CancellationToken ct)
		{
			CancellationToken cellCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < rewards.Count; i++)
			{
				addCell(rewards[i], i, cellCt);
			}

			_tcs = new UniTaskCompletionSource();
			PlayPopupOpen();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		private void addCell(GrantedReward reward, int index, CancellationToken ct)
		{
			ItemCellView cell = new ItemCellView(_grid.contentContainer, _gradeColors, _icons);
			cell.Tag = index;
			cell.OnClicked += onCellClicked;
			_rewards.Add(reward);

			if (reward.type == RewardType.Currency)
			{
				cell.BindCurrencyAsync(reward.currency, reward.count, ct).Forget();
				return;
			}

			if (reward.equipment != null)
			{
				cell.BindEquipmentAsync(reward.equipment, false, ct).Forget();
				return;
			}

			cell.BindItemAsync(Table_Item.Get(reward.itemId), reward.count, ct).Forget();
		}

		// 받은 것도 이 팝업에선 조작하지 않는다 — 상점 진열과 같은 읽기 전용 경로로 연다.
		private void onCellClicked(ItemCellView sender, long uid, int itemId)
		{
			int index = (int)sender.Tag;
			GrantedReward reward = _rewards[index];

			RewardPreviewItem item;
			item.type = reward.type;
			item.itemId = reward.itemId;
			item.currency = reward.currency;
			item.count = reward.count;
			item.equipment = reward.equipment;
			ShopRewardPopup.Show(item, sender.Root, this.GetCancellationTokenOnDestroy());
		}

		private void onDimClicked(ClickEvent evt)
		{
			close();
		}

		private void onConfirmClicked()
		{
			close();
		}

		private void close()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}
	}
}
