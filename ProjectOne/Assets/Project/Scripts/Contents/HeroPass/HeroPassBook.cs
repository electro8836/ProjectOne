using System;
using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Network;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.HeroPasses
{
	// 히어로패스 진행도 — 경험치, 유형별 활동 카운터, 레벨별 수령 기록, 구매 여부.
	//
	// 시즌은 계정 생성일부터 35일 주기다. 경계는 DailyReset 의 하루 경계(KST 06시)를 그대로 쓴다.
	// 시즌이 넘어가면 경험치·카운터·수령 기록·구매 여부가 전부 초기화된다.
	// 공개 메서드는 모두 ensureSeason() 을 먼저 불러 경계를 넘긴 상태를 읽지 않게 한다.
	//
	// 서버 권위다 — 경험치는 서버가 처치·던전 클리어 정산 때 같은 규칙(HeroPassRules)으로 센다.
	// 여기서 세는 것은 표시용이고, 수령·구매는 서버 응답 후에만 반영한다. 로그인 때 서버 값으로 다시 맞춘다.
	public sealed class HeroPassBook
	{
		private readonly HeroPassDto _dto;

		public HeroPassBook(HeroPassDto dto)
		{
			_dto = (dto != null) ? dto : createLocal();
			if (_dto.counters == null)
			{
				_dto.counters = new List<HeroPassCounterDto>();
			}

			if (_dto.claimedNormal == null)
			{
				_dto.claimedNormal = new List<int>();
			}

			if (_dto.claimedAdvanced == null)
			{
				_dto.claimedAdvanced = new List<int>();
			}
		}

		public int Exp
		{
			get
			{
				ensureSeason();
				return _dto.exp;
			}
		}

		public int Level
		{
			get
			{
				ensureSeason();
				return HeroPassRules.GetLevelByExp(_dto.exp);
			}
		}

		public bool IsPurchased
		{
			get
			{
				ensureSeason();
				return _dto.purchased;
			}
		}

		// 활동 1회를 센다(표시용 — 서버도 같은 처치를 정산 때 센다). 이번에 늘어난 경험치를 돌려준다.
		// 레벨이 오르면 밀린 처치 배치를 바로 보내 서버 레벨이 뒤처지지 않게 한다(곧 수령할 수 있어야 한다).
		public int AddCount(HeroPassExpType type)
		{
			ensureSeason();

			int levelBefore = HeroPassRules.GetLevelByExp(_dto.exp);
			int gained = HeroPassRules.AddCount(_dto, type);
			if (gained <= 0)
			{
				return 0;
			}

			int levelAfter = HeroPassRules.GetLevelByExp(_dto.exp);
			if (levelAfter > levelBefore)
			{
				NetworkManager.Instance.FlushFieldBatch();
			}

			EventManager.Instance.Publish(new HeroPassExpChangedEvent(levelAfter, levelBefore));
			return gained;
		}

		public bool IsClaimed(int level, bool advanced)
		{
			ensureSeason();
			return HeroPassRules.IsClaimed(_dto, level, advanced);
		}

		// 도달했고, 아직 안 받았고, 추가 보상이면 구매까지 했는가.
		public bool CanClaim(int level, bool advanced)
		{
			ensureSeason();
			return HeroPassRules.CanClaim(_dto, level, advanced);
		}

		// 서버 수령 성공 후에만 부른다.
		public void MarkClaimed(int level, bool advanced)
		{
			ensureSeason();
			HeroPassRules.MarkClaimed(_dto, level, advanced);
		}

		// 서버 구매 성공 후에만 부른다.
		public void SetPurchased()
		{
			ensureSeason();
			_dto.purchased = true;
		}

		// 이번 시즌이 끝나기까지 남은 시간.
		public TimeSpan GetSeasonRemaining()
		{
			ensureSeason();

			int daysLeft = HeroPassRules.GetSeasonDaysLeft(_dto, DailyReset.GetResetDay());

			// GetRemaining 은 오늘 경계까지다 — 남은 온전한 날수를 더한다.
			return DailyReset.GetRemaining() + TimeSpan.FromDays(daysLeft - 1);
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		// 서버 데이터가 없으면(미로그인) 오늘을 시즌 시작일로 둔다.
		private static HeroPassDto createLocal()
		{
			HeroPassDto dto = new HeroPassDto();
			dto.startResetDay = DailyReset.GetResetDay();
			return dto;
		}

		private void ensureSeason()
		{
			HeroPassRules.EnsureSeason(_dto, DailyReset.GetResetDay());
		}
	}
}
