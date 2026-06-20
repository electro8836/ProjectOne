using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BackendFunction
{
	// 펑션 응답 직렬화 공용 헬퍼 — 모든 핸들러가 공유.
	public static class FuncResult
	{
		// 에러 응답. StringToStream 으로 원문 그대로 반환(JsonToStream 은 문자열을 한 번 더 직렬화해 이중 인코딩됨).
		public static Stream Error(string err)
		{
			JObject error = new JObject();
			error.Add("status", "fail");
			error.Add("error", err);
			return Backend.StringToStream(error.ToString());
		}

		// 성공/일반 객체 응답. 클라는 GetReturnValueByUnmarshall() 로 이 JSON 문자열을 그대로 받는다.
		public static Stream Json(object payload)
		{
			return Backend.StringToStream(JsonConvert.SerializeObject(payload));
		}
	}

	// 차트 조회 공용 헬퍼 — 차트 "이름"으로 파일 ID 를 해석한다.
	// 차트 파일 ID 는 재업로드 시마다 바뀌므로 고정인 이름으로 조회하고, 해석 결과를 캐시한다(워밍 컨테이너 재사용, 콜드스타트 시에만 재조회).
	public static class ChartUtil
	{
		private static readonly Dictionary<string, string> _chartFileIdCache = new Dictionary<string, string>();

		// chartName 에 해당하는 차트 파일 ID 를 반환한다. 성공 시 캐시에 저장.
		public static bool ResolveChartFileId(string chartName, out string fileId, out string err)
		{
			if (_chartFileIdCache.TryGetValue(chartName, out fileId))
			{
				err = null;
				return true;
			}

			var listBro = Backend.Chart.GetChartListV2();
			if (!listBro.IsSuccess())
			{
				fileId = null;
				err = "Failed to get chart list: " + listBro.GetErrorCode();
				return false;
			}

			JsonData rows = listBro.FlattenRows();
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i]["chartName"].ToString() == chartName)
				{
					fileId = rows[i]["selectedChartFileId"].ToString();
					_chartFileIdCache[chartName] = fileId;
					err = null;
					return true;
				}
			}

			fileId = null;
			err = "chart not found: " + chartName;
			return false;
		}
	}
}
