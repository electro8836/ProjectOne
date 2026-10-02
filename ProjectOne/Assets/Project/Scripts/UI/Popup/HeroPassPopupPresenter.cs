using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.HeroPasses;
using ProjectOne.Network;
using ProjectOne.Reward;
using ProjectOne.Shared;
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

		// 지급 결과 버퍼 — 서버 응답을 RewardGranter 로 인벤/지갑에 반영할 때 재사용한다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>(4);

		// 이번 요청으로 받을 (레벨, 추가 여부) 목록 — 응답이 오면 이 목록대로 수령 표시를 한다.
		private readonly List<int> _claimLevels = new List<int>(50);
		private readonly List<bool> _claimAdvanced = new List<bool>(50);

		// 서버 응답 대기 중 — 응답 전 재요청을 막는다(입력은 네트워크 차단막도 막는다).
		private bool _pendingClaim;
		private bool _isDisposed;

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

			EventManager.Instance.Subscribe<HeroPassExpChangedEvent>(onHeroPassExpChanged);
		}

		protected override void OnDispose()
		{
			_isDisposed = true;

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnSecondTick -= _onSecondTick;
			view.OnClaimRequested -= _onClaimRequested;
			view.OnAllReceiveRequested -= _onAllReceiveRequested;

			EventManager.Instance.Unsubscribe<HeroPassExpChangedEvent>(onHeroPassExpChanged);
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

		// 받을 수 있는 항목만 목록에 담는다 — 수령 판단은 항상 HeroPassBook 에서 다시 확인한다.
		private void collectClaim(int level, bool advanced)
		{
			if (Account.Instance.HeroPass.CanClaim(level, advanced) == false)
			{
				return;
			}

			_claimLevels.Add(level);
			_claimAdvanced.Add(advanced);
		}

		// 모은 목록을 서버에 한 번에 요청한다. 보상 지급·수령 표시는 응답에서 한다.
		private void requestClaim()
		{
			if (_claimLevels.Count == 0)
			{
				return;
			}

			HeroPassClaimRequest request = new HeroPassClaimRequest();
			request.levels = _claimLevels.ToArray();
			request.advanced = _claimAdvanced.ToArray();

			_pendingClaim = true;
			NetworkManager.Instance.RequestHeroPassClaim(request, onClaimed);
		}

		// 계정 반영은 팝업이 닫혔어도 한다 — 서버에는 이미 저장됐다. 다시 그리기만 팝업이 살아 있을 때 한다.
		private void onClaimed(bool success, HeroPassClaimResponse data, string error)
		{
			_pendingClaim = false;

			if (success == false || data == null)
			{
				Debug.LogWarning($"[HeroPass] 수령 실패 — 서버 경험치가 아직 따라오지 않았을 수 있다(바닥 드랍 미정산): {error}");
				_claimLevels.Clear();
				_claimAdvanced.Clear();
				return;
			}

			_granted.Clear();
			RewardGranter.FromServer(data.rewards, data.equipments, _granted);
			RewardGranter.ApplyAll(_granted);

			HeroPassBook book = Account.Instance.HeroPass;
			for (int i = 0; i < _claimLevels.Count; i++)
			{
				book.MarkClaimed(_claimLevels[i], _claimAdvanced[i]);
			}

			_claimLevels.Clear();
			_claimAdvanced.Clear();

			if (_isDisposed == true)
			{
				return;
			}

			render();
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
			if (_pendingClaim == true || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			_claimLevels.Clear();
			_claimAdvanced.Clear();
			collectClaim(level, advanced);
			requestClaim();
		}

		// 받을 수 있는 일반·추가 보상을 전부 모아 한 번에 받는다. 하나도 없으면 보내지 않는다.
		private void onAllReceiveRequested()
		{
			if (_pendingClaim == true || NetworkManager.Instance.IsLoggedIn == false)
			{
				return;
			}

			_claimLevels.Clear();
			_claimAdvanced.Clear();

			int maxLevel = HeroPassCatalog.MaxLevel;
			for (int level = 1; level <= maxLevel; level++)
			{
				collectClaim(level, false);
				collectClaim(level, true);
			}

			requestClaim();
		}

		// 열어 둔 채 사냥해도 진행도가 따라온다. 레벨이 바뀌면 새로 받을 칸이 생기므로 슬롯까지 다시 그린다.
		// 수령 응답 대기 중이면 슬롯은 응답 후 render 가 다시 그린다.
		private void onHeroPassExpChanged(HeroPassExpChangedEvent e)
		{
			applyHeader();

			if (e.Level != e.PrevLevel && _pendingClaim == false)
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
