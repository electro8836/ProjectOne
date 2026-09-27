using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.HeroPasses;
using ProjectOne.Reward;
using ProjectOne.UserData;
using UnityEngine;

namespace ProjectOne.UI
{
	// 히어로패스 팝업 Presenter — 헤더·진행도·각 슬롯의 상태를 정하고, 누른 칸(또는 모두 받기)을 수령 처리한다.
	//
	// 수령 판단은 항상 HeroPassBook 에서 다시 확인한다 — 연타나 화면이 다시 그려지기 전의 늦은 클릭이 들어와도 걸린다.
	public sealed class HeroPassPopupPresenter : Presenter<HeroPassPopup>
	{
		private readonly List<HeroPassRewardSlotData> _slotData = new List<HeroPassRewardSlotData>(50);

		// 지급 결과 버퍼 — 지급 자체는 RewardGranter 가 인벤/지갑에 반영하므로 여기선 재사용만 한다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>(4);

		private Action _onSecondTick;
		private Action<int, bool> _onClaimRequested;
		private Action _onAllReceiveRequested;

		// 마지막으로 그린 남은 분 — 표시 단위가 분이라 초가 바뀔 때마다 TMP 를 건드리지 않는다.
		private int _renderedMinute = -1;

		// 렌더 단위 취소 — 아이콘 로드가 await 라 연타하면 렌더가 겹친다.
		private CancellationTokenSource _renderCts;

		protected override void OnInitialize()
		{
			_onSecondTick = onSecondTick;
			_onClaimRequested = onClaimRequested;
			_onAllReceiveRequested = onAllReceiveRequested;

			view.OnSecondTick += _onSecondTick;
			view.OnClaimRequested += _onClaimRequested;
			view.OnAllReceiveRequested += _onAllReceiveRequested;
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnSecondTick -= _onSecondTick;
			view.OnClaimRequested -= _onClaimRequested;
			view.OnAllReceiveRequested -= _onAllReceiveRequested;
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			applyHeader();
			buildSlotData();
			await view.RenderSlotsAsync(_slotData, ct);

			// 열 때만 맞춘다 — 열려 있는 동안 수령해도 스크롤을 옮기지 않는다.
			view.ScrollToSlot(findFocusIndex());

			refreshTime();
			view.Reveal();
		}

		// ── 수령 ──────────────────────────────────────────────────────────

		private bool tryClaim(int level, bool advanced)
		{
			HeroPassBook book = Account.Instance.HeroPass;
			if (book.CanClaim(level, advanced) == false)
			{
				return false;
			}

			IReadOnlyList<Table_HeroPass.Row> levels = HeroPassCatalog.GetLevels();
			if (level > levels.Count)
			{
				return false;
			}

			Table_HeroPass.Row row = levels[level - 1];
			int groupId = advanced ? row.RewardGroupID_Advanced : row.RewardGroupID_Normal;
			if (groupId <= 0)
			{
				Debug.LogWarning($"[HeroPass] {level}레벨 {(advanced ? "추가" : "일반")} 보상 그룹이 비어 있다 — HeroPass 테이블을 확인한다.");
				return false;
			}

			_granted.Clear();
			RewardGranter.Grant(groupId, RewardContext.HeroPass, _granted);

			book.MarkClaimed(level, advanced);
			return true;
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		private void applyHeader()
		{
			HeroPassBook book = Account.Instance.HeroPass;
			int level = book.Level;
			view.SetHeader(level, book.IsPurchased);

			// 최대 레벨이면 더 채울 칸이 없다.
			if (level >= HeroPassCatalog.MaxLevel)
			{
				view.SetProgress(1f, "MAX");
				return;
			}

			int levelBase = HeroPassCatalog.GetTotalExp(level);
			int current = book.Exp - levelBase;
			int required = HeroPassCatalog.GetTotalExp(level + 1) - levelBase;

			float ratio = (required > 0) ? (float)current / required : 0f;
			view.SetProgress(ratio, current + "/" + required);
		}

		private void buildSlotData()
		{
			_slotData.Clear();

			HeroPassBook book = Account.Instance.HeroPass;
			int currentLevel = book.Level;
			bool isPurchased = book.IsPurchased;

			// 모두 받기 버튼은 받을 보상이 하나라도 있을 때만 켠다.
			bool hasClaimable = false;

			IReadOnlyList<Table_HeroPass.Row> levels = HeroPassCatalog.GetLevels();
			for (int i = 0; i < levels.Count; i++)
			{
				Table_HeroPass.Row row = levels[i];
				int level = i + 1;

				HeroPassRewardSlotData data = default(HeroPassRewardSlotData);
				data.level = level;
				data.isLocked = (isPurchased == false);

				if (currentLevel > level)
				{
					data.sliderValue = 1f;
				}
				else if (currentLevel == level)
				{
					data.sliderValue = 0.5f;
				}
				else
				{
					data.sliderValue = 0f;
				}

				data.normalGroupId = row.RewardGroupID_Normal;
				data.normalClaimed = book.IsClaimed(level, false);
				data.normalClaimable = book.CanClaim(level, false);

				data.advancedGroupId = row.RewardGroupID_Advanced;
				data.advancedClaimed = book.IsClaimed(level, true);
				data.advancedClaimable = book.CanClaim(level, true);

				if (data.normalClaimable == true || data.advancedClaimable == true)
				{
					hasClaimable = true;
				}

				_slotData.Add(data);
			}

			view.SetAllReceiveInteractable(hasClaimable);
		}

		// 받을 보상이 있는 가장 낮은 레벨 칸. 없으면 현재 레벨 칸(0레벨이면 첫 칸).
		private int findFocusIndex()
		{
			for (int i = 0; i < _slotData.Count; i++)
			{
				if (_slotData[i].normalClaimable == true || _slotData[i].advancedClaimable == true)
				{
					return i;
				}
			}

			return Math.Max(0, Account.Instance.HeroPass.Level - 1);
		}

		private void render()
		{
			applyHeader();
			buildSlotData();

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderSlotsAsync(_slotData, _renderCts.Token).Forget();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onClaimRequested(int level, bool advanced)
		{
			if (tryClaim(level, advanced) == false)
			{
				return;
			}

			render();
		}

		// 받을 수 있는 일반·추가 보상을 전부 받는다. 하나도 없으면 다시 그리지 않는다.
		private void onAllReceiveRequested()
		{
			bool claimedAny = false;

			int maxLevel = HeroPassCatalog.MaxLevel;
			for (int level = 1; level <= maxLevel; level++)
			{
				if (tryClaim(level, false) == true)
				{
					claimedAny = true;
				}

				if (tryClaim(level, true) == true)
				{
					claimedAny = true;
				}
			}

			if (claimedAny == true)
			{
				render();
			}
		}

		// 1초마다. 남은 분이 그대로면 문자열을 새로 만들지 않는다.
		private void onSecondTick()
		{
			refreshTime();
		}

		private void refreshTime()
		{
			int remaining = (int)Account.Instance.HeroPass.GetSeasonRemaining().TotalSeconds;
			if (remaining < 0)
			{
				remaining = 0;
			}

			int minute = remaining / 60;
			if (minute == _renderedMinute)
			{
				return;
			}

			// 팝업을 띄워 둔 채 시즌 경계를 넘으면 남은 시간이 다시 늘어난다 — 그 자리에서 초기화된 상태로 다시 그린다.
			if (minute > _renderedMinute && _renderedMinute >= 0)
			{
				render();
			}

			_renderedMinute = minute;
			view.SetRemainTimeText("초기화까지 " + formatRemain(remaining));
		}

		// "29일 23시간" — 하루 미만이면 "23시간 59분".
		private static string formatRemain(int totalSeconds)
		{
			int day = totalSeconds / 86400;
			int hour = (totalSeconds % 86400) / 3600;

			if (day > 0)
			{
				return day + "일 " + hour + "시간";
			}

			int minute = (totalSeconds % 3600) / 60;
			return hour + "시간 " + minute + "분";
		}
	}
}
