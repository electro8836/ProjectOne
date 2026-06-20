using System;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Dungeon
	{
		// 맵 보상 차트(콘솔 > 게임 정보 관리 > 차트)의 차트 이름 — 컬럼: mapId, rewardExp.
		// 차트 파일 ID 는 재업로드 시마다 바뀌므로, 고정인 "이름"으로 조회한다(콘솔 차트 이름과 일치시킬 것).
		private const string MapRewardChartName = "MapReward";

		// 이름으로 해석한 차트 파일 ID 캐시 — 워밍 컨테이너 재사용(콜드스타트 시에만 재조회).
		private static string _cachedMapChartFileId;

		// DungeonClear — 클라가 보낸 mapId 로 보상 차트를 조회해 rewardExp 를 가산 저장 후 반환(서버 권위).
		// MainRouter.ProjectOneFunction 이 Initialize 후 호출하는 내부 핸들러(진입점 아님).
		public Stream DungeonClear()
		{
			// [테스트] 유저 데이터가 저장된 뒤끝 콘솔의 테이블 이름
			string tableName = "USER_INFO";

			try
			{
				// 2. 클라가 보낸 요청(req)에서 mapId 를 파싱한다(BackndFunctionCaller 가 body 의 "req" 에 담아 보냄).
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				DungeonClearRequest req = JsonConvert.DeserializeObject<DungeonClearRequest>(reqJson);
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 3. 맵 보상 차트를 조회해 해당 mapId 의 rewardExp 를 결정한다(서버 권위 — 클라 값 불신).
				//    차트 파일 ID 는 이름으로 해석한다(하드코딩 ID 가 재업로드로 깨지는 것 방지).
				if (resolveMapChartFileId(out string mapChartFileId, out string chartErr) == false)
				{
					return FuncResult.Error(chartErr);
				}

				var chartResult = Backend.Chart.GetChartContents(mapChartFileId);
				if (!chartResult.IsSuccess())
				{
					return FuncResult.Error("Failed to get map chart: " + chartResult.GetErrorCode());
				}

				JsonData chartRows = chartResult.FlattenRows();
				int rewardExp = -1;
				for (int i = 0; i < chartRows.Count; i++)
				{
					if (int.Parse(chartRows[i]["MapID"].ToString()) == req.mapId)
					{
						rewardExp = int.Parse(chartRows[i]["RewardExp"].ToString());
						break;
					}
				}

				if (rewardExp < 0)
				{
					return FuncResult.Error("invalid mapId: " + req.mapId);
				}

				// 4. 현재 유저의 게임 데이터(경험치 등)를 조회한다(행 1개 가정).
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return FuncResult.Error("Failed to get user data: " + getResult.GetErrorCode());
				}

				// 조회한 데이터에서 현재 경험치(Exp)를 읽는다.
				JsonData rows = getResult.FlattenRows();
				if (rows.Count == 0)
				{
					return FuncResult.Error("User data row not found. Please create row first.");
				}

				int currentExp = int.Parse(rows[0]["Exp"].ToString());

				// 5. 경험치에 보상치를 가산한다.
				int updatedExp = currentExp + rewardExp;

				// 6. 업데이트할 Param 구성.
				Param updateParam = new Param();
				updateParam.Add("Exp", updatedExp);

				// 7. 서버 권위로 내 데이터(현재 유저 단일 행)를 수정한다. GetMyData 와 동일한 new Where() 방식 —
				//    inDate/owner(Backend.UserInDate) 인자가 함수 컨텍스트에서 비어 UndefinedParameterException 나는 문제를 우회.
				var updateResult = Backend.GameData.Update(tableName, new Where(), updateParam);

				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("Failed to update Exp: " + updateResult.GetErrorCode());
				}

				// 8. 갱신된 결과를 공유 DTO 로 반환(클라 JsonUtility 와 키 일치).
				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = updatedExp;

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 차트 이름(MapRewardChartName)으로 차트 파일 ID 를 해석한다. 성공 시 캐시에 저장.
		private static bool resolveMapChartFileId(out string chartFileId, out string err)
		{
			if (string.IsNullOrEmpty(_cachedMapChartFileId) == false)
			{
				chartFileId = _cachedMapChartFileId;
				err = null;
				return true;
			}

			var listBro = Backend.Chart.GetChartListV2();
			if (!listBro.IsSuccess())
			{
				chartFileId = null;
				err = "Failed to get chart list: " + listBro.GetErrorCode();
				return false;
			}

			JsonData rows = listBro.FlattenRows();
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i]["chartName"].ToString() == MapRewardChartName)
				{
					_cachedMapChartFileId = rows[i]["selectedChartFileId"].ToString();
					chartFileId = _cachedMapChartFileId;
					err = null;
					return true;
				}
			}

			chartFileId = null;
			err = "chart not found: " + MapRewardChartName;
			return false;
		}
	}
}
