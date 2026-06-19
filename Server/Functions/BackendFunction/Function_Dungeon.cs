using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using BackEnd;
using LitJson;
using Newtonsoft.Json.Linq;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BackendFunction
{
	public class Dungeon
	{
		public Stream DungeonClear(Stream stream, ILambdaContext context)
		{
			try
			{
				// 1. 뒤끝 펑션 초기화
				Backend.Initialize(ref stream);
			}
			catch (Exception e)
			{
				return Common.ReturnErrorObject("Initialize Failed: " + e.ToString());
			}

			// [설정] 유저 정보가 저장된 뒤끝 콘솔의 테이블 이름
			string tableName = "USER_INFO";
			int rewardExp = 1000;

			try
			{
				// 2. 현재 유저의 기존 데이터(경험치 등)를 먼저 조회합니다.
				// 유저당 데이터가 1개만 있다고 가정하고 GetMyData를 사용합니다.
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return Common.ReturnErrorObject("Failed to get user data: " + getResult.GetErrorCode());
				}

				// 기존 유저 데이터에서 indate(데이터 고유 키)와 현재 경험치(Exp)를 추출합니다.
				JsonData rows = getResult.FlattenRows();
				if (rows.Count == 0)
				{
					return Common.ReturnErrorObject("User data row not found. Please create row first.");
				}

				string inDate = rows[0]["inDate"].ToString();
				int currentExp = int.Parse(rows[0]["Exp"].ToString());

				// 3. 경험치를 1000 더합니다.
				int headerExp = currentExp + rewardExp;

				// 4. DB에 업데이트할 Param 객체를 생성합니다.
				Param updateParam = new Param();
				updateParam.Add("Exp", headerExp);

				// 5. 서버에서 뒤끝 DB를 직접 업데이트합니다.
				var updateResult = Backend.GameData.UpdateV2(tableName, inDate, Backend.UserInDate, updateParam);

				if (!updateResult.IsSuccess())
				{
					return Common.ReturnErrorObject("Failed to update Exp: " + updateResult.GetErrorCode());
				}

				// 6. 업데이트 성공 시 클라이언트에 돌려줄 응답값(JSON)을 만듭니다.
				JObject responseJson = new JObject();
				responseJson.Add("status", "success");
				responseJson.Add("message", $"던전 클리어! 경험치 {rewardExp}을 획득했습니다.");
				responseJson.Add("currentExp", headerExp); // 현재 총 경험치도 함께 리턴

				return Backend.JsonToStream(responseJson.ToString());
			}
			catch (Exception ex)
			{
				return Common.ReturnErrorObject("Server Error: " + ex.ToString());
			}
		}
    }
}
