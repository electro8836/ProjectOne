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
	// 던전 결과창(UIPrefab_DungeonResult). 서버가 확정한 실제 획득 보상을 슬롯으로 나열하고,
	// 다시 도전 / 다음 단계 / 마을 복귀 중 하나를 기다린다.
	//
	// 재도전과 다음 단계는 둘 다 **새 입장**이라 입장 횟수를 다시 소모한다 — 남은 횟수가 0이면
	// 두 버튼을 숨기지 않고 interactable 만 끈다(UIButton 이 회색 틴트를 처리한다).
	//
	// 딤 클릭으로 나가는 경로는 없앴다. 나가려면 ExitButton 을 누르거나 자동 복귀를 기다려야 한다.
	public class DungeonResultUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private ItemSlot _slotPrefab;

		[Header("등급 색상 테이블")]
		[SerializeField] private ItemGradeColorTable _gradeColors;

		[Header("정보")]
		[SerializeField] private TMP_Text _dungeonNameText;
		[SerializeField] private TMP_Text _dungeonStageText;
		[SerializeField] private TMP_Text _expText;
		[SerializeField] private TMP_Text _enterCountText;
		[SerializeField] private TMP_Text _returnTownText;

		[Header("버튼")]
		[SerializeField] private UIButton _retryButton;
		[SerializeField] private UIButton _nextButton;
		[SerializeField] private UIButton _exitButton;

		[Header("자동 복귀")]
		[SerializeField] private float _autoReturnSeconds = 30f;

		// 남은 입장 횟수가 0임을 알리는 색. 중앙 색상 테이블이 없어 화면이 직접 들고 있는다.
		private static readonly Color EnterCountEmptyColor = new Color32(0xE0, 0x4B, 0x4B, 0xFF);
		private static readonly Color EnterCountNormalColor = Color.white;

		// 플레이어가 고른 다음 행동. 자동 복귀는 ReturnTown 으로 확정된다.
		private UniTaskCompletionSource<DungeonResultAction> _closeSource;

		// 카운트다운 전용 수명. 버튼을 누르는 순간 끊어 자동 복귀가 뒤늦게 끼어들지 못하게 한다.
		private CancellationTokenSource _countdownCts;

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
			_retryButton.OnClickEvent += onRetryClicked;
			_nextButton.OnClickEvent += onNextClicked;
			_exitButton.OnClickEvent += onExitClicked;
		}

		private void OnDestroy()
		{
			_retryButton.OnClickEvent -= onRetryClicked;
			_nextButton.OnClickEvent -= onNextClicked;
			_exitButton.OnClickEvent -= onExitClicked;

			stopCountdown();

			unbindSlots();
			_closeSource?.TrySetCanceled();
		}

		// 슬롯 빌드 + 정보 표시 + 카운트다운 시작. 버튼 선택이나 자동 복귀 중 먼저 오는 것까지 대기.
		//
		// equipments 는 이번 판에 실제로 지급된 장비 인스턴스다.
		// 등급·레벨·품질은 테이블이 아니라 인스턴스가 소유하므로(아이템 설계 4장) 따로 받는다.
		//
		// gainedExp 는 이번 판에서 실제로 오른 경험치다 — 단계 고정 보상이 아니라 Director 가 재는 값이다.
		public async UniTask<DungeonResultAction> WaitAsync(IReadOnlyList<GrantedRewardDto> rewards, IReadOnlyList<EquipmentInstance> equipments,
			EDT.Dungeon dungeonType, int stage, int gainedExp, bool hasNextStage, CancellationToken ct)
		{
			buildSlots(rewards, equipments);

			applyInfo(dungeonType, stage, gainedExp, hasNextStage);

			_closeSource = new UniTaskCompletionSource<DungeonResultAction>();

			_countdownCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
			countdownAsync(_countdownCts.Token).Forget();

			using (ct.Register(onCanceled))
			{
				return await _closeSource.Task;
			}
		}

		// 던전 이름·단계·획득 경험치·남은 입장 횟수와 그에 따른 버튼 활성 상태를 한 번에 채운다.
		private void applyInfo(EDT.Dungeon dungeonType, int stage, int gainedExp, bool hasNextStage)
		{
			Table_Dungeon.Row dungeon = Table_Dungeon.Get(dungeonType);

			if (_dungeonNameText != null)
			{
				_dungeonNameText.text = ((dungeon != null) ? dungeon.Name : string.Empty) + " 클리어!";
			}

			if (_dungeonStageText != null)
			{
				_dungeonStageText.text = "스테이지 " + stage;
			}

			if (_expText != null)
			{
				_expText.text = "경험치+" + gainedExp;
			}

			// 재도전·다음 단계는 새 입장이라 남은 횟수를 다시 쓴다.
			int remaining = ProjectOne.Dungeon.DungeonProgress.GetRemainingCount(dungeonType);

			if (_enterCountText != null)
			{
				_enterCountText.text = "남은 입장 횟수 " + remaining + "회";
				_enterCountText.color = (remaining <= 0) ? EnterCountEmptyColor : EnterCountNormalColor;
			}

			// 숨기지 않고 잠근다 — 버튼 줄이 GridLayoutGroup 이라 하나를 빼면 나머지가 재배치된다.
			_retryButton.interactable = (remaining > 0);
			_nextButton.interactable = (remaining > 0 && hasNextStage == true);
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

		private async UniTaskVoid countdownAsync(CancellationToken ct)
		{
			int remaining = Mathf.CeilToInt(_autoReturnSeconds);
			while (remaining > 0)
			{
				updateReturnTownText(remaining);

				bool cancelled = await UniTask.Delay(System.TimeSpan.FromSeconds(1), cancellationToken: ct).SuppressCancellationThrow();
				if (cancelled == true)
				{
					return;
				}

				remaining -= 1;
			}

			updateReturnTownText(0);
			_closeSource?.TrySetResult(DungeonResultAction.ReturnTown);
		}

		// 카운트다운을 끊는다. 버튼을 누른 뒤 남은 대기가 뒤늦게 깨어나 마을행을 또 확정하면 안 된다.
		private void stopCountdown()
		{
			if (_countdownCts == null)
			{
				return;
			}

			_countdownCts.Cancel();
			_countdownCts.Dispose();
			_countdownCts = null;
		}

		private void updateReturnTownText(int seconds)
		{
			if (_returnTownText != null)
			{
				_returnTownText.text = seconds + "초 후 마을로 이동합니다.";
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

		private void onRetryClicked()
		{
			finish(DungeonResultAction.Retry);
		}

		private void onNextClicked()
		{
			finish(DungeonResultAction.NextStage);
		}

		private void onExitClicked()
		{
			finish(DungeonResultAction.ReturnTown);
		}

		// 버튼 세 개의 공통 마무리 — 카운트다운을 끊고 안내를 걷은 뒤 결과를 확정한다.
		private void finish(DungeonResultAction action)
		{
			stopCountdown();

			if (_returnTownText != null)
			{
				_returnTownText.gameObject.SetActive(false);
			}

			_closeSource?.TrySetResult(action);
		}

		private void onCanceled()
		{
			_closeSource?.TrySetCanceled();
		}
	}
}
