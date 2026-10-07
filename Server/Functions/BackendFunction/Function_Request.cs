using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 요청 중복 방지 — 클라가 통신 실패로 같은 요청을 다시 보내도 한 번만 반영되게 한다.
	//
	// 클라는 유저가 직접 누른 요청마다 번호(rid)를 붙이고, 재전송 때는 같은 번호에 retry 표시를 더한다.
	// 진입점(BFunc.Function)이 성공 응답을 번호와 함께 USER_REQUEST 에 남겨 두었다가, 같은 번호의 재전송이 오면
	// 핸들러를 다시 돌리지 않고 남겨 둔 응답을 그대로 돌려준다.
	//
	// 유저당 마지막 1건만 둔다 — 대상 요청은 딤으로 입력을 막아 하나씩만 나간다.
	// 핸들러가 저장을 끝낸 직후 이 기록을 쓰기 전에 펑션이 죽으면 재전송이 한 번 더 반영된다(알고 둔 한계).
	public static class RequestOps
	{
		private const string Table = "USER_REQUEST";

		public const string RidKey = "rid";
		public const string RetryKey = "retry";

		// 중복 방지 대상 — 상태를 바꾸는 요청만. 조회는 다시 돌려도 안전해 뺀다.
		// 새 action 을 만들 때 유저가 직접 누르는 상태 변경 요청이면 여기에 더한다.
		public static bool IsGuarded(string action)
		{
			switch (action)
			{
				case FunctionName.ShopBuy:
				case FunctionName.DungeonEnter:
				case FunctionName.DungeonSweep:
				case FunctionName.DungeonRevive:
				case FunctionName.EquipmentPromote:
				case FunctionName.EquipmentTransfer:
				case FunctionName.EquipmentDecompose:
				case FunctionName.EquipmentDecomposeAll:
				case FunctionName.PetPromote:
				case FunctionName.DailyBonusClaim:
				case FunctionName.HeroPassClaim:
				case FunctionName.MailReceive:
				case FunctionName.ChangeNickname:
					return true;
			}

			return false;
		}

		// 같은 번호로 이미 처리한 요청이면 그때의 응답을 돌려준다. 없으면 null.
		public static string FindResult(string rid)
		{
			var getResult = Backend.GameData.GetMyData(Table, new Where());
			if (!getResult.IsSuccess())
			{
				return null;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				return null;
			}

			RequestRecordDto record = JsonConvert.DeserializeObject<RequestRecordDto>(rows[0]["Data"].ToString());
			if (record == null || record.rid != rid || string.IsNullOrEmpty(record.result) == true)
			{
				return null;
			}

			return record.result;
		}

		// 실패해도 요청 자체는 이미 성공했다 — 응답을 막지 않는다(중복 방지만 빠진다).
		public static void Save(string rid, string action, string result)
		{
			RequestRecordDto record = new RequestRecordDto();
			record.rid = rid;
			record.action = action;
			record.result = result;

			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(record));

			// 행이 있으면 갱신, 없으면(첫 요청) 만든다.
			var getResult = Backend.GameData.GetMyData(Table, new Where());
			if (!getResult.IsSuccess())
			{
				return;
			}

			if (getResult.FlattenRows().Count == 0)
			{
				Backend.GameData.Insert(Table, param);
				return;
			}

			Backend.GameData.Update(Table, new Where(), param);
		}

		// 핸들러가 돌려준 스트림의 본문을 읽는다.
		public static string ReadAll(Stream stream)
		{
			if (stream.CanSeek == true)
			{
				stream.Position = 0;
			}

			StreamReader reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}

		// 핸들러가 만든 성공 응답인가 — 거절은 서버 상태가 바뀌지 않아 남길 필요가 없다.
		public static bool IsSuccess(string result)
		{
			return string.IsNullOrEmpty(result) == false && result.Contains("\"success\":true") == true;
		}
	}
}
