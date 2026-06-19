using System;
using System.IO;
using BackEnd;
using LitJson;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Auth
	{
		// GetUserData — 로그인 후 계정 데이터 로드. 신규 계정이면 기본 데이터를 생성해 반환한다.
		public Stream GetUserData()
		{
			string tableName = "USER_INFO";

			try
			{
				// 1. 현재 로그인한 유저의 행이 이미 존재하는지 조회
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return FuncResult.Error("Data Get Failed: " + getResult.GetErrorCode());
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
						return FuncResult.Error("Data Insert Failed: " + insertResult.GetErrorCode());
					}

					response.exp = 0;
				}
				else
				{
					// 3. 기존 유저 → 저장된 데이터 로드
					response.exp = int.Parse(rows[0]["Exp"].ToString());
				}

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}
	}
}
