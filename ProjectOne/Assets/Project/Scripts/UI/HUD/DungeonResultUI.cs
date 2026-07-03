using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 던전 결과창(Prefab_DungeonResult). 서버가 확정한 실제 획득 보상을 슬롯으로 나열하고,
	// 배경 클릭 또는 자동복귀 카운트다운(표시 시점부터 _autoReturnSeconds) 만료 시 로비로 이동한다.
	// 서버 응답이 없으면(오프라인/실패) 누적 상자 목록으로 폴백 표시한다.
	public class DungeonResultUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private RewardSlot _slotPrefab;

		[Header("등급 색상 테이블")]
		[SerializeField] private GradeColorTable _equipmentColors;
		[SerializeField] private MaterialGradeColorTable _materialColors;
		[SerializeField] private CardSkillGradeColorTable _cardSkillColors;

		[Header("복귀")]
		[SerializeField] private UIButton _backgroundButton;             // 보상 외 영역 클릭 → 로비
		[SerializeField] private TMP_Text _touchToContinueText;
		[SerializeField] private TMP_Text _expText;                      // 획득 경험치 표시(선택)
		[SerializeField] private float _autoReturnSeconds = 30f;

		private UniTaskCompletionSource _closeSource;

		// 합산 중간 표현 — 대표 타입/아이템 + 합산 수량 + 보너스 여부
		private struct MergedReward
		{
			public int rewardType;   // 아이템 모드에서만 유효(RewardTypes)
			public int itemId;       // 아이템 모드=실제 아이템ID, 상자 모드=RewardItemId
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
		// rewards 가 있으면 서버 확정 아이템을, null 이면 누적 상자 목록을 표시한다.
		public async UniTask WaitAsync(IReadOnlyList<GrantedRewardDto> rewards, int dungeonId, bool extraCleared, CancellationToken ct)
		{
			buildSlots(rewards);
			updateExpText(dungeonId, extraCleared);

			_closeSource = new UniTaskCompletionSource();
			countdownAsync().Forget();

			using (ct.Register(onCanceled))
			{
				await _closeSource.Task;
			}
		}

		private void buildSlots(IReadOnlyList<GrantedRewardDto> rewards)
		{
			if (rewards != null)
			{
				buildItemSlots(rewards);
			}
			else
			{
				buildBoxSlots();
			}
		}

		// 서버 확정 아이템 — (보너스여부 + 타입 + 아이템ID) 로 합산해 등급 색상 슬롯 생성.
		private void buildItemSlots(IReadOnlyList<GrantedRewardDto> rewards)
		{
			List<MergedReward> merged = new List<MergedReward>();
			Dictionary<string, int> keyIndex = new Dictionary<string, int>();
			for (int i = 0; i < rewards.Count; i++)
			{
				GrantedRewardDto r = rewards[i];
				string key = (r.isBonus ? "B" : "N") + r.rewardType + "|" + r.itemId;

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
					merged.Add(new MergedReward { rewardType = r.rewardType, itemId = r.itemId, count = r.count, isBonus = r.isBonus });
				}
			}

			CancellationToken iconCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < merged.Count; i++)
			{
				RewardSlot slot = Instantiate(_slotPrefab, _rewardGrid);
				slot.BindItemAsync(merged[i].rewardType, merged[i].itemId, merged[i].count, merged[i].isBonus,
					_equipmentColors, _materialColors, _cardSkillColors, iconCt).Forget();
			}
		}

		// 폴백(서버 응답 없음) — 누적 상자 목록을 상자 단위로 합산해 표시(기존 동작).
		private void buildBoxSlots()
		{
			IReadOnlyList<DungeonRewardResult> rewards = DungeonRunState.Instance.AccumulatedRewards;

			List<MergedReward> merged = new List<MergedReward>();
			Dictionary<string, int> keyIndex = new Dictionary<string, int>();
			for (int i = 0; i < rewards.Count; i++)
			{
				DungeonRewardResult r = rewards[i];
				string key = (r.IsBonus ? "B" : "N") + r.RewardItemId;

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
					merged.Add(new MergedReward { itemId = r.RewardItemId, count = r.Amount, isBonus = r.IsBonus });
				}
			}

			CancellationToken iconCt = this.GetCancellationTokenOnDestroy();
			for (int i = 0; i < merged.Count; i++)
			{
				RewardSlot slot = Instantiate(_slotPrefab, _rewardGrid);
				slot.BindAsync(merged[i].itemId, merged[i].count, merged[i].isBonus, iconCt).Forget();
			}
		}

		// 획득 경험치 표시 — 던전 테이블의 ClearExp(+추가 클리어 시 ExtraClearExp).
		private void updateExpText(int dungeonId, bool extraCleared)
		{
			if (_expText == null)
			{
				return;
			}

			Table_Dungeon.Row row = Table_Dungeon.Get(dungeonId);
			int exp = 0;
			if (row != null)
			{
				exp = row.ClearExp + (extraCleared == true ? row.ExtraClearExp : 0);
			}

			_expText.text = "+" + exp;
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
