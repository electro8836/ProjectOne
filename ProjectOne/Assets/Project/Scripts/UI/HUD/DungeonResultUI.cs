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
	// "다음 단계 도전" 또는 마을 복귀 중 하나를 기다린다.
	// 도전은 새 입장이므로 입장 횟수를 다시 소모한다 — 호출부가 남은 횟수를 보고 버튼 활성을 결정한다.
	public class DungeonResultUI : UIScreen
	{
		[Header("보상 목록")]
		[SerializeField] private Transform _rewardGrid;
		[SerializeField] private RewardSlot _slotPrefab;

		[Header("등급 색상 테이블")]
		[SerializeField] private GradeColorTable _gradeColors;

		[Header("복귀")]
		[SerializeField] private UIButton _backgroundButton;             // 보상 외 영역 클릭 → 마을
		[SerializeField] private TMP_Text _touchToContinueText;
		[SerializeField] private TMP_Text _expText;                      // 획득 경험치 표시(선택)
		[SerializeField] private float _autoReturnSeconds = 30f;

		[Header("다음 단계")]
		[SerializeField] private UIButton _nextStageButton;              // 다음 단계 도전 (입장 횟수 재소모)

		// true = 다음 단계 도전, false = 마을 복귀
		private UniTaskCompletionSource<bool> _closeSource;

		// 합산 중간 표현 — 대표 타입/아이템 + 합산 수량 + 보너스 여부
		private struct MergedReward
		{
			public int rewardType;   // RewardType 정수
			public int itemId;
			public int count;
			public bool isBonus;
		}

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

			_closeSource?.TrySetCanceled();
		}

		// 슬롯 빌드 + 카운트다운 시작. 다음 단계 도전(true) / 마을 복귀(false) 중 먼저 오는 것까지 대기.
		public async UniTask<bool> WaitAsync(IReadOnlyList<GrantedRewardDto> rewards, EDT.Dungeon dungeonType, int stage, bool canChallengeNext, CancellationToken ct)
		{
			if (rewards != null)
			{
				buildItemSlots(rewards);
			}

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
					_gradeColors, iconCt).Forget();
			}
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
			_closeSource?.TrySetResult(false);
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
