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

		// DungeonClear — 클라가 보낸 mapId 로 보상 차트를 조회해 rewardExp 를 전투한 캐릭터의 exp 에 가산 저장 후 반환(서버 권위).
		// MainRouter.ProjectOneFunction 이 Initialize 후 호출하는 내부 핸들러(진입점 아님).
		public Stream DungeonClear()
		{
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

				// 4. 전투한 캐릭터의 USER_CHARACTER 를 로드한다(행 1개 가정).
				var getResult = Backend.GameData.GetMyData("USER_CHARACTER", new Where());

				if (!getResult.IsSuccess())
				{
					return FuncResult.Error("USER_CHARACTER Get Failed: " + getResult.GetErrorCode());
				}

				JsonData rows = getResult.FlattenRows();
				if (rows.Count == 0)
				{
					return FuncResult.Error("USER_CHARACTER row not found");
				}

				CharacterDto character = JsonConvert.DeserializeObject<CharacterDto>(rows[0]["Data"].ToString());
				if (character == null)
				{
					return FuncResult.Error("USER_CHARACTER parse failed");
				}

				// 5. 보유 검증 후 해당 캐릭터의 누적 exp 에 보상을 가산한다(서버 권위 — 미보유 캐릭터 거부).
				OwnedCharacterDto target = findCharacter(character, req.characterId);
				if (target == null)
				{
					return FuncResult.Error("character not owned: " + req.characterId);
				}

				target.exp += rewardExp;

				// 6. USER_CHARACTER Data 덮어쓰기. 함수 컨텍스트는 owner 인자가 비어 UndefinedParameterException 나므로 new Where() 사용.
				Param updateParam = new Param();
				updateParam.Add("Data", JsonConvert.SerializeObject(character));
				var updateResult = Backend.GameData.Update("USER_CHARACTER", new Where(), updateParam);

				if (!updateResult.IsSuccess())
				{
					return FuncResult.Error("Failed to update USER_CHARACTER: " + updateResult.GetErrorCode());
				}

				// 7. 갱신된 캐릭터 누적 exp 를 반환(클라가 로컬에 권위 반영).
				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = target.exp;

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// CharacterDto 에서 characterId 로 보유 캐릭터를 찾는다. 미보유면 null.
		private static OwnedCharacterDto findCharacter(CharacterDto character, int characterId)
		{
			for (int i = 0; i < character.characters.Count; i++)
			{
				OwnedCharacterDto oc = character.characters[i];
				if (oc != null && oc.characterId == characterId)
				{
					return oc;
				}
			}

			return null;
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
