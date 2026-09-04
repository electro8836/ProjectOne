using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Items;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 던전 결과창(Prefab_DungeonResult). 서버가 확정한 실제 획득 보상을 슬롯으로 나열하고,
	// "다음 단계 도전" 또는 마을 복귀 중 하나를 기다린다.
	// 도전은 새 입장이므로 입장 횟수를 다시 소모한다 — 호출부가 남은 횟수를 보고 버튼 활성을 결정한다.
	public class DungeonResultUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private ItemSlot _slotPrefab;

		[Header("등급 색상 테이블")]
		[SerializeField] private ItemGradeColorTable _gradeColors;

		[Header("복귀")]
		[SerializeField] private UIButton _backgroundButton;             // 보상 외 영역 클릭 → 마을
		[SerializeField] private TMP_Text _touchToContinueText;
		[SerializeField] private TMP_Text _expText;                      // 획득 경험치 표시(선택)
		[SerializeField] private float _autoReturnSeconds = 30f;

		[Header("다음 단계")]
		[SerializeField] private UIButton _nextStageButton;              // 다음 단계 도전 (입장 횟수 재소모)

		// true = 다음 단계 도전, false = 마을 복귀
		private UniTaskCompletionSource<bool> _closeSource;

		// 합산 중간 표현 — 대표 타입/아이템 + 합산 수량
		private struct MergedReward
		{
			public int rewardType;   // RewardType 정수
			public int itemId;
			public int count;

			// 합산 리스트에 처음 들어간 순번. 같은 분류 안에서 순서를 고정하는 2차 정렬 키다.
			public int order;
		}

		// 재화는 ItemMainCategory 에 속하지 않는다 — 모든 분류 뒤로 보낸다.
		private const int CurrencySortKey = 1000;

		// 테이블에 없는 아이템. bindSlot 이 어차피 슬롯을 걷어내므로 자리만 잡아 준다.
		private const int UnknownSortKey = 999;

		private const string EQUIPMENT_POPUP_ADDRESS = "UIPrefab_EquipmentPopup";
		private const string CONSUMABLE_POPUP_ADDRESS = "UIPrefab_ConsumablePopup";

		// 슬롯이 무엇을 들고 있는지 — 클릭 시 어느 팝업을 열지 정하는 근거다.
		// 슬롯을 만든 쪽만 아는 정보라 (uid, itemId) 로 되짚어 추측하지 않고 여기에 남긴다.
		private readonly Dictionary<ItemSlot, EquipmentInstance> _slotEquipments = new Dictionary<ItemSlot, EquipmentInstance>();
		private readonly Dictionary<ItemSlot, MergedReward> _slotRewards = new Dictionary<ItemSlot, MergedReward>();

		private void Awake()
		{
			_backgroundButton.OnClickEvent += onBackgroundClicked;
			if (_nextStageButton != null)
			{
				_nextStageButton.OnClickEvent += onNextStageClicked;
			}
		}

		private void OnDestroy()
		{
			_backgroundButton.OnClickEvent -= onBackgroundClicked;
			if (_nextStageButton != null)
			{
				_nextStageButton.OnClickEvent -= onNextStageClicked;
			}

			unbindSlots();
			_closeSource?.TrySetCanceled();
		}

		// 슬롯 빌드 + 카운트다운 시작. 다음 단계 도전(true) / 마을 복귀(false) 중 먼저 오는 것까지 대기.
		// equipments 는 이번 판에 실제로 지급된 장비 인스턴스다.
		// 등급·레벨·품질은 테이블이 아니라 인스턴스가 소유하므로(아이템 설계 4장) 따로 받는다.
		public async UniTask<bool> WaitAsync(IReadOnlyList<GrantedRewardDto> rewards, IReadOnlyList<EquipmentInstance> equipments,
			EDT.Dungeon dungeonType, int stage, bool canChallengeNext, CancellationToken ct)
		{
			buildSlots(rewards, equipments);

			updateExpText(dungeonType, stage);

			if (_nextStageButton != null)
			{
				_nextStageButton.gameObject.SetActive(canChallengeNext);
			}

			_closeSource = new UniTaskCompletionSource<bool>();
			countdownAsync().Forget();

			using (ct.Register(onCanceled))
			{
				return await _closeSource.Task;
			}
		}

		// 장비 슬롯을 먼저, 그 다음 합산 슬롯.
		//
		// 이 배치가 곧 정렬이다 — Equipment 가 ItemMainCategory 의 첫 실항목이라
		// 장비를 앞에 그리면 "MainCategory 순 + 재화 맨 뒤" 가 성립한다.
		// **enum 앞에 다른 분류가 끼면 여기가 깨진다.**
		private void buildSlots(IReadOnlyList<GrantedRewardDto> rewards, IReadOnlyList<EquipmentInstance> equipments)
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();

			// 장비는 합산하지 않는다 — 인스턴스마다 등급·품질이 달라 합치면 그 정보가 사라진다.
			if (equipments != null)
			{
				for (int i = 0; i < equipments.Count; i++)
				{
					EquipmentInstance instance = equipments[i];
					if (instance == null)
					{
						continue;
					}

					ItemSlot slot = Instantiate(_slotPrefab, _rewardGrid);
					slot.BindEquipmentAsync(instance, false, _gradeColors, ct).Forget();

					_slotEquipments.Add(slot, instance);
					slot.OnClicked += onSlotClicked;
				}
			}

			if (rewards != null)
			{
				buildMergedSlots(rewards, ct);
			}
		}

		// 스택 아이템·재화 — (타입 + 아이템ID) 로 합산해 슬롯을 만든다.
		private void buildMergedSlots(IReadOnlyList<GrantedRewardDto> rewards, CancellationToken ct)
		{
			List<MergedReward> merged = new List<MergedReward>();
			Dictionary<string, int> keyIndex = new Dictionary<string, int>();
			for (int i = 0; i < rewards.Count; i++)
			{
				GrantedRewardDto r = rewards[i];

				// 장비는 위에서 인스턴스로 이미 그렸다 — 여기서 또 그리면 슬롯이 두 벌이 된다.
				if (Table_Equipment.Get(r.itemId) != null)
				{
					continue;
				}

				string key = r.rewardType + "|" + r.itemId;

				int idx;
				if (keyIndex.TryGetValue(key, out idx) == true)
				{
					MergedReward m = merged[idx];
					m.count += r.count;
					merged[idx] = m;
				}
				else
				{
					keyIndex[key] = merged.Count;
					merged.Add(new MergedReward { rewardType = r.rewardType, itemId = r.itemId, count = r.count, order = merged.Count });
				}
			}

			merged.Sort(compareMerged);

			for (int i = 0; i < merged.Count; i++)
			{
				bindSlot(merged[i], ct);
			}
		}

		// 분류 순(ItemMainCategory) → 재화 순으로 세운다.
		private static int compareMerged(MergedReward a, MergedReward b)
		{
			int ka = sortKey(a);
			int kb = sortKey(b);
			if (ka != kb)
			{
				return ka.CompareTo(kb);
			}

			// 같은 분류 안에서는 들어온 순서를 지킨다 — List.Sort 는 불안정 정렬이다.
			return a.order.CompareTo(b.order);
		}

		private static int sortKey(MergedReward reward)
		{
			if ((RewardType)reward.rewardType == RewardType.Currency)
			{
				return CurrencySortKey;
			}

			Table_Item.Row row = Table_Item.Get(reward.itemId);
			return (row != null) ? (int)row.MainCategory : UnknownSortKey;
		}

		// 스택 보상 한 칸. 재화는 등급·분류·강화가 없어 슬롯을 다르게 채운다.
		private void bindSlot(MergedReward reward, CancellationToken ct)
		{
			ItemSlot slot = Instantiate(_slotPrefab, _rewardGrid);

			_slotRewards.Add(slot, reward);
			slot.OnClicked += onSlotClicked;

			if ((RewardType)reward.rewardType == RewardType.Currency)
			{
				slot.BindCurrencyAsync((EDT.Currency)reward.itemId, reward.count, _gradeColors, ct).Forget();
				return;
			}

			// 장비·재료·소모품이 Item 테이블 하나로 통합되어 등급 축도 Item.Grade 하나뿐이다.
			Table_Item.Row row = Table_Item.Get(reward.itemId);
			if (row == null)
			{
				Debug.LogWarning($"[DungeonResultUI] Item {reward.itemId} 가 테이블에 없습니다 — 보상 슬롯을 건너뜁니다.");
				slot.OnClicked -= onSlotClicked;
				_slotRewards.Remove(slot);
				Destroy(slot.gameObject);
				return;
			}

			slot.BindItemAsync(row, reward.count, _gradeColors, ct).Forget();
		}

		// 획득 경험치 표시 — 경험치는 던전이 아니라 단계가 소유한다 (DungeonStage.RewardExp).
		private void updateExpText(EDT.Dungeon dungeonType, int stage)
		{
			if (_expText == null)
			{
				return;
			}

			Table_DungeonStage.Row row = ProjectOne.Dungeon.DungeonProgress.FindStageRow(dungeonType, stage);
			int exp = (row != null) ? row.RewardExp : 0;
			_expText.text = "경험치 +" + exp;
		}

		private async UniTaskVoid countdownAsync()
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();
			int remaining = Mathf.CeilToInt(_autoReturnSeconds);
			while (remaining > 0)
			{
				updateTouchText(remaining);

				bool cancelled = await UniTask.Delay(System.TimeSpan.FromSeconds(1), cancellationToken: ct).SuppressCancellationThrow();
				if (cancelled == true)
				{
					return;
				}

				remaining -= 1;
			}

			updateTouchText(0);
			_closeSource?.TrySetResult(false);
		}

		private void updateTouchText(int seconds)
		{
			if (_touchToContinueText != null)
			{
				_touchToContinueText.text = "화면을 터치하면 마을로 이동합니다.\n(" + seconds + "초 후 이동)";
			}
		}

		// 보상 슬롯 클릭 — 전부 **디스플레이(읽기 전용)** 경로다. 내가 조작할 수 있는 목록이 아니다.
		private void onSlotClicked(ItemSlot sender, long uid, int itemId)
		{
			CancellationToken ct = this.GetCancellationTokenOnDestroy();

			EquipmentInstance instance;
			if (_slotEquipments.TryGetValue(sender, out instance) == true)
			{
				UIManager.Instance.ShowItemInfoPopupAsync(EQUIPMENT_POPUP_ADDRESS, instance, ct).Forget();
				return;
			}

			MergedReward reward;
			if (_slotRewards.TryGetValue(sender, out reward) == false)
			{
				return;
			}

			if ((RewardType)reward.rewardType == RewardType.Currency)
			{
				Table_Currency.Row currency = Table_Currency.Get((EDT.Currency)reward.itemId);
				if (currency != null)
				{
					showSimple(currency.Name, currency.Desc, sender, ct);
				}

				return;
			}

			Table_Item.Row row = Table_Item.Get(reward.itemId);
			if (row == null)
			{
				return;
			}

			// 재료는 보여줄 것이 이름·설명뿐이라 정식 팝업을 열지 않는다.
			if (row.MainCategory == ItemMainCategory.Material)
			{
				showSimple(row.Name, row.Desc, sender, ct);
				return;
			}

			UIManager.Instance.ShowConsumablePopupAsync(CONSUMABLE_POPUP_ADDRESS, reward.itemId, true, ct).Forget();
		}

		private static void showSimple(string name, string desc, ItemSlot anchor, CancellationToken ct)
		{
			UIManager.Instance.ShowSimplePopupAsync(name + "\n" + desc, anchor.transform as RectTransform, ct).Forget();
		}

		private void unbindSlots()
		{
			Dictionary<ItemSlot, EquipmentInstance>.Enumerator e = _slotEquipments.GetEnumerator();
			while (e.MoveNext() == true)
			{
				if (e.Current.Key != null)
				{
					e.Current.Key.OnClicked -= onSlotClicked;
				}
			}

			Dictionary<ItemSlot, MergedReward>.Enumerator r = _slotRewards.GetEnumerator();
			while (r.MoveNext() == true)
			{
				if (r.Current.Key != null)
				{
					r.Current.Key.OnClicked -= onSlotClicked;
				}
			}

			_slotEquipments.Clear();
			_slotRewards.Clear();
		}

		private void onBackgroundClicked()
		{
			_closeSource?.TrySetResult(false);
		}

		private void onNextStageClicked()
		{
			_closeSource?.TrySetResult(true);
		}

		private void onCanceled()
		{
			_closeSource?.TrySetCanceled();
		}
	}
}
