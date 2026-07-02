using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using ProjectOne.Dungeon;

namespace ProjectOne.UI
{
	// 던전 결과창(Prefab_DungeonResult). 누적 보상을 슬롯으로 나열하고,
	// 배경 클릭 또는 자동복귀 카운트다운(표시 시점부터 _autoReturnSeconds) 만료 시 로비로 이동한다.
	public class DungeonResultUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private RewardSlot _slotPrefab;

		[Header("복귀")]
		[SerializeField] private UIButton _backgroundButton;             // 보상 외 영역 클릭 → 로비
		[SerializeField] private TMP_Text _touchToContinueText;
		[SerializeField] private float _autoReturnSeconds = 30f;

		private UniTaskCompletionSource _closeSource;

		// 합산 중간 표현 — 대표 RewardItemId + 합산 수량 + 보너스 여부
		private struct MergedReward
		{
			public int rewardItemId;
			public int count;
			public bool isBonus;
		}

		private void Awake()
		{
			_backgroundButton.OnClickEvent += onBackgroundClicked;
		}

		private void OnDestroy()
		{
			_backgroundButton.OnClickEvent -= onBackgroundClicked;
			_closeSource?.TrySetCanceled();
		}

		// 슬롯 빌드 + 카운트다운 시작. 배경 클릭/카운트다운 만료 중 먼저 오는 것까지 대기.
		public async UniTask WaitAsync(CancellationToken ct)
		{
			buildSlots();

			_closeSource = new UniTaskCompletionSource();
			countdownAsync().Forget();

			using (ct.Register(onCanceled))
			{
				await _closeSource.Task;
			}
		}

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
			_closeSource?.TrySetResult();
		}

		private void updateTouchText(int seconds)
		{
			if (_touchToContinueText != null)
			{
				_touchToContinueText.text = "화면을 터치하면 로비로 이동합니다\n(" + seconds + "초 후 이동)";
			}
		}

		private void onBackgroundClicked()
		{
			_closeSource?.TrySetResult();
		}

		private void onCanceled()
		{
			_closeSource?.TrySetCanceled();
		}
	}
}
