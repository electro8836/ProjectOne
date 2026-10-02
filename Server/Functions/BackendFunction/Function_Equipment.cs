using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 장비 성장 — 강화·승급·전이. 판정·비용은 클라와 같은 공유 규칙(EquipmentGrowthRules)으로 한다.
	//
	// 세 행위 모두 재화 차감 + 장비 등급·레벨 변경이라 INVENTORY·CURRENCY 를 트랜잭션 하나로 저장한다.
	// 착용 상태(equippedSlot)는 바뀌지 않으므로 LOADOUT 은 건드리지 않는다.
	public class EquipmentFunctions
	{
		// 강화 묶음 1회 요청의 최대 횟수 — 서버 처리 시간을 묶어 둔다.
		private const int MaxEnhanceCount = 100;

		public Stream EquipmentEnhance()
		{
			try
			{
				if (begin(out EquipmentEnhanceRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				EquipmentInstanceDto target = findEquipment(inventory, req.uid);
				if (target == null)
				{
					return FuncResult.Error("equipment not owned: " + req.uid);
				}

				// 레벨이 어긋났으면(앞 묶음 부분 적용 등) 바꾸지 않고 현재 상태로 맞추게 한다.
				List<CurrencyCost> spent = new List<CurrencyCost>(2);
				if (target.level != req.fromLevel || req.count <= 0)
				{
					return respond(spent, target, null);
				}

				int count = (req.count < MaxEnhanceCount) ? req.count : MaxEnhanceCount;
				ItemGradeType grade = (ItemGradeType)target.grade;
				List<CurrencyCost> costs = new List<CurrencyCost>(2);
				for (int i = 0; i < count; i++)
				{
					if (EquipmentGrowthRules.CanEnhance(target.itemId, grade, target.level) == false)
					{
						break;
					}

					EquipmentGrowthRules.GetEnhanceCost(target.itemId, target.level, costs);
					if (costs.Count == 0 || CurrencyUtil.TrySpendAll(currency, costs) == false)
					{
						break;
					}

					target.level++;
					CurrencyUtil.AddCosts(spent, costs);
				}

				if (spent.Count == 0)
				{
					return respond(spent, target, null);
				}

				return commit(inventory, currency, spent, target, null);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		public Stream EquipmentPromote()
		{
			try
			{
				if (begin(out EquipmentPromoteRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				EquipmentInstanceDto target = findEquipment(inventory, req.uid);
				if (target == null)
				{
					return FuncResult.Error("equipment not owned: " + req.uid);
				}

				ItemGradeType grade = (ItemGradeType)target.grade;
				PromoteBlock block = EquipmentGrowthRules.GetPromoteBlock(target.itemId, grade, target.level);
				if (block != PromoteBlock.None)
				{
					return FuncResult.Error("cannot promote: " + block);
				}

				List<CurrencyCost> costs = new List<CurrencyCost>(3);
				EquipmentGrowthRules.GetPromoteCost(target.itemId, grade, costs);
				if (costs.Count == 0 || CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency");
				}

				// 등급만 바꾼다 — 레벨·품질은 유지된다 (설계 6.2).
				target.grade = (int)EquipmentGrowthRules.GetNextGrade(target.itemId, grade);

				return commit(inventory, currency, costs, target, null);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		public Stream EquipmentTransfer()
		{
			try
			{
				if (begin(out EquipmentTransferRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				EquipmentInstanceDto a = findEquipment(inventory, req.sourceUid);
				EquipmentInstanceDto b = findEquipment(inventory, req.targetUid);
				if (a == null || b == null)
				{
					return FuncResult.Error("equipment not owned: " + req.sourceUid + ", " + req.targetUid);
				}

				ItemGradeType gradeA = (ItemGradeType)a.grade;
				ItemGradeType gradeB = (ItemGradeType)b.grade;
				TransferBlock block = EquipmentGrowthRules.GetTransferBlock(a.uid, a.itemId, gradeA, a.level, b.uid, b.itemId, gradeB, b.level);
				if (block != TransferBlock.None)
				{
					return FuncResult.Error("cannot transfer: " + block);
				}

				List<CurrencyCost> costs = new List<CurrencyCost>(1);
				EquipmentGrowthRules.GetTransferCost(a.itemId, gradeA, b.itemId, gradeB, costs);
				if (costs.Count == 0 || CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency");
				}

				// 등급·강화도를 교체한다. 품질은 각자 그대로 남는다.
				int grade = a.grade;
				int level = a.level;
				a.grade = b.grade;
				a.level = b.level;
				b.grade = grade;
				b.level = level;

				return commit(inventory, currency, costs, a, b);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 공용 ──────────────────────────────────────────────────────

		// 테이블 로드 → 요청 파싱 → INVENTORY·CURRENCY 로드.
		private static bool begin<T>(out T req, out InventoryDto inventory, out CurrencyDto currency, out string err) where T : class
		{
			req = null;
			inventory = null;
			currency = null;

			if (GameData.EnsureLoaded(out err) == false)
			{
				return false;
			}

			if (Backend.HasKey("req") == false)
			{
				err = "req key is not exist";
				return false;
			}

			req = JsonConvert.DeserializeObject<T>(Backend.Content["req"].ToString());
			if (req == null)
			{
				err = "req parse failed";
				return false;
			}

			if (MyData.Load("USER_INVENTORY", out inventory, out err) == false)
			{
				return false;
			}

			return MyData.Load("USER_CURRENCY", out currency, out err);
		}

		// 원자 저장 후 응답 — 바뀐 장비 최종 상태와 차감량.
		private static Stream commit(InventoryDto inventory, CurrencyDto currency, List<CurrencyCost> costs, EquipmentInstanceDto changedA, EquipmentInstanceDto changedB)
		{
			List<TransactionValue> tx = new List<TransactionValue>();
			tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
			tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
			}

			return respond(costs, changedA, changedB);
		}

		// 바뀐(또는 맞춰야 할) 장비 상태와 차감량을 응답한다.
		private static Stream respond(List<CurrencyCost> costs, EquipmentInstanceDto changedA, EquipmentInstanceDto changedB)
		{
			EquipmentGrowthResponse response = new EquipmentGrowthResponse();
			response.success = true;
			response.equipments = (changedB != null)
				? new EquipmentInstanceDto[] { changedA, changedB }
				: new EquipmentInstanceDto[] { changedA };

			response.spent = CurrencyUtil.ToSpentDto(costs);

			return FuncResult.Json(response);
		}

		private static EquipmentInstanceDto findEquipment(InventoryDto inventory, long uid)
		{
			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto equipment = inventory.equipments[i];
				if (equipment != null && equipment.uid == uid)
				{
					return equipment;
				}
			}

			return null;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
