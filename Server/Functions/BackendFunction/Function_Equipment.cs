using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 장비 성장·분해 — 강화·승급·전이·분해. 판정·비용은 클라와 같은 공유 규칙(EquipmentGrowthRules)으로 한다.
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

		// 분해 — 장비를 없애고 주 재화 일부를 돌려준다. 환급이 0 이어도 장비는 사라진다.
		public Stream EquipmentDecompose()
		{
			try
			{
				if (begin(out EquipmentDecomposeRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				EquipmentInstanceDto target = findEquipment(inventory, req.uid);
				if (target == null)
				{
					return FuncResult.Error("equipment not owned: " + req.uid);
				}

				if (target.equippedSlot != (int)EquipSlotTypes.None || target.locked == true)
				{
					return FuncResult.Error("cannot decompose equipped or locked: " + req.uid);
				}

				List<CurrencyCost> gains = new List<CurrencyCost>(1);
				EquipmentGrowthRules.GetDecomposeRefund(target.itemId, (ItemGradeType)target.grade, target.level, gains);

				inventory.equipments.Remove(target);
				CurrencyUtil.AddAll(currency, gains);

				string saveErr = saveDecomposed(inventory, currency);
				if (saveErr != null)
				{
					return FuncResult.Error(saveErr);
				}

				EquipmentDecomposeResponse response = new EquipmentDecomposeResponse();
				response.success = true;
				response.uid = req.uid;
				response.gained = CurrencyUtil.ToSpentDto(gains);

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 일괄 분해 — 요청한 장비 중 분해할 수 있는 것만 없앤다. 착용·잠금·미보유는 건너뛴다(거절하지 않는다).
		public Stream EquipmentDecomposeAll()
		{
			try
			{
				if (begin(out EquipmentDecomposeAllRequest req, out InventoryDto inventory, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				if (req.uids == null || req.uids.Length == 0)
				{
					return FuncResult.Error("uids is empty");
				}

				List<long> decomposed = new List<long>(req.uids.Length);
				List<CurrencyCost> total = new List<CurrencyCost>(1);
				List<CurrencyCost> gains = new List<CurrencyCost>(1);
				for (int i = 0; i < req.uids.Length; i++)
				{
					// 지운 장비는 다시 찾히지 않으므로 같은 UID 가 두 번 와도 한 번만 분해된다.
					EquipmentInstanceDto target = findEquipment(inventory, req.uids[i]);
					if (target == null || target.equippedSlot != (int)EquipSlotTypes.None || target.locked == true)
					{
						continue;
					}

					EquipmentGrowthRules.GetDecomposeRefund(target.itemId, (ItemGradeType)target.grade, target.level, gains);
					CurrencyUtil.AddCosts(total, gains);

					inventory.equipments.Remove(target);
					decomposed.Add(target.uid);
				}

				if (decomposed.Count > 0)
				{
					CurrencyUtil.AddAll(currency, total);

					string saveErr = saveDecomposed(inventory, currency);
					if (saveErr != null)
					{
						return FuncResult.Error(saveErr);
					}
				}

				EquipmentDecomposeAllResponse response = new EquipmentDecomposeAllResponse();
				response.success = true;
				response.uids = decomposed.ToArray();
				response.gained = CurrencyUtil.ToSpentDto(total);

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// SaveEquipmentLock — 잠근 장비 UID 전체(최종 상태)를 저장한다. 목록에 있는 것만 잠금, 나머지는 해제.
		// 보유하지 않은 UID 는 무시한다 — 분해 등으로 이미 사라졌을 수 있다.
		public Stream SaveEquipmentLock()
		{
			try
			{
				if (beginInventory(out SaveEquipmentLockRequest req, out InventoryDto inventory, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				if (req.lockedUids == null)
				{
					return FuncResult.Error("req parse failed");
				}

				HashSet<long> locked = new HashSet<long>(req.lockedUids);
				for (int i = 0; i < inventory.equipments.Count; i++)
				{
					EquipmentInstanceDto equipment = inventory.equipments[i];
					if (equipment != null)
					{
						equipment.locked = locked.Contains(equipment.uid);
					}
				}

				string saveErr = saveInventory(inventory);
				if (saveErr != null)
				{
					return FuncResult.Error(saveErr);
				}

				SaveEquipmentLockResponse response = new SaveEquipmentLockResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// SaveDecomposeSetting — 분해 조건을 저장한다. 이후 지급부터 자동 분해 판정에 쓰인다(RewardApplier).
		public Stream SaveDecomposeSetting()
		{
			try
			{
				if (beginInventory(out SaveDecomposeSettingRequest req, out InventoryDto inventory, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				if (req.setting == null || req.setting.quality < 0 || req.setting.quality > EquipmentQuality.Max)
				{
					return FuncResult.Error("invalid setting");
				}

				inventory.decomposeSetting = req.setting;

				string saveErr = saveInventory(inventory);
				if (saveErr != null)
				{
					return FuncResult.Error(saveErr);
				}

				SaveDecomposeSettingResponse response = new SaveDecomposeSettingResponse();
				response.success = true;
				return FuncResult.Json(response);
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

		// 요청 파싱 → INVENTORY 로드. 재화를 건드리지 않는 저장(잠금·분해 조건)용.
		private static bool beginInventory<T>(out T req, out InventoryDto inventory, out string err) where T : class
		{
			req = null;
			inventory = null;

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

			return MyData.Load("USER_INVENTORY", out inventory, out err);
		}

		// 실패하면 원인을, 성공하면 null 을 돌려준다.
		private static string saveInventory(InventoryDto inventory)
		{
			var updateResult = Backend.GameData.Update("USER_INVENTORY", new Where(), toParam(inventory));
			if (!updateResult.IsSuccess())
			{
				return "USER_INVENTORY Update Failed: " + updateResult.GetErrorCode();
			}

			return null;
		}

		// 분해 결과(장비 제거 + 재화 가산) 원자 저장. 실패하면 원인을, 성공하면 null 을 돌려준다.
		// 착용 장비는 분해되지 않아 전투력이 그대로다 — 랭킹을 갱신하지 않는다.
		private static string saveDecomposed(InventoryDto inventory, CurrencyDto currency)
		{
			List<TransactionValue> tx = new List<TransactionValue>();
			tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), toParam(inventory)));
			tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				return "Transaction failed: " + txResult.GetErrorCode();
			}

			return null;
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

			RankOps.Refresh(null, inventory, null, null, null);

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
