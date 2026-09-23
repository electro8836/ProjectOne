using System.Collections.Generic;
using EDT;
using ProjectOne.Shared;
using ProjectOne.Utils;

namespace ProjectOne.DailyBonuses
{
	// 출석 진행도 — 종류(주간/월간)마다 "다음에 받을 일차" 를 하나씩 들고 있다.
	//
	// 하루 경계는 DailyReset.GetResetDay() 하나로 두 종류가 똑같이 넘어간다.
	// 그래서 수령일(lastClaimedDay)은 종류별로 따로 두되 실제로는 같이 움직이고,
	// 일차만 주기 길이가 달라 따로 순환한다(주간 7, 월간 30).
	//
	// TODO(서버): 지금은 "팝업에서 칸을 누른 날" 에만 일차가 오른다. 접속만 하고 안 누르면 오르지 않는다.
	// 인메모리 1회성이라 지금은 드러나지 않지만, 서버 권위로 넘어가면
	// 출석일 누적과 보상 수령을 갈라 "접속만 해도 일차는 오르고, 밀린 보상은 나중에 받는" 형태가 되어야 한다.
	public sealed class DailyBonusBook
	{
		// 종류별 진행. dayCount 는 다음에 받을 일차(1부터), lastClaimedDay 는 마지막 수령일.
		private sealed class Entry
		{
			public int dayCount = 1;
			public int lastClaimedDay;
		}

		private readonly Dictionary<DailyBonusType, Entry> _entries = new Dictionary<DailyBonusType, Entry>();

		public DailyBonusBook(DailyBonusDto dto)
		{
			LoadFrom(dto);
		}

		// 다음에 받을 일차 (1부터).
		public int GetDayCount(DailyBonusType type)
		{
			return getOrCreate(type).dayCount;
		}

		// 오늘 아직 안 받았는가.
		public bool CanClaim(DailyBonusType type)
		{
			return getOrCreate(type).lastClaimedDay != DailyReset.GetResetDay();
		}

		// 오늘 몫을 받은 것으로 표시한다. 일차는 주기 끝에서 1로 돌아온다.
		public void MarkClaimed(DailyBonusType type, int cycleLength)
		{
			Entry entry = getOrCreate(type);
			entry.lastClaimedDay = DailyReset.GetResetDay();

			if (cycleLength <= 0)
			{
				return;
			}

			entry.dayCount = (entry.dayCount % cycleLength) + 1;
		}

		// ── 직렬화 ────────────────────────────────────────────────────────

		public DailyBonusDto ToDto()
		{
			DailyBonusDto dto = new DailyBonusDto();

			Dictionary<DailyBonusType, Entry>.Enumerator e = _entries.GetEnumerator();
			while (e.MoveNext() == true)
			{
				DailyBonusProgressDto progress = new DailyBonusProgressDto();
				progress.typeId = (int)e.Current.Key;
				progress.dayCount = e.Current.Value.dayCount;
				progress.lastClaimedDay = e.Current.Value.lastClaimedDay;
				dto.progress.Add(progress);
			}

			return dto;
		}

		public void LoadFrom(DailyBonusDto dto)
		{
			_entries.Clear();

			if (dto == null || dto.progress == null)
			{
				return;
			}

			for (int i = 0; i < dto.progress.Count; i++)
			{
				DailyBonusProgressDto progress = dto.progress[i];

				Entry entry = getOrCreate((DailyBonusType)progress.typeId);
				entry.dayCount = (progress.dayCount > 0) ? progress.dayCount : 1;
				entry.lastClaimedDay = progress.lastClaimedDay;
			}
		}

		// ── 내부 ──────────────────────────────────────────────────────────

		private Entry getOrCreate(DailyBonusType type)
		{
			Entry entry;
			if (_entries.TryGetValue(type, out entry) == false)
			{
				entry = new Entry();
				_entries.Add(type, entry);
			}

			return entry;
		}
	}
}
