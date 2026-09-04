using EDT;
using ProjectOne.Items;

namespace ProjectOne.UI
{
	// 스탯 옵션(Option) 을 화면 문구로 바꾸는 공용 규칙.
	//
	// 장비 정보 팝업과 마스터리 화면이 같은 규칙을 쓴다 — 한쪽만 고치면 같은 옵션이 화면마다
	// 다르게 보이므로 포맷을 여기 한 곳에 둔다. 값이 어디서 왔는지(장비 강화·순도, 마스터리 레벨)는
	// 호출자가 계산해 넘기고, 여기서는 "어떻게 보이는가"만 결정한다.
	public static class StatOptionText
	{
		// 수치 증감 색. 미해금 줄은 색 태그를 아예 넣지 않고 View 가 줄 전체를 회색으로 칠한다.
		private const string COLOR_INCREASE = "#5CFF5C";
		private const string COLOR_DECREASE = "#FF5C5C";

		// DisplayFormat 의 {0} 자리에 "부호+수치" 를 끼운다.
		// DisplayFormat 은 부호를 갖지 않는 것을 전제로 한다 (예: "공격력 {0}").
		//
		// 미해금(locked)이면 색 태그를 넣지 않는다 — 줄 전체를 회색으로 만드는 일은 View 가
		// TMP_Text.color 로 처리한다. 문자열에 바깥 태그를 한 겹 더 씌우는 것보다 단순하다.
		public static string FormatStat(StatDetail detail, float value, bool locked)
		{
			// 단위(%)를 먼저 수치에 붙인 뒤 색을 씌운다 — 그래야 "+6.6%" 가 통째로 색 안에 들어간다.
			bool absorbUnit = hasPercentSuffix(detail);
			string body = signed(detail, value) + (absorbUnit ? "%" : string.Empty);

			if (locked == false)
			{
				string color = (value < 0f) ? COLOR_DECREASE : COLOR_INCREASE;
				body = "<color=" + color + ">" + body + "</color>";
			}

			return applyFormat(detail, body, absorbUnit);
		}

		// 범위는 수치만 적는다 — 스탯 이름은 같은 줄의 OptionText 가 이미 말하고 있다.
		// 예) (2% - 20%) / (250 - 500)
		public static string FormatRange(StatDetail detail, float min, float max)
		{
			return "(" + rangeValue(detail, min) + " - " + rangeValue(detail, max) + ")";
		}

		private static string rangeValue(StatDetail detail, float value)
		{
			return shown(detail, value) + (isPercent(detail) ? "%" : string.Empty);
		}

		// unitAbsorbed 면 "%" 가 이미 body 에 붙어 있으므로 포맷에서 그 한 글자를 지운다(중복 방지).
		private static string applyFormat(StatDetail detail, string body, bool unitAbsorbed)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			if (row == null || string.IsNullOrEmpty(row.DisplayFormat) == true)
			{
				return detail + " " + body;
			}

			string format = row.DisplayFormat;
			if (unitAbsorbed == true)
			{
				format = format.Remove(format.IndexOf("{0}") + 3, 1);
			}

			return string.Format(format, body);
		}

		// DisplayFormat 이 "{0}%" 처럼 자리표시자 바로 뒤에 단위를 붙여 두었는가.
		// 접미사가 "" / "%" 두 가지뿐이라 한 글자만 보면 충분하다.
		private static bool hasPercentSuffix(StatDetail detail)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			if (row == null || string.IsNullOrEmpty(row.DisplayFormat) == true)
			{
				return false;
			}

			int index = row.DisplayFormat.IndexOf("{0}");
			return index >= 0 && index + 3 < row.DisplayFormat.Length && row.DisplayFormat[index + 3] == '%';
		}

		private static bool isPercent(StatDetail detail)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			return row != null && row.StatValueType == StatValueTypes.Percent;
		}

		private static string signed(StatDetail detail, float value)
		{
			return ((value < 0f) ? "-" : "+") + shown(detail, System.Math.Abs(value));
		}

		// Percent 타입은 0.05 처럼 비율로 저장되어 있다 — 표시할 때만 100 을 곱한다("%" 는 포맷이 갖는다).
		private static string shown(StatDetail detail, float value)
		{
			Table_StatDetail.Row row = Table_StatDetail.Get(detail);
			float v = (row != null && row.StatValueType == StatValueTypes.Percent) ? value * 100f : value;
			return v.ToString("0.##");
		}

		// ── 조회 ──────────────────────────────────────────────────────────

		// 스탯 옵션만 문구로 만들 수 있다. 스킬/모디파이어 옵션은 문구 규칙이 아직 없다.
		public static bool TryGetStatDetail(Option option, out StatDetail detail)
		{
			detail = StatDetail.None;

			OptionCatalog.Entry entry;
			if (OptionCatalog.TryGet(option, out entry) == false || entry.type != OptionTypes.Stat)
			{
				return false;
			}

			detail = entry.statDetail;
			return true;
		}

		// 아이콘은 세부 스탯이 아니라 부모 스탯이 소유한다 (공격력 Add/Ratio/Amp 가 같은 아이콘을 쓴다).
		public static string GetStatIcon(StatDetail detail)
		{
			Table_StatDetail.Row detailRow = Table_StatDetail.Get(detail);
			if (detailRow == null)
			{
				return string.Empty;
			}

			Table_Stat.Row statRow = Table_Stat.Get(detailRow.StatID);
			return (statRow != null) ? statRow.Icon : string.Empty;
		}
	}
}
