using System;
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

				if (ensureDomainRow("USER_LOADOUT", JsonConvert.SerializeObject(new LoadoutDto()), out string loadoutJson, out string loadoutErr) == false)
				{
					return FuncResult.Error(loadoutErr);
				}

				response.loadout = JsonConvert.DeserializeObject<LoadoutDto>(loadoutJson);

				if (ensureDomainRow("USER_COSTUME", JsonConvert.SerializeObject(new CostumeDto()), out string costumeJson, out string costumeErr) == false)
				{
					return FuncResult.Error(costumeErr);
				}

				response.costume = JsonConvert.DeserializeObject<CostumeDto>(costumeJson);

				if (ensureDomainRow("USER_DUNGEON", buildEmptyClearedDungeonsJson(), out string dungeonJson, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				response.clearedDungeons = JsonConvert.DeserializeObject<ClearedDungeonsDto>(dungeonJson);

				if (ensureDomainRow("USER_MASTERY", JsonConvert.SerializeObject(new MasteryDto()), out string masteryJson, out string masteryErr) == false)
				{
					return FuncResult.Error(masteryErr);
				}

				response.mastery = JsonConvert.DeserializeObject<MasteryDto>(masteryJson);

				if (ensureDomainRow("USER_QUEST", JsonConvert.SerializeObject(new QuestDto()), out string questJson, out string questErr) == false)
				{
					return FuncResult.Error(questErr);
				}

				response.quest = JsonConvert.DeserializeObject<QuestDto>(questJson);

				if (ensureDomainRow("USER_PET", JsonConvert.SerializeObject(new PetDto()), out string petJson, out string petErr) == false)
				{
					return FuncResult.Error(petErr);
				}

				response.pet = JsonConvert.DeserializeObject<PetDto>(petJson);

				if (ensureDomainRow("USER_DAILYBONUS", JsonConvert.SerializeObject(new DailyBonusDto()), out string dailyBonusJson, out string dailyBonusErr) == false)
				{
					return FuncResult.Error(dailyBonusErr);
				}

				response.dailyBonus = JsonConvert.DeserializeObject<DailyBonusDto>(dailyBonusJson);

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

		// 가챠 테스트용 스타터 재화 JSON — currencyId 는 클라와 같은 EDT.Currency 정수.
		private static string buildStarterCurrencyJson()
		{
			CurrencyDto starter = new CurrencyDto();
			CurrencyAmountDto gold = new CurrencyAmountDto();
			gold.currencyId = (int)EDT.Currency.Gold;
			gold.amount = 100000;
			starter.amounts.Add(gold);
			CurrencyAmountDto dia = new CurrencyAmountDto();
			dia.currencyId = (int)EDT.Currency.Dia;
			dia.amount = 10000;
			starter.amounts.Add(dia);
			return JsonConvert.SerializeObject(starter);
		}

		// 빈 인벤토리 JSON
		private static string buildEmptyInventoryJson()
		{
			return JsonConvert.SerializeObject(new InventoryDto());
		}

		// 빈 던전 클리어 기록 JSON
		private static string buildEmptyClearedDungeonsJson()
		{
			return JsonConvert.SerializeObject(new ClearedDungeonsDto());
		}
	}
}
