using System;
using System.IO;
using Amazon.Lambda.Core;
using BackEnd;
using LitJson;
using Newtonsoft.Json.Linq;

namespace BackendFunction
{
	public class Auth
	{
		public Stream InitUserAfterLogin(Stream stream, ILambdaContext context)
		{
			try
			{
				Backend.Initialize(ref stream);
			}
			catch (Exception e)
			{
				return Common.ReturnErrorObject("Initialize Failed: " + e.ToString());
			}

			string tableName = "USER_INFO";

			try
			{
				// 1. 현재 로그인한 유저의 행이 이미 존재하는지 조회
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return Common.ReturnErrorObject("Data Get Failed: " + getResult.GetErrorCode());
				}

				JsonData rows = getResult.FlattenRows();
				JObject responseJson = new JObject();

				// 2. 행이 없다 = 오늘 처음 가입해서 로그인한 신규 유저다!
				if (rows.Count == 0)
				{
					// 서버가 주도해서 초기 데이터를 설정 (해킹 불가능)
					Param defaultParam = new Param();
					defaultParam.Add("Exp", 0);

					// 서버가 직접 DB에 행 생성
					var insertResult = Backend.GameData.Insert(tableName, defaultParam);

					if (!insertResult.IsSuccess())
					{
						return Common.ReturnErrorObject("Data Insert Failed: " + insertResult.GetErrorCode());
					}

					responseJson.Add("status", "new_user_initialized");
					responseJson.Add("message", "신규 유저 초기화 완료");
					responseJson.Add("Exp", 0);
				}
				else
				{
					// 3. 행이 이미 있다 = 기존에 하던 유저다!
					responseJson.Add("status", "existing_user");
					responseJson.Add("message", "기존 유저 데이터 로드 완료");
					responseJson.Add("Exp", int.Parse(rows[0]["Exp"].ToString()));
				}

				return Backend.JsonToStream(responseJson.ToString());
			}
			catch (Exception ex)
			{
				return Common.ReturnErrorObject("Server Error: " + ex.ToString());
			}
		}
	}
}