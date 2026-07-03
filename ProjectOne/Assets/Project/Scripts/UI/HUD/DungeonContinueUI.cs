using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Dungeon;

namespace ProjectOne.UI
{
	// 던전 사망 시 부활/귀환 선택창(Prefab_DungeonContinue).
	// 누적 보상을 슬롯으로 나열하고(DungeonResultUI 와 동일 방식), 부활(다이아 소모)/귀환 중 하나를 기다린다.
	// 게임 정지(Time.timeScale=0)는 호출부(DungeonDirector)가 담당한다.
	public class DungeonContinueUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private RewardSlot _slotPrefab;

		[Header("선택")]
		[SerializeField] private UIButton _reviveButton;   // 부활(다이아 소모)
		[SerializeField] private UIButton _returnButton;   // 로비 귀환(누적 보상 소멸)
		[SerializeField] private TMP_Text _costText;       // 부활 비용 — "x소모량"
		[SerializeField] private TMP_Text _reviveLabelText; // Text_Continue — "부활(남은횟수/총횟수)"

		// 부활 다이아 소모값 (현재 스테이지 × 150)
		private int _reviveCost;

		// 남은 부활 횟수 (0 이면 부활 불가)
		private int _remainingRevives;

		// 선택 결과 — true=부활, false=귀환
		private UniTaskCompletionSource<bool> _choiceSource;

		// 합산 중간 표현 — 대표 RewardItemId + 합산 수량 + 보너스 여부
		private struct MergedReward
		{
			public int rewardItemId;
			public int count;
			public bool isBonus;
		}

		private void Awake()
		{
			_reviveButton.OnClickEvent += onReviveClicked;
			_returnButton.OnClickEvent += onReturnClicked;
		}

		private void OnDestroy()
		{
			_reviveButton.OnClickEvent -= onReviveClicked;
			_returnButton.OnClickEvent -= onReturnClicked;
			_choiceSource?.TrySetCanceled();
		}

		// 슬롯 빌드 + 비용/부활횟수 표시. 부활(true)/귀환(false) 선택까지 대기.
		public async UniTask<bool> WaitChoiceAsync(int reviveCost, int remainingRevives, int totalRevives, CancellationToken ct)
		{
			_reviveCost = reviveCost;
			_remainingRevives = remainingRevives;
			buildSlots();
			refreshState(remainingRevives, totalRevives);

			_choiceSource = new UniTaskCompletionSource<bool>();

			using (ct.Register(onCanceled))
			{
				return await _choiceSource.Task;
			}
		}

		// 누적 보상을 등급/보너스 단위로 합산해 슬롯을 생성한다 (DungeonResultUI 와 동일 규칙).
		private void buildSlots()
		{
			IReadOnlyList<DungeonRewardResult> rewards = DungeonRunState.Instance.AccumulatedRewards;

			// 키 = (보너스여부) + (등급/종류) — 보너스는 일반과 분리, 같은 등급끼리만 합산.
			List<MergedReward> merged = new List<MergedReward>();
			Dictionary<string, int> keyIndex = new Dictionary<string, int>();
			for (int i = 0; i < rewards.Count; i++)
			{
				DungeonRewardResult r = rewards[i];
				string key = (r.IsBonus ? "B" : "N") + DungeonRewardResolver.GradeKey(r);

				int idx;
				if (keyIndex.TryGetValue(key, out idx) == true)
				{
					MergedReward m = merged[idx];
					m.count += r.Amount;
					merged[idx] = m;
				}
				else
				{
					keyIndex[key] = merged.Count;
					merged.Add(new MergedReward { rewardItemId = r.RewardItemId, count = r.Amount, isBonus = r.IsBonus });
				}
			}

			CancellationToken iconCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < merged.Count; i++)
			{
				RewardSlot slot = Instantiate(_slotPrefab, _rewardGrid);
				slot.BindAsync(merged[i].rewardItemId, merged[i].count, merged[i].isBonus, iconCt).Forget();
			}
		}

		// 비용("x소모량")/부활횟수("부활(남은/총)") 텍스트 갱신 + 부활 불가 시 버튼 비활성.
		// 부활 버튼은 남은 횟수가 있고 다이아가 충분할 때만 활성화한다.
		private void refreshState(int remainingRevives, int totalRevives)
		{
			if (_costText != null)
			{
				_costText.text = "x" + _reviveCost;
			}

			if (_reviveLabelText != null)
			{
				_reviveLabelText.text = "부활(" + remainingRevives + "/" + totalRevives + ")";
			}

			bool canAfford = CurrencyManager.Instance.GetAmount(CurrencyInfo.Dia) >= _reviveCost;
			_reviveButton.interactable = remainingRevives > 0 && canAfford;
		}

		private void onReviveClicked()
		{
			// 남은 부활 횟수 없으면 무시 (버튼 비활성 상태라 정상 흐름에선 도달하지 않음)
			if (_remainingRevives <= 0)
			{
				return;
			}

			// 다이아 소모 성공 시에만 부활 확정 (버튼 비활성 상태라 정상 흐름에선 잔액 부족에 도달하지 않음)
			if (CurrencyManager.Instance.TrySpend(CurrencyInfo.Dia, _reviveCost) == false)
			{
				return;
			}

			_choiceSource?.TrySetResult(true);
		}

		private void onReturnClicked()
		{
			_choiceSource?.TrySetResult(false);
		}

		private void onCanceled()
		{
			_choiceSource?.TrySetCanceled();
		}
	}
}
