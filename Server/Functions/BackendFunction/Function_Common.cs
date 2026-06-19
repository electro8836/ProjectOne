using System.IO;
using BackEnd;
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
}
