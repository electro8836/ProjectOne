using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.DailyBonuses;
using ProjectOne.Reward;
using ProjectOne.UserData;
using ProjectOne.Utils;
using UnityEngine;

namespace ProjectOne.UI
{
	// 출석 팝업 Presenter — 각 칸의 상태를 정하고, 사용자가 누른 칸을 수령 처리한다.
	//
	// **수령은 사용자가 Focus 가 켜진 칸을 눌러야 일어난다.** 열자마자 자동으로 주면
	// 무엇을 받았는지 보이지 않아 수령 연출이 성립하지 않는다.
	// 받고 나면 그 칸은 Check 로 바뀌고, 같은 칸을 다시 누르면 슬롯 정보 팝업이 뜬다
	// (슬롯이 "지금 받을 수 있는가" 하나만 기억하므로 자연히 그렇게 갈린다).
	//
	// 주간·월간은 각각 눌러서 받는다 — 탭마다 따로 수령하는 것이 출석 UI 의 보통 동작이다.
	public sealed class DailyBonusPopupPresenter : Presenter<DailyBonusPopup>
	{
		private readonly List<DailyBonusSlotData> _slotData = new List<DailyBonusSlotData>(30);

		// 지급 결과 버퍼 — 지급 자체는 RewardGranter 가 인벤/지갑에 반영하므로 여기선 재사용만 한다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>(4);

		private Action _onSecondTick;
		private Action<DailyBonusType> _onTabSelected;
		private Action<int> _onClaimRequested;

		// 마지막으로 그린 남은 분 — 표시 단위가 분이라 초가 바뀔 때마다 TMP 를 건드리지 않는다.
		private int _renderedMinute = -1;

		private DailyBonusType _currentType = DailyBonusType.Week;

		// 렌더 단위 취소 — 아이콘 로드가 await 라 탭을 연타하면 렌더가 겹친다.
		private CancellationTokenSource _renderCts;

		protected override void OnInitialize()
		{
			_onSecondTick = onSecondTick;
			_onTabSelected = onTabSelected;
			_onClaimRequested = onClaimRequested;

			view.OnSecondTick += _onSecondTick;
			view.OnTabSelected += _onTabSelected;
			view.OnClaimRequested += _onClaimRequested;
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
			view.OnTabSelected -= _onTabSelected;
			view.OnClaimRequested -= _onClaimRequested;
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			_currentType = DailyBonusType.Week;
			buildSlotData(_currentType);

			// TabGroup.Select 는 OnTabChanged 를 쏘지 않는다 — 첫 렌더는 직접 부른다.
			view.SelectWeekTab();
			await view.RenderAsync(_currentType, _slotData, ct);

			refreshTime();
			view.Reveal();
		}

		// ── 수령 ──────────────────────────────────────────────────────────

		// 누른 칸이 정말 지금 받을 수 있는 칸인지 다시 확인하고 지급한다.
		// 연타나 화면이 다시 그려지기 전의 늦은 클릭이 두 번 들어와도 여기서 걸린다.
		private bool tryClaim(DailyBonusType type, int dayCount)
		{
			DailyBonusBook book = Account.Instance.DailyBonus;
			if (book.CanClaim(type) == false)
			{
				return false;
			}

			if (book.GetDayCount(type) != dayCount)
			{
				return false;
			}

			int cycleLength = DailyBonusCatalog.GetCycleLength(type);
			if (cycleLength <= 0)
			{
				return false;
			}

			Table_DailyBonus.Row row = DailyBonusCatalog.GetDay(type, dayCount);
			if (row == null)
			{
				Debug.LogWarning($"[DailyBonus] {type} {dayCount}일차 행이 없다 — DailyBonus 테이블을 확인한다.");
				return false;
			}

			_granted.Clear();
			RewardGranter.Grant(row.RewardGroupID, RewardContext.DailyBonus, _granted);

			book.MarkClaimed(type, cycleLength);
			return true;
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// Check 는 "이번 주기에서 이미 받은 칸", Focus 는 "오늘의 칸" 이다.
		// 셋이 서로 다른 뜻이라 따로 계산한다 — 특히 Focus 는 받고 나서도 그 칸에 남고,
		// 날이 바뀌어야 다음 칸으로 옮겨간다.
		private void buildSlotData(DailyBonusType type)
		{
			_slotData.Clear();

			DailyBonusBook book = Account.Instance.DailyBonus;

			int nextDay = book.GetDayCount(type);
			bool canClaim = book.CanClaim(type);

			// GetDayCount 는 "다음에 받을 일차" 라 오늘 몫을 받고 나면 이미 한 칸 앞서 있다 —
			// 오늘의 칸은 그만큼 되돌린 자리다. 주기 끝을 받았으면 1 로 되감겨 있으므로 마지막 일차로 돌린다.
			int cycleLength = DailyBonusCatalog.GetCycleLength(type);
			int focusDay = nextDay;
			if (canClaim == false)
			{
				focusDay = (nextDay <= 1) ? cycleLength : (nextDay - 1);
			}

			// 오늘 받은 칸. 주기 끝을 받으면 nextDay 가 1 로 되감겨 "받은 칸보다 앞" 판정이
			// 아무 칸도 잡지 못하므로, 그 칸만 따로 Check 를 살린다.
			int claimedDay = canClaim ? 0 : focusDay;

			IReadOnlyList<Table_DailyBonus.Row> days = DailyBonusCatalog.GetDays(type);
			for (int i = 0; i < days.Count; i++)
			{
				Table_DailyBonus.Row row = days[i];

				DailyBonusSlotData data = default(DailyBonusSlotData);
				data.dayCount = row.DayCount;
				data.rewardGroupId = row.RewardGroupID;
				data.isClaimed = (row.DayCount < nextDay) || (row.DayCount == claimedDay);
				data.isFocused = (row.DayCount == focusDay);
				data.isClaimable = (row.DayCount == nextDay) && canClaim;

				_slotData.Add(data);
			}
		}

		private void render(DailyBonusType type)
		{
			buildSlotData(type);

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderAsync(type, _slotData, _renderCts.Token).Forget();
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onTabSelected(DailyBonusType type)
		{
			_currentType = type;
			render(_currentType);
		}

		// Focus 가 켜진 칸을 눌렀다.
		private void onClaimRequested(int dayCount)
		{
			if (tryClaim(_currentType, dayCount) == false)
			{
				return;
			}

			render(_currentType);
		}

		// 1초마다. 남은 분이 그대로면 문자열을 새로 만들지 않는다.
		private void onSecondTick()
		{
			refreshTime();
		}

		private void refreshTime()
		{
			int remaining = (int)DailyReset.GetRemaining().TotalSeconds;
			if (remaining < 0)
			{
				remaining = 0;
			}

			int minute = remaining / 60;
			if (minute == _renderedMinute)
			{
				return;
			}

			// 팝업을 띄워 둔 채 경계를 넘으면 그 자리에서 다음 칸에 Focus 가 켜져야 한다.
			if (minute > _renderedMinute && _renderedMinute >= 0)
			{
				render(_currentType);
			}

			_renderedMinute = minute;
			view.SetTimeText("다음 보상까지 : " + formatRemain(remaining));
		}

		// "18시간 42분" — 두 단위 고정. BuffSlot.formatRemain(큰 단위 하나)과 축이 달라 공용화하지 않는다.
		private static string formatRemain(int totalSeconds)
		{
			int hour = totalSeconds / 3600;
			int minute = (totalSeconds % 3600) / 60;

			if (hour <= 0)
			{
				return minute + "분";
			}

			return hour + "시간 " + minute + "분";
		}
	}
}
