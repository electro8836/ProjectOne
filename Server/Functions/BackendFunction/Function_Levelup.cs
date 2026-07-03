using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// LevelupCharacter — 선택 캐릭터를 다음 레벨로 올린다(서버 권위).
	// 경험치 충족(LevelExp 차트) + 재료/재화 비용(LevelupCost 차트)을 검증·차감한 뒤 레벨 +1.
	// 경험치는 누적 총량이라 레벨업 시 차감하지 않는다(레벨만 올림).
	public class Levelup
	{
		private const string LevelExpChartName = "LevelExp";        // 컬럼: ID(레벨), TotalExperience
		private const string LevelupCostChartName = "LevelupCost";  // 컬럼: ID(도달 레벨), MaterialID_01/MaterialValue_01, MaterialID_02/MaterialValue_02, CurrencyType_01/CurrencyValue_01, CurrencyType_02/CurrencyValue_02

		// 비용 1행(차트에서 해석한 값) — 재료 2종, 재화 2종.
		private struct CostRow
		{
			public int materialId1;
			public int materialValue1;
			public int materialId2;
			public int materialValue2;
			public int currencyId1;
			public int currencyValue1;
			public int currencyId2;
			public int currencyValue2;
		}

		public Stream LevelupCharacter()
		{
			try
			{
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				LevelupRequest req = JsonConvert.DeserializeObject<LevelupRequest>(reqJson);
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (req.characterId <= 0)
				{
					return FuncResult.Error("invalid characterId: " + req.characterId);
				}

				// 1. USER_CHARACTER 로드 → 대상 캐릭터.
				if (loadGameData("USER_CHARACTER", out JsonData charRows, out string charErr) == false)
				{
					return FuncResult.Error(charErr);
				}

				CharacterDto character = JsonConvert.DeserializeObject<CharacterDto>(charRows[0]["Data"].ToString());
				if (character == null)
				{
					return FuncResult.Error("USER_CHARACTER parse failed");
				}

				OwnedCharacterDto target = findCharacter(character, req.characterId);
				if (target == null)
				{
					return FuncResult.Error("character not owned: " + req.characterId);
				}

				int nextLevel = target.level + 1;

				// 2. 필요 경험치(LevelExp 차트) 검증.
				if (getRequiredExp(nextLevel, out int requiredExp, out string expErr) == false)
				{
					return FuncResult.Error(expErr);
				}

				if (target.exp < requiredExp)
				{
					return levelupFail("not enough exp");
				}

				// 3. 비용(LevelupCost 차트) 조회.
				if (getLevelupCost(nextLevel, out CostRow cost, out string costErr) == false)
				{
					return FuncResult.Error(costErr);
				}

				// 4. USER_INVENTORY / USER_CURRENCY 로드.
				if (loadGameData("USER_INVENTORY", out JsonData invRows, out string invErr) == false)
				{
					return FuncResult.Error(invErr);
				}

				InventoryDto inventory = JsonConvert.DeserializeObject<InventoryDto>(invRows[0]["Data"].ToString());
				if (inventory == null)
				{
					inventory = new InventoryDto();
				}

				if (loadGameData("USER_CURRENCY", out JsonData curRows, out string curErr) == false)
				{
					return FuncResult.Error(curErr);
				}

				CurrencyDto currency = JsonConvert.DeserializeObject<CurrencyDto>(curRows[0]["Data"].ToString());
				if (currency == null)
				{
					currency = new CurrencyDto();
				}

				// 5. 비용 검증(부족은 정상 실패 — 데이터 불변).
				if (hasMaterial(inventory, cost.materialId1, cost.materialValue1) == false
					|| hasMaterial(inventory, cost.materialId2, cost.materialValue2) == false)
				{
					return levelupFail("not enough material");
				}

				if (hasCurrency(currency, cost.currencyId1, cost.currencyValue1) == false
					|| hasCurrency(currency, cost.currencyId2, cost.currencyValue2) == false)
				{
					return levelupFail("not enough currency");
				}

				// 6. 차감(메모리).
				spendMaterial(inventory, cost.materialId1, cost.materialValue1);
				spendMaterial(inventory, cost.materialId2, cost.materialValue2);
				spendCurrency(currency, cost.currencyId1, cost.currencyValue1);
				spendCurrency(currency, cost.currencyId2, cost.currencyValue2);

				// 7. 레벨 +1(경험치는 누적이라 유지).
				target.level = nextLevel;

				// 8. 트랜잭션 원자 쓰기 — 캐릭터/인벤토리/재화를 하나로(부분 반영 방지).
				Param charParam = new Param();
				charParam.Add("Data", JsonConvert.SerializeObject(character));
				Param invParam = new Param();
				invParam.Add("Data", JsonConvert.SerializeObject(inventory));
				Param curParam = new Param();
				curParam.Add("Data", JsonConvert.SerializeObject(currency));

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_CHARACTER", new Where(), charParam));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), invParam));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), curParam));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				LevelupResponse response = new LevelupResponse();
				response.success = true;
				response.newLevel = target.level;
				response.newExp = target.exp;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 차트 조회 ──────────────────────────────────────────────────

		// LevelExp 차트에서 level 의 누적 필요경험치를 읽는다. 없으면 실패(최대 레벨/데이터 없음).
		private static bool getRequiredExp(int level, out int requiredExp, out string err)
		{
			requiredExp = 0;
			if (ChartUtil.GetChartRows(LevelExpChartName, out JsonData rows, out err) == false)
			{
				return false;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				// 차트 키 컬럼명: ID 는 뒤끝 예약어라 LevelExpID 로 명명됨.
				if (int.Parse(rows[i]["LevelExpID"].ToString()) == level)
				{
					requiredExp = int.Parse(rows[i]["TotalExperience"].ToString());
					err = null;
					return true;
				}
			}

			err = "level not found in LevelExp (max level?): " + level;
			return false;
		}

		// LevelupCost 차트에서 level(도달 레벨)의 비용을 읽는다. 없으면 실패.
		private static bool getLevelupCost(int level, out CostRow cost, out string err)
		{
			cost = new CostRow();
			if (ChartUtil.GetChartRows(LevelupCostChartName, out JsonData rows, out err) == false)
			{
				return false;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				// 차트 키 컬럼명: ID 는 뒤끝 예약어라 LevelupCostID 로 명명됨.
				if (parseIntOrZero(rows[i]["LevelupCostID"].ToString()) != level)
				{
					continue;
				}

				// 미사용 비용 슬롯은 차트 셀이 비어 있으므로 빈 칸은 0 으로 처리한다.
				cost.materialId1 = parseIntOrZero(rows[i]["MaterialID_01"].ToString());
				cost.materialValue1 = parseIntOrZero(rows[i]["MaterialValue_01"].ToString());
				cost.materialId2 = parseIntOrZero(rows[i]["MaterialID_02"].ToString());
				cost.materialValue2 = parseIntOrZero(rows[i]["MaterialValue_02"].ToString());
				cost.currencyId1 = currencyCellToId(rows[i]["CurrencyType_01"].ToString());
				cost.currencyValue1 = parseIntOrZero(rows[i]["CurrencyValue_01"].ToString());
				cost.currencyId2 = currencyCellToId(rows[i]["CurrencyType_02"].ToString());
				cost.currencyValue2 = parseIntOrZero(rows[i]["CurrencyValue_02"].ToString());
				err = null;
				return true;
			}

			err = "level not found in LevelupCost: " + level;
			return false;
		}

		// ── 재료 / 재화 검증·차감 ─────────────────────────────────────

		private static bool hasMaterial(InventoryDto inventory, int materialId, int required)
		{
			if (materialId <= 0 || required <= 0)
			{
				return true;
			}

			for (int i = 0; i < inventory.items.Count; i++)
			{
				OwnedItemDto item = inventory.items[i];
				if (item != null && item.itemId == materialId)
				{
					return item.count >= required;
				}
			}

			return false;
		}

		private static void spendMaterial(InventoryDto inventory, int materialId, int required)
		{
			if (materialId <= 0 || required <= 0)
			{
				return;
			}

			for (int i = 0; i < inventory.items.Count; i++)
			{
				if (inventory.items[i] != null && inventory.items[i].itemId == materialId)
				{
					inventory.items[i].count -= required;
					return;
				}
			}
		}

		private static bool hasCurrency(CurrencyDto currency, int currencyId, int required)
		{
			if (currencyId <= 0 || required <= 0)
			{
				return true;
			}

			for (int i = 0; i < currency.amounts.Count; i++)
			{
				if (currency.amounts[i].currencyId == currencyId)
				{
					return currency.amounts[i].amount >= required;
				}
			}

			return false;
		}

		private static void spendCurrency(CurrencyDto currency, int currencyId, int required)
		{
			if (currencyId <= 0 || required <= 0)
			{
				return;
			}

			for (int i = 0; i < currency.amounts.Count; i++)
			{
				if (currency.amounts[i].currencyId == currencyId)
				{
					currency.amounts[i].amount -= required;
					return;
				}
			}
		}

		// ── 공용 ──────────────────────────────────────────────────────

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

		// 내 게임데이터 1행 로드(행 없으면 실패).
		private static bool loadGameData(string tableName, out JsonData rows, out string err)
		{
			rows = null;
			var getResult = Backend.GameData.GetMyData(tableName, new Where());
			if (!getResult.IsSuccess())
			{
				err = tableName + " Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				err = tableName + " row not found";
				return false;
			}

			err = null;
			return true;
		}

		// 비용 부족 등 정상 실패 응답(데이터 불변).
		private static Stream levelupFail(string reason)
		{
			LevelupResponse fail = new LevelupResponse();
			fail.success = false;
			fail.error = reason;
			return FuncResult.Json(fail);
		}

		// 차트 셀 → 정수. 빈 칸/비정상 값은 0 으로 처리(미사용 비용 슬롯 대비).
		private static int parseIntOrZero(string cell)
		{
			if (int.TryParse(cell, out int value))
			{
				return value;
			}

			return 0;
		}

		// 재화 셀(enum 이름 또는 정수) → 정수 ID. edt_enums.CurrencyInfo 순서와 일치(EDT 미컴파일이라 직접 정의).
		private static int currencyCellToId(string cell)
		{
			if (int.TryParse(cell, out int numeric))
			{
				return numeric;
			}

			switch (cell)
			{
				case "Gold": return 1;
				case "Dia": return 2;
				case "GoldDungeonTicket": return 3;
				case "ExpDungeonTicket": return 4;
				case "RaidTicket": return 5;
				case "PVPTicket": return 6;
				default: return 0; // None
			}
		}
	}
}
