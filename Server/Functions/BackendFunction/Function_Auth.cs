using System;
using System.IO;
using Amazon.Lambda.Core;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Auth
	{
		// getUserData — 로그인 후 계정 데이터 로드. 신규 계정이면 기본 데이터를 생성해 반환한다.
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
				GetUserDataResponse response = new GetUserDataResponse();
				response.success = true;

				// 2. 행이 없으면 신규 유저 → 기본 데이터로 행 생성
				if (rows.Count == 0)
				{
					Param defaultParam = new Param();
					defaultParam.Add("Exp", 0);

					var insertResult = Backend.GameData.Insert(tableName, defaultParam);

					if (!insertResult.IsSuccess())
					{
						return Common.ReturnErrorObject("Data Insert Failed: " + insertResult.GetErrorCode());
					}

					response.exp = 0;
				}
				else
				{
					// 3. 기존 유저 → 저장된 데이터 로드
					response.exp = int.Parse(rows[0]["Exp"].ToString());
				}

				return Backend.JsonToStream(JsonConvert.SerializeObject(response));
			}
			catch (Exception ex)
			{
				return Common.ReturnErrorObject("Server Error: " + ex.ToString());
			}
		}
	}
}
