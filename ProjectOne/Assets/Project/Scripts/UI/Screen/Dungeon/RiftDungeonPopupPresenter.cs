using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Dungeon;
using ProjectOne.Map;
using ProjectOne.Network;
using ProjectOne.Reward;
using ProjectOne.Shared;
using ProjectOne.Utils;
using UnityEngine;

namespace ProjectOne.UI
{
	// 균열던전 팝업 Presenter — 현황 계산, 균열 스킬 선택, 입장·소탕 판정을 담당한다.
	//
	// 기록·체크포인트·보상 계산은 DungeonProgress 가 소유한다. 여기서는 그 값을 화면용 문자열로 옮기기만 한다.
	public sealed class RiftDungeonPopupPresenter : Presenter<RiftDungeonPopup>
	{
		private const EDT.Dungeon DUNGEON_TYPE = EDT.Dungeon.Rift;

		// 스킬 정렬 버퍼 — Dictionary 는 순서를 보장하지 않아 칸 순서가 흔들린다.
		private readonly List<Table_RiftSkill.Row> _skills = new List<Table_RiftSkill.Row>();
		private readonly List<RiftSkillSlotData> _slotData = new List<RiftSkillSlotData>();

		private Table_RiftSkill.Row _selected;

		// 소탕 지급 목록 — 보상 팝업이 닫힐 때까지 읽으므로 다음 소탕 전까지 유지한다.
		private readonly List<GrantedReward> _granted = new List<GrantedReward>();

		// 마지막으로 그린 남은 초 — 매초 문자열을 새로 만들지 않기 위해 변화가 있을 때만 갱신한다.
		private int _lastRefreshSecond = -1;

		// 서버 소탕 응답 대기 중 — 연타로 횟수가 두 번 빠지지 않게 막는다.
		private bool _sweeping;
		private bool _isDisposed;

		protected override void OnInitialize()
		{
			view.OnSkillSelected += onSkillSelected;
			view.OnSkillInfoClicked += onSkillInfoClicked;
			view.OnEnterClicked += onEnterClicked;
			view.OnSweepClicked += onSweepClicked;
			view.OnSecondTick += onSecondTick;
		}

		protected override void OnDispose()
		{
			_isDisposed = true;

			view.OnSkillSelected -= onSkillSelected;
			view.OnSkillInfoClicked -= onSkillInfoClicked;
			view.OnEnterClicked -= onEnterClicked;
			view.OnSweepClicked -= onSweepClicked;
			view.OnSecondTick -= onSecondTick;
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			renderInfo();

			buildSkills();
			view.RenderSkills(_slotData);

			// 기본 선택은 첫 번째 스킬이다.
			if (_skills.Count > 0)
			{
				onSkillSelected(_skills[0].ID);
			}

			renderStatus();
			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onSkillSelected(int riftSkillId)
		{
			_selected = Table_RiftSkill.Get(riftSkillId);
			if (_selected == null)
			{
				return;
			}

			Table_Skill.Row skill = Table_Skill.Get(_selected.SkillID);
			string skillName = (skill != null) ? skill.Name : string.Empty;
			string icon = (skill != null) ? skill.Icon : string.Empty;

			view.SetSelectedSkill(riftSkillId, skillName, icon);
			refreshButtons();
		}

		private void onSkillInfoClicked()
		{
			if (_selected == null)
			{
				return;
			}

			Table_Skill.Row skill = Table_Skill.Get(_selected.SkillID);
			string skillName = (skill != null) ? skill.Name : string.Empty;
			string desc = (skill != null) ? skill.Desc : string.Empty;

			view.ShowSkillInfo(skillName, _selected.ReqGauge, _selected.MaxGauge, desc);
		}

		// 체크포인트에서 시작한다. 입장 횟수는 여기서 소모한다(골드던전과 같은 규칙).
		private void onEnterClicked()
		{
			if (_selected == null)
			{
				return;
			}

			if (DungeonProgress.CanEnter(DUNGEON_TYPE) == false)
			{
				Debug.Log("[RiftDungeonPopup] 남은 입장 횟수가 없습니다.");
				return;
			}

			enterAsync(DungeonProgress.GetRiftCheckpoint(), _selected.ID).Forget();
		}

		// 입장 횟수 차감과 런 발급은 서버가 한다 — 응답을 받고 들어간다.
		private async UniTaskVoid enterAsync(int startWave, int riftSkillId)
		{
			DungeonEnterResult result = await DungeonEntry.RequestAsync(DUNGEON_TYPE, startWave, view.GetDestroyToken());
			if (result.ok == false)
			{
				return;
			}

			view.Close();
			MapNavigator.StartRiftDungeon(startWave, riftSkillId, result.run);
		}

		// 소탕 — 입장 1회를 쓰고 입장보상만 받는다. 1웨이브라도 넘긴 기록이 있어야 한다.
		// 로그인 중이면 서버(DungeonSweep)가 차감·지급하고, 미로그인이면 로컬로 지급한다.
		private void onSweepClicked()
		{
			if (canSweep() == false || _sweeping == true)
			{
				return;
			}

			if (NetworkManager.Instance.IsLoggedIn == true)
			{
				DungeonSweepRequest request = new DungeonSweepRequest();
				request.dungeonType = (int)DUNGEON_TYPE;
				_sweeping = true;
				NetworkManager.Instance.RequestDungeonSweep(request, onSweepResponse);
				return;
			}

			if (DungeonProgress.TryConsumeEnter(DUNGEON_TYPE) == false)
			{
				return;
			}

			EDT.Currency currency = DungeonProgress.GetRiftRewardCurrency();
			int reward = DungeonProgress.GetRiftEntryReward();
			Debug.Log($"[RiftDungeonPopup] 소탕 — {currency} {reward}");

			_granted.Clear();
			if (currency != EDT.Currency.None && reward > 0)
			{
				GrantedReward granted = default(GrantedReward);
				granted.type = RewardType.Currency;
				granted.currency = currency;
				granted.count = reward;
				_granted.Add(granted);
			}

			showSweepResult();
		}

		// 서버 소탕 결과 — 계정 반영은 창이 닫혔어도 한다(서버에는 이미 저장됐다).
		private void onSweepResponse(bool success, DungeonSweepResponse data, string error)
		{
			_sweeping = false;
			if (success == false || data == null)
			{
				Debug.LogWarning($"[RiftDungeonPopup] 소탕 실패: {error}");
				return;
			}

			DungeonProgress.ApplyEntry(data.entry);

			_granted.Clear();
			RewardGranter.FromServer(data.rewards, null, _granted);
			showSweepResult();
		}

		// 공용 지급 경로를 탄다 — 획득 로그(RewardAcquiredEvent)가 여기서 찍힌다.
		private void showSweepResult()
		{
			RewardGranter.ApplyAll(_granted);

			if (_isDisposed == true)
			{
				return;
			}

			renderStatus();

			if (_granted.Count > 0)
			{
				UIManager.Instance.ShowRewardPopupAsync(_granted, view.GetDestroyToken()).Forget();
			}
		}

		// 1초마다. 남은 초가 그대로면 문자열을 새로 만들지 않는다.
		private void onSecondTick()
		{
			int remaining = (int)DailyReset.GetRemaining().TotalSeconds;
			if (remaining < 0)
			{
				remaining = 0;
			}

			if (remaining == _lastRefreshSecond)
			{
				return;
			}

			_lastRefreshSecond = remaining;
			view.SetRefreshTime(DailyReset.FormatDuration(remaining));
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		private void renderInfo()
		{
			Table_Dungeon.Row row = Table_Dungeon.Get(DUNGEON_TYPE);
			if (row == null)
			{
				Debug.LogError($"[RiftDungeonPopup] Table_Dungeon.Get({DUNGEON_TYPE}) == null");
				return;
			}

			view.RenderInfo(row.Name, row.Thumbnail);
		}

		// 기록·체크포인트·횟수·보상. 입장이나 소탕으로 바뀌면 다시 부른다.
		private void renderStatus()
		{
			int best = DungeonProgress.GetHighestStage(DUNGEON_TYPE);
			int checkpoint = DungeonProgress.GetRiftCheckpoint();

			Table_Currency.Row currency = Table_Currency.Get(DungeonProgress.GetRiftRewardCurrency());

			RiftDungeonStatusData data;
			data.maxWave = (best > 0) ? best.ToString() : "없음";
			data.checkPoint = checkpoint.ToString();
			data.enterCount = DungeonProgress.GetRemainingCount(DUNGEON_TYPE) + "/" + DungeonProgress.GetMaxCount(DUNGEON_TYPE);
			data.enterReward = DungeonProgress.GetRiftEntryReward();
			data.waveReward = DungeonProgress.GetRiftWaveReward(checkpoint);
			data.rewardIconAddress = (currency != null) ? currency.Icon : string.Empty;

			view.RenderStatus(data);
			refreshButtons();
		}

		private void refreshButtons()
		{
			bool canEnter = DungeonProgress.CanEnter(DUNGEON_TYPE);
			view.SetEnterInteractable(canEnter == true && _selected != null);
			view.SetSweepInteractable(canSweep());
		}

		private static bool canSweep()
		{
			return DungeonProgress.GetHighestStage(DUNGEON_TYPE) >= 1 && DungeonProgress.CanEnter(DUNGEON_TYPE) == true;
		}

		// 균열 스킬을 ID 순으로 담는다.
		private void buildSkills()
		{
			_skills.Clear();

			Dictionary<int, Table_RiftSkill.Row>.Enumerator e = Table_RiftSkill.All().GetEnumerator();
			while (e.MoveNext() == true)
			{
				_skills.Add(e.Current.Value);
			}

			_skills.Sort(compareId);

			_slotData.Clear();
			for (int i = 0; i < _skills.Count; i++)
			{
				Table_Skill.Row skill = Table_Skill.Get(_skills[i].SkillID);

				RiftSkillSlotData data;
				data.riftSkillId = _skills[i].ID;
				data.iconAddress = (skill != null) ? skill.Icon : string.Empty;
				_slotData.Add(data);
			}
		}

		private int compareId(Table_RiftSkill.Row a, Table_RiftSkill.Row b)
		{
			return a.ID.CompareTo(b.ID);
		}
	}
}
