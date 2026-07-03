using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	public class Auth
	{
		// GetUserData — 로그인 스냅샷 번들. 서버가 USER_INFO/CURRENCY/INVENTORY 를 읽어 한 응답으로 조립한다.
		// 신규 계정이면 USER_INFO 를 생성하고, 신규/기존 무관하게 도메인 행을 ensure(없으면 생성)해
		// 개발 중 추가된 테이블을 기존 유저에게 자동 마이그레이션한다.
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

				// 2. USER_INFO — 유저 존재 앵커. 신규면 생성(계정 exp 는 더 이상 사용하지 않으므로 응답에 싣지 않는다).
				if (rows.Count == 0)
				{
					Param defaultParam = new Param();
					defaultParam.Add("Exp", 0);

					var insertResult = Backend.GameData.Insert(tableName, defaultParam);
					if (!insertResult.IsSuccess())
					{
						return FuncResult.Error("Data Insert Failed: " + insertResult.GetErrorCode());
					}
				}

				// 3. 도메인 행 ensure(없으면 생성) + 그 데이터를 응답 번들에 실어 보낸다.
				//    새 도메인 테이블이 추가되면 여기 ensure + 응답 세팅 한 쌍만 더하면 된다.
				//    (콘솔에 실제 존재하는 테이블만 대상 — 없는 테이블은 GetMyData 가 실패한다.)
				if (ensureDomainRow("USER_CURRENCY", buildStarterCurrencyJson(), out string currencyJson, out string currencyErr) == false)
				{
					return FuncResult.Error(currencyErr);
				}

				response.currency = JsonConvert.DeserializeObject<CurrencyDto>(currencyJson);

				if (ensureDomainRow("USER_INVENTORY", buildEmptyInventoryJson(), out string inventoryJson, out string inventoryErr) == false)
				{
					return FuncResult.Error(inventoryErr);
				}

				response.inventory = JsonConvert.DeserializeObject<InventoryDto>(inventoryJson);

				if (ensureDomainRow("USER_CARDSKILL", buildEmptyCardSkillJson(), out string cardSkillJson, out string cardSkillErr) == false)
				{
					return FuncResult.Error(cardSkillErr);
				}

				response.cardSkill = JsonConvert.DeserializeObject<CardSkillBookDto>(cardSkillJson);

				if (ensureDomainRow("USER_DUNGEON", buildEmptyClearedDungeonsJson(), out string dungeonJson, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				response.clearedDungeons = JsonConvert.DeserializeObject<ClearedDungeonsDto>(dungeonJson);

				// 4. USER_CHARACTER — 없으면 무료 캐릭터(차트 UnlockState=Free)로 생성, 있으면 기존 보유 유지.
				if (ensureCharacterRow(out string characterJson, out string characterErr) == false)
				{
					return FuncResult.Error(characterErr);
				}

				response.character = JsonConvert.DeserializeObject<CharacterDto>(characterJson);

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 도메인 행이 없으면 기본 데이터로 생성(있으면 그대로 둔다). 최종 Data(JSON)를 dataJson 으로 돌려준다.
		private bool ensureDomainRow(string tableName, string defaultDataJson, out string dataJson, out string err)
		{
			var getResult = Backend.GameData.GetMyData(tableName, new Where());
			if (!getResult.IsSuccess())
			{
				dataJson = null;
				err = tableName + " Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count > 0)
			{
				// 이미 존재 — 기존 데이터 보존(덮지 않음)
				dataJson = rows[0]["Data"].ToString();
				err = null;
				return true;
			}

			Param param = new Param();
			param.Add("Data", defaultDataJson);
			var insertResult = Backend.GameData.Insert(tableName, param);
			if (!insertResult.IsSuccess())
			{
				dataJson = null;
				err = tableName + " Insert Failed: " + insertResult.GetErrorCode();
				return false;
			}

			dataJson = defaultDataJson;
			err = null;
			return true;
		}

		// USER_CHARACTER 행 ensure — 없으면 무료 캐릭터 목록으로 생성(있으면 기존 보유 그대로 둔다).
		// 기존 유저는 차트 조인 비용을 피하려 행이 있으면 곧장 반환한다(신규 계정일 때만 차트 조회).
		private bool ensureCharacterRow(out string dataJson, out string err)
		{
			var getResult = Backend.GameData.GetMyData("USER_CHARACTER", new Where());
			if (!getResult.IsSuccess())
			{
				dataJson = null;
				err = "USER_CHARACTER Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count > 0)
			{
				// 이미 존재 — 기존 데이터 보존(덮지 않음)
				dataJson = rows[0]["Data"].ToString();
				err = null;
				return true;
			}

			// 신규 — 무료 캐릭터 목록을 차트에서 만들어 생성
			if (buildStarterCharacterJson(out string starterJson, out string buildErr) == false)
			{
				dataJson = null;
				err = buildErr;
				return false;
			}

			Param param = new Param();
			param.Add("Data", starterJson);
			var insertResult = Backend.GameData.Insert("USER_CHARACTER", param);
			if (!insertResult.IsSuccess())
			{
				dataJson = null;
				err = "USER_CHARACTER Insert Failed: " + insertResult.GetErrorCode();
				return false;
			}

			dataJson = starterJson;
			err = null;
			return true;
		}

		// 무료 캐릭터 스타터 목록 JSON 생성 — UnlockCondition / Character 두 차트를 조인한다(서버 권위).
		// UnlockCondition 차트에서 UnlockState=Free 인 UnlockConditionID 를 모으고,
		// Character 차트에서 그 조건을 참조하는 CharacterID 를 무료 캐릭터로 지급한다.
		// 차트 컬럼명: 뒤끝 차트는 'ID' 가 예약어라 키 컬럼을 'CharacterID' / 'UnlockConditionID' 로 둔다.
		private bool buildStarterCharacterJson(out string dataJson, out string err)
		{
			// 1. UnlockCondition 차트 — Free 조건 ID 수집
			if (ChartUtil.GetChartRows("UnlockCondition", out JsonData unlockRows, out err) == false)
			{
				dataJson = null;
				return false;
			}

			HashSet<int> freeConditionIds = new HashSet<int>();
			for (int i = 0; i < unlockRows.Count; i++)
			{
				// 차트는 정수(1) 또는 문자열("Free")로 올라올 수 있어 둘 다 Free 로 인식한다(CharacterUnlockState.Free = 1).
				string unlockState = unlockRows[i]["UnlockState"].ToString();
				if (unlockState == "Free" || unlockState == "1")
				{
					freeConditionIds.Add(int.Parse(unlockRows[i]["UnlockConditionID"].ToString()));
				}
			}

			// 2. Character 차트 — Free 조건을 참조하는 캐릭터 수집
			if (ChartUtil.GetChartRows("Character", out JsonData charRows, out err) == false)
			{
				dataJson = null;
				return false;
			}

			CharacterDto dto = new CharacterDto();
			int minCharacterId = 0;
			for (int i = 0; i < charRows.Count; i++)
			{
				int unlockConditionId = int.Parse(charRows[i]["UnlockConditionID"].ToString());
				if (freeConditionIds.Contains(unlockConditionId) == false)
				{
					continue;
				}

				int characterId = int.Parse(charRows[i]["CharacterID"].ToString());
				OwnedCharacterDto owned = new OwnedCharacterDto();
				owned.characterId = characterId;
				owned.grade = 1;
				owned.level = 1;
				owned.exp = 0;
				owned.awakenLevel = 0;
				owned.dupCount = 0;
				dto.characters.Add(owned);

				// 선택 캐릭터 기본값 — 가장 작은 캐릭터 ID
				if (minCharacterId == 0 || characterId < minCharacterId)
				{
					minCharacterId = characterId;
				}
			}

			dto.selectedCharacterId = minCharacterId;
			dataJson = JsonConvert.SerializeObject(dto);
			err = null;
			return true;
		}

		// 가챠 테스트용 스타터 재화 JSON — currencyId 는 edt_enums.CurrencyInfo 순서(Gold=1, Dia=2)와 일치.
		private static string buildStarterCurrencyJson()
		{
			CurrencyDto starter = new CurrencyDto();
			CurrencyAmountDto gold = new CurrencyAmountDto();
			gold.currencyId = 1;   // Gold
			gold.amount = 100000;
			starter.amounts.Add(gold);
			CurrencyAmountDto dia = new CurrencyAmountDto();
			dia.currencyId = 2;    // Dia
			dia.amount = 10000;
			starter.amounts.Add(dia);
			return JsonConvert.SerializeObject(starter);
		}

		// 빈 인벤토리 JSON
		private static string buildEmptyInventoryJson()
		{
			return JsonConvert.SerializeObject(new InventoryDto());
		}

		// 빈 카드스킬 JSON
		private static string buildEmptyCardSkillJson()
		{
			return JsonConvert.SerializeObject(new CardSkillBookDto());
		}

		// 빈 던전 클리어 기록 JSON
		private static string buildEmptyClearedDungeonsJson()
		{
			return JsonConvert.SerializeObject(new ClearedDungeonsDto());
		}
	}
}
