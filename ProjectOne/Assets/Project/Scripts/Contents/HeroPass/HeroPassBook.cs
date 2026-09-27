using System;
using System.Collections.Generic;
using EDT;
using ProjectOne.Utils;

namespace ProjectOne.HeroPasses
{
	// 히어로패스 진행도 — 경험치, 유형별 활동 카운터, 레벨별 수령 기록, 구매 여부.
	//
	// 시즌은 계정 첫 접속일부터 35일 주기다. 경계는 DailyReset 의 하루 경계(KST 06시)를 그대로 쓴다.
	// 시즌이 넘어가면 경험치·카운터·수령 기록·구매 여부가 전부 초기화된다.
	// 공개 메서드는 모두 ensureSeason() 을 먼저 불러 경계를 넘긴 상태를 읽지 않게 한다.
	//
	// TODO(서버): 지금은 인메모리다. 시즌 기준일은 Account 생성 시점(= 앱 실행)이라 재실행하면 시즌이 새로 시작된다.
	// 서버 권위로 넘어가면 기준일은 계정 생성일로, 경험치·수령·구매는 서버가 소유해야 한다.
	public sealed class HeroPassBook
	{
		public const int SEASON_DAYS = 35;

		private readonly int _startResetDay;
		private int _seasonIndex;

		private int _exp;
		private bool _isPurchased;

		private readonly Dictionary<HeroPassExpType, int> _counters = new Dictionary<HeroPassExpType, int>();
		private readonly HashSet<int> _claimedNormal = new HashSet<int>();
		private readonly HashSet<int> _claimedAdvanced = new HashSet<int>();

		public HeroPassBook()
		{
			_startResetDay = DailyReset.GetResetDay();
			_seasonIndex = 0;
		}

		public int Exp
		{
			get
			{
				ensureSeason();
				return _exp;
			}
		}

		public int Level
		{
			get
			{
				ensureSeason();
				return HeroPassCatalog.GetLevelByExp(_exp);
			}
		}

		public bool IsPurchased
		{
			get
			{
				ensureSeason();
				return _isPurchased;
			}
		}

		// 활동 1회를 센다. ReqCount 배수에 닿은 규칙마다 ExpAmount 를 적립하고, 이번에 늘어난 경험치를 돌려준다.
		public int AddCount(HeroPassExpType type)
		{
			ensureSeason();

			int count;
			_counters.TryGetValue(type, out count);
			count++;
			_counters[type] = count;

			int gained = 0;
			IReadOnlyList<Table_HeroPassExpInfo.Row> infos = HeroPassCatalog.GetExpInfos(type);
			for (int i = 0; i < infos.Count; i++)
			{
				if (count % infos[i].ReqCount == 0)
				{
					gained += infos[i].ExpAmount;
				}
			}

			if (gained <= 0)
			{
				return 0;
			}

			// 최대 레벨을 넘는 몫은 버린다.
			int before = _exp;
			_exp = Math.Min(_exp + gained, HeroPassCatalog.GetMaxExp());
			return _exp - before;
		}

		public bool IsClaimed(int level, bool advanced)
		{
			ensureSeason();
			return getClaimed(advanced).Contains(level);
		}

		// 도달했고, 아직 안 받았고, 추가 보상이면 구매까지 했는가.
		public bool CanClaim(int level, bool advanced)
		{
			ensureSeason();

			if (level <= 0 || level > HeroPassCatalog.GetLevelByExp(_exp))
			{
				return false;
			}

			if (advanced == true && _isPurchased == false)
			{
				return false;
			}

			return getClaimed(advanced).Contains(level) == false;
		}

		public void MarkClaimed(int level, bool advanced)
		{
			ensureSeason();
			getClaimed(advanced).Add(level);
		}

		public void SetPurchased()
		{
			ensureSeason();
			_isPurchased = true;
		}

		// 이번 시즌이 끝나기까지 남은 시간.
		public TimeSpan GetSeasonRemaining()
		{
			ensureSeason();

			int elapsedDays = DailyReset.GetResetDay() - _startResetDay;
			int daysLeft = SEASON_DAYS - (elapsedDays % SEASON_DAYS);

			// GetRemaining 은 오늘 경계까지다 — 남은 온전한 날수를 더한다.
			return DailyReset.GetRemaining() + TimeSpan.FromDays(daysLeft - 1);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private HashSet<int> getClaimed(bool advanced)
		{
			return advanced ? _claimedAdvanced : _claimedNormal;
		}

		private void ensureSeason()
		{
			int season = (DailyReset.GetResetDay() - _startResetDay) / SEASON_DAYS;
			if (season == _seasonIndex)
			{
				return;
			}

			_seasonIndex = season;
			_exp = 0;
			_isPurchased = false;
			_counters.Clear();
			_claimedNormal.Clear();
			_claimedAdvanced.Clear();
		}
	}
}
