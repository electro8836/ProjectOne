using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 장비 가챠(뽑기) — 서버 권위로 비용 차감 + 장비 지급을 트랜잭션으로 처리한다.
	// 추첨 흐름: GachaInfo(비용/타입) → 재화 확인 → Gacha_Equipment(등급 가중) → Equipment(등급별 풀) 균등 추첨.
	public class Gacha
	{
		// 차트 이름(콘솔 차트 이름과 일치시킬 것).
		private const string GachaInfoChartName = "GachaInfo";
		private const string GachaEquipmentChartName = "Gacha_Equipment";
		private const string EquipmentChartName = "Equipment";

		// 한 번 호출당 뽑기 횟수 상한(어뷰징/과도한 페이로드 방지).
		private const int MaxDrawCount = 10;

		// 호출마다 새 인스턴스 → 워밍 컨테이너 재사용 시 동일 시드 위험을 피한다(정적 캐시 금지).
		private readonly Random _rng = new Random();

		// EquipmentGachaDraw — 장비 뽑기 진입 핸들러(Function_Main 이 action 분기로 호출).
		public Stream EquipmentGachaDraw()
		{
			try
			{
				// 1. 요청 파싱
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				string reqJson = Backend.Content["req"].ToString();
				GachaDrawRequest req = JsonConvert.DeserializeObject<GachaDrawRequest>(reqJson);
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 2. 입력 검증
				if (req.gachaId <= 0)
				{
					return FuncResult.Error("invalid gachaId: " + req.gachaId);
				}

				if (req.count < 1 || req.count > MaxDrawCount)
				{
					return FuncResult.Error("invalid count: " + req.count);
				}

				// 3. GachaInfo 차트 조회 → 비용/타입 결정(서버 권위 — 클라 값 불신)
				if (ChartUtil.ResolveChartFileId(GachaInfoChartName, out string gachaInfoFileId, out string infoErr) == false)
				{
					return FuncResult.Error(infoErr);
				}

				var gachaInfoResult = Backend.Chart.GetChartContents(gachaInfoFileId);
				if (!gachaInfoResult.IsSuccess())
				{
					return FuncResult.Error("Failed to get GachaInfo chart: " + gachaInfoResult.GetErrorCode());
				}

				JsonData infoRows = gachaInfoResult.FlattenRows();
				string gachaType = null;
				string currencyTypeName = null;
				int currencyCost = -1;
				for (int i = 0; i < infoRows.Count; i++)
				{
					if (int.Parse(infoRows[i]["GachaID"].ToString()) == req.gachaId)
					{
						gachaType = infoRows[i]["GachaType"].ToString();
						currencyTypeName = infoRows[i]["CurrencyType"].ToString();
						currencyCost = int.Parse(infoRows[i]["CurrencyCost"].ToString());
						break;
					}
				}

				if (currencyCost < 0)
				{
					return FuncResult.Error("gachaId not found in GachaInfo: " + req.gachaId);
				}

				// 이번 범위는 장비 가챠만 처리.
				if (gachaType != "Equipment")
				{
					return FuncResult.Error("unsupported gachaType: " + gachaType);
				}

				int currencyId = currencyNameToId(currencyTypeName);
				if (currencyId == 0)
				{
					return FuncResult.Error("invalid CurrencyType: " + currencyTypeName);
				}

				// 4. 총비용
				int totalCost = currencyCost * req.count;

				// 5. USER_CURRENCY 조회 → 잔액 확인
				var currencyGet = Backend.GameData.GetMyData("USER_CURRENCY", new Where());
				if (!currencyGet.IsSuccess())
				{
					return FuncResult.Error("Failed to get USER_CURRENCY: " + currencyGet.GetErrorCode());
				}

				JsonData currencyRows = currencyGet.FlattenRows();
				if (currencyRows.Count == 0)
				{
					return FuncResult.Error("USER_CURRENCY row not found");
				}

				CurrencyDto currencyData = JsonConvert.DeserializeObject<CurrencyDto>(currencyRows[0]["Data"].ToString());
				if (currencyData == null)
				{
					currencyData = new CurrencyDto();
				}

				int currencyIndex = -1;
				for (int i = 0; i < currencyData.amounts.Count; i++)
				{
					if (currencyData.amounts[i].currencyId == currencyId)
					{
						currencyIndex = i;
						break;
					}
				}

				int currentAmount = 0;
				if (currencyIndex >= 0)
				{
					currentAmount = currencyData.amounts[currencyIndex].amount;
				}

				// 잔액 부족 → 정상 실패(예외 아님, 데이터 불변)
				if (currentAmount < totalCost)
				{
					GachaDrawResponse fail = new GachaDrawResponse();
					fail.success = false;
					fail.error = "not enough currency";
					fail.currencyId = currencyId;
					fail.spentAmount = 0;
					fail.remainingAmount = currentAmount;
					fail.itemIds = new int[0];
					fail.grades = new int[0];
					return FuncResult.Json(fail);
				}

				// 6. Gacha_Equipment 차트 조회 → gachaId 그룹의 등급 가중치 수집
				if (ChartUtil.ResolveChartFileId(GachaEquipmentChartName, out string gachaEquipFileId, out string gradeErr) == false)
				{
					return FuncResult.Error(gradeErr);
				}

				var gradeResult = Backend.Chart.GetChartContents(gachaEquipFileId);
				if (!gradeResult.IsSuccess())
				{
					return FuncResult.Error("Failed to get Gacha_Equipment chart: " + gradeResult.GetErrorCode());
				}

				JsonData gradeRows = gradeResult.FlattenRows();
				List<string> grades = new List<string>();
				List<int> weights = new List<int>();
				int weightSum = 0;
				for (int i = 0; i < gradeRows.Count; i++)
				{
					if (int.Parse(gradeRows[i]["GachaID"].ToString()) == req.gachaId)
					{
						grades.Add(gradeRows[i]["Grade"].ToString());
						int weight = int.Parse(gradeRows[i]["Weight"].ToString());
						weights.Add(weight);
						weightSum += weight;
					}
				}

				if (weightSum <= 0)
				{
					return FuncResult.Error("no grade weights for gachaId: " + req.gachaId);
				}

				// 7. Equipment 차트 조회 → 등급별 후보 풀 분류(한 번만)
				if (ChartUtil.ResolveChartFileId(EquipmentChartName, out string equipFileId, out string equipErr) == false)
				{
					return FuncResult.Error(equipErr);
				}

				var equipResult = Backend.Chart.GetChartContents(equipFileId);
				if (!equipResult.IsSuccess())
				{
					return FuncResult.Error("Failed to get Equipment chart: " + equipResult.GetErrorCode());
				}

				JsonData equipRows = equipResult.FlattenRows();
				Dictionary<string, List<int>> poolByGrade = new Dictionary<string, List<int>>();
				for (int i = 0; i < equipRows.Count; i++)
				{
					string grade = equipRows[i]["Grade"].ToString();
					int id = int.Parse(equipRows[i]["ItemID"].ToString());
					if (poolByGrade.TryGetValue(grade, out List<int> pool) == false)
					{
						pool = new List<int>();
						poolByGrade[grade] = pool;
					}

					pool.Add(id);
				}

				// 8. count 회 추첨 — 등급 가중 추첨 후 해당 등급 풀에서 균등 추첨
				int[] resultItemIds = new int[req.count];
				int[] resultGrades = new int[req.count];
				for (int n = 0; n < req.count; n++)
				{
					int r = _rng.Next(0, weightSum);
					int acc = 0;
					string pickedGrade = grades[grades.Count - 1];
					for (int g = 0; g < grades.Count; g++)
					{
						acc += weights[g];
						if (r < acc)
						{
							pickedGrade = grades[g];
							break;
						}
					}

					if (poolByGrade.TryGetValue(pickedGrade, out List<int> gradePool) == false || gradePool.Count == 0)
					{
						return FuncResult.Error("no equipment for grade: " + pickedGrade);
					}

					int pickIndex = _rng.Next(0, gradePool.Count);
					resultItemIds[n] = gradePool[pickIndex];
					resultGrades[n] = gradeNameToId(pickedGrade);
				}

				// 9. USER_INVENTORY 조회 → 추첨 결과 반영(메모리)
				var inventoryGet = Backend.GameData.GetMyData("USER_INVENTORY", new Where());
				if (!inventoryGet.IsSuccess())
				{
					return FuncResult.Error("Failed to get USER_INVENTORY: " + inventoryGet.GetErrorCode());
				}

				JsonData inventoryRows = inventoryGet.FlattenRows();
				if (inventoryRows.Count == 0)
				{
					return FuncResult.Error("USER_INVENTORY row not found");
				}

				InventoryDto inventoryData = JsonConvert.DeserializeObject<InventoryDto>(inventoryRows[0]["Data"].ToString());
				if (inventoryData == null)
				{
					inventoryData = new InventoryDto();
				}

				for (int n = 0; n < resultItemIds.Length; n++)
				{
					int itemId = resultItemIds[n];
					int itemIndex = -1;
					for (int i = 0; i < inventoryData.items.Count; i++)
					{
						if (inventoryData.items[i].itemId == itemId)
						{
							itemIndex = i;
							break;
						}
					}

					if (itemIndex >= 0)
					{
						inventoryData.items[itemIndex].count += 1;
					}
					else
					{
						OwnedItemDto newItem = new OwnedItemDto();
						newItem.itemId = itemId;
						newItem.count = 1;
						newItem.enhanceLevel = 0;
						inventoryData.items.Add(newItem);
					}
				}

				// 10. 재화 차감(메모리)
				int remainingAmount = currentAmount - totalCost;
				currencyData.amounts[currencyIndex].amount = remainingAmount;

				// 11. 트랜잭션 원자 쓰기 — 재화 차감 + 인벤토리 지급을 하나로(부분 반영 방지).
				//     SetUpdate 의 Where 는 함수 컨텍스트에서 내 행을 가리킨다(Dungeon 의 Update(new Where()) 와 동일 우회).
				Param currencyUpdate = new Param();
				currencyUpdate.Add("Data", JsonConvert.SerializeObject(currencyData));
				Param inventoryUpdate = new Param();
				inventoryUpdate.Add("Data", JsonConvert.SerializeObject(inventoryData));

				List<TransactionValue> transactionList = new List<TransactionValue>();
				transactionList.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), currencyUpdate));
				transactionList.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), inventoryUpdate));

				var transactionResult = Backend.GameData.TransactionWriteV2(transactionList);
				if (!transactionResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + transactionResult.GetErrorCode());
				}

				// 12. 응답
				GachaDrawResponse response = new GachaDrawResponse();
				response.success = true;
				response.itemIds = resultItemIds;
				response.grades = resultGrades;
				response.currencyId = currencyId;
				response.spentAmount = totalCost;
				response.remainingAmount = remainingAmount;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// SkillGachaDraw — 스킬 뽑기 진입 핸들러(stub). 장비와 호출 경로를 분리하기 위한 자리.
		// 스킬 추첨 차트(Gacha_Skill)·SkillInfo 등급·USER_SKILL 지급이 준비되면 EquipmentGachaDraw 와 동일 패턴으로 구현한다.
		public Stream SkillGachaDraw()
		{
			return FuncResult.Error("skill gacha not implemented");
		}

		// 재화 종류 enum(edt_enums.CurrencyInfo) 이름 → 정수 매핑. EDT 는 서버에 컴파일되지 않으므로 직접 정의한다.
		// edt_enums.cs 의 CurrencyInfo 순서 변경 시 함께 갱신할 것.
		private static int currencyNameToId(string name)
		{
			switch (name)
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

		// 등급 enum(edt_enums.ItemGradeTypes) 이름 → 정수 매핑(응답용). edt_enums.cs 변경 시 함께 갱신할 것.
		private static int gradeNameToId(string name)
		{
			switch (name)
			{
				case "Normal": return 1;
				case "Magic": return 2;
				case "Rare": return 3;
				case "Epic": return 4;
				case "Legendary": return 5;
				default: return 0; // None
			}
		}
	}
}
