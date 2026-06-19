using System;
using System.IO;
using Amazon.Lambda.Core;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BackendFunction
{
	public class Dungeon
	{
		// DungeonClear — (테스트) 서버가 exp+1000 가산 저장 후 반환(서버 권위).
		public Stream DungeonClear(Stream stream, ILambdaContext context)
		{
			try
			{
				// 1. 뒤끝 SDK 초기화
				Backend.Initialize(ref stream);
			}
			catch (Exception e)
			{
				return Common.ReturnErrorObject("Initialize Failed: " + e.ToString());
			}

			// [테스트] 유저 데이터가 저장된 뒤끝 콘솔의 테이블 이름
			string tableName = "USER_INFO";
			int rewardExp = 1000;

			try
			{
				// 2. 현재 유저의 게임 데이터(경험치 등)를 먼저 조회한다(행 1개 가정).
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return Common.ReturnErrorObject("Failed to get user data: " + getResult.GetErrorCode());
				}

				// 조회한 데이터에서 indate(행 식별 키)와 현재 경험치(Exp)를 읽는다.
				JsonData rows = getResult.FlattenRows();
				if (rows.Count == 0)
				{
					return Common.ReturnErrorObject("User data row not found. Please create row first.");
				}

				string inDate = rows[0]["inDate"].ToString();
				int currentExp = int.Parse(rows[0]["Exp"].ToString());

				// 3. 경험치에 보상치를 가산한다.
				int updatedExp = currentExp + rewardExp;

				// 4. 업데이트할 Param 구성.
				Param updateParam = new Param();
				updateParam.Add("Exp", updatedExp);

				// 5. 서버 권위로 뒤끝 DB에 저장.
				var updateResult = Backend.GameData.UpdateV2(tableName, inDate, Backend.UserInDate, updateParam);

				if (!updateResult.IsSuccess())
				{
					return Common.ReturnErrorObject("Failed to update Exp: " + updateResult.GetErrorCode());
				}

				// 6. 갱신된 결과를 공유 DTO 로 반환(클라 JsonUtility 와 키 일치).
				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = updatedExp;

				return Backend.JsonToStream(JsonConvert.SerializeObject(response));
			}
			catch (Exception ex)
			{
				return Common.ReturnErrorObject("Server Error: " + ex.ToString());
			}
		}
	}
}
