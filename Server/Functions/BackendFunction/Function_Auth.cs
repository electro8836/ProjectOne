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

				// 2. USER_INFO — 신규면 생성, 기존이면 로드
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
					response.exp = int.Parse(rows[0]["Exp"].ToString());
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
	}
}
