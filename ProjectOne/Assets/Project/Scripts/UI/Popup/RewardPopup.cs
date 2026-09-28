using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using EDT;
using ProjectOne.Reward;

namespace ProjectOne.UI
{
	// 공용 보상 팝업 — 방금 지급된 보상(GrantedReward 목록)을 아이템 슬롯으로 늘어놓고, Dim 을 누르면 닫힌다.
	// 슬롯을 누르면 그 보상의 정보 팝업(장비·소모품·설명)을 위에 띄운다.
	//
	// UIManager.ShowRewardPopupAsync 가 ShowAsync 로 닫힘을 기다린다. 표시 전용이라 Presenter 를 두지 않는다.
	// 지급은 호출자가 끝낸 뒤 부른다 — 이 팝업은 인벤/지갑에 손대지 않는다.
	public class RewardPopup : UIScreen
	{
		[SerializeField] private UIButton _dimButton;					// Dimmed
		[SerializeField] private RectTransform _grid;					// Reward_ScrollRect/Veiwport/Content/Grid
		[SerializeField] private ItemSlot _slotPrefab;					// UIPrefab_ItemSlot
		[SerializeField] private ItemGradeColorTable _gradeColors;		// 등급 색상 SO

		// 만든 슬롯과 그 보상 — 같은 인덱스끼리 짝이다. 슬롯 클릭 시 무엇을 보여줄지 여기서 찾는다.
		private readonly List<ItemSlot> _slots = new List<ItemSlot>();
		private readonly List<GrantedReward> _rewards = new List<GrantedReward>();

		private UniTaskCompletionSource _tcs;

		private void Awake()
		{
			_dimButton.OnClickEvent += onDimClicked;
		}

		private void OnDestroy()
		{
			_dimButton.OnClickEvent -= onDimClicked;

			for (int i = 0; i < _slots.Count; i++)
			{
				_slots[i].OnClicked -= onSlotClicked;
			}
		}

		// 슬롯을 모두 채우고 Dim 이 눌릴 때까지 돌아오지 않는다.
		public async UniTask ShowAsync(IReadOnlyList<GrantedReward> rewards, CancellationToken ct)
		{
			CancellationToken slotCt = this.GetCancellationTokenOnDestroy();

			for (int i = 0; i < rewards.Count; i++)
			{
				bindSlot(rewards[i], slotCt);
			}

			_tcs = new UniTaskCompletionSource();
			await _tcs.Task.AttachExternalCancellation(ct).SuppressCancellationThrow();
		}

		// 재화·장비·스택 아이템을 ItemSlot 의 해당 바인딩으로 보낸다.
		private void bindSlot(GrantedReward reward, CancellationToken ct)
		{
			ItemSlot slot = Instantiate(_slotPrefab, _grid);
			slot.OnClicked += onSlotClicked;
			_slots.Add(slot);
			_rewards.Add(reward);

			if (reward.type == RewardType.Currency)
			{
				slot.BindCurrencyAsync(reward.currency, reward.count, _gradeColors, ct).Forget();
				return;
			}

			if (reward.equipment != null)
			{
				slot.BindEquipmentAsync(reward.equipment, false, _gradeColors, ct).Forget();
				return;
			}

			slot.BindItemAsync(Table_Item.Get(reward.itemId), reward.count, _gradeColors, ct).Forget();
		}

		// 받은 보상이 무엇인지 보여준다 — 퀘스트·출석 보상 칸과 같은 읽기 전용 경로를 쓴다.
		private void onSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			int index = _slots.IndexOf(sender);
			if (index < 0)
			{
				return;
			}

			GrantedReward reward = _rewards[index];

			RewardPreviewItem item;
			item.type = reward.type;
			item.itemId = reward.itemId;
			item.currency = reward.currency;
			item.count = reward.count;
			item.equipment = reward.equipment;

			ShopRewardPopup.Show(item, sender.transform as RectTransform, this.GetCancellationTokenOnDestroy());
		}

		private void onDimClicked()
		{
			if (_tcs != null)
			{
				_tcs.TrySetResult();
			}
		}
	}
}
