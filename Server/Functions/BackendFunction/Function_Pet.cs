using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using EDT;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 펫 강화·승급 + 외형(펫 장착·코스튬 착용) 저장. 판정·비용은 클라와 같은 공유 규칙(PetRules)으로 한다.
	//
	// 강화·승급은 재화 차감 + 펫 상태 변경이라 PET·CURRENCY 를 트랜잭션 하나로 저장한다.
	// 외형 저장은 재화가 없고 PET·COSTUME 를 함께 저장한다.
	public class PetFunctions
	{
		// 강화 묶음 1회 요청의 최대 횟수 — 서버 처리 시간을 묶어 둔다.
		private const int MaxEnhanceCount = 100;

		public Stream PetEnhance()
		{
			try
			{
				if (begin(out PetEnhanceRequest req, out PetDto pet, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				PetEntryDto target = findPet(pet, req.petId);
				if (target == null)
				{
					return FuncResult.Error("pet not owned: " + req.petId);
				}

				// 레벨이 어긋났으면(앞 묶음 부분 적용 등) 바꾸지 않고 현재 상태로 맞추게 한다.
				List<CurrencyCost> spent = new List<CurrencyCost>(1);
				if (target.level != req.fromLevel || req.count <= 0)
				{
					return respond(spent, target);
				}

				int count = (req.count < MaxEnhanceCount) ? req.count : MaxEnhanceCount;
				ItemGradeType grade = (ItemGradeType)target.grade;
				List<CurrencyCost> costs = new List<CurrencyCost>(1);
				for (int i = 0; i < count; i++)
				{
					if (PetRules.GetEnhanceBlock(target.level, grade) != PetEnhanceBlock.None)
					{
						break;
					}

					if (fillEnhanceCost(target.level, costs) == false || CurrencyUtil.TrySpendAll(currency, costs) == false)
					{
						break;
					}

					target.level++;
					CurrencyUtil.AddCosts(spent, costs);
				}

				if (spent.Count == 0)
				{
					return respond(spent, target);
				}

				return commit(pet, currency, spent, target);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		public Stream PetPromote()
		{
			try
			{
				if (begin(out PetPromoteRequest req, out PetDto pet, out CurrencyDto currency, out string err) == false)
				{
					return FuncResult.Error(err);
				}

				PetEntryDto target = findPet(pet, req.petId);
				if (target == null)
				{
					return FuncResult.Error("pet not owned: " + req.petId);
				}

				ItemGradeType grade = (ItemGradeType)target.grade;
				PetPromoteBlock block = PetRules.GetPromoteBlock(grade);
				if (block != PetPromoteBlock.None)
				{
					return FuncResult.Error("cannot promote: " + block);
				}

				Table_PetPromotion.Row row = PetRules.GetPromotion(grade);
				List<CurrencyCost> costs = new List<CurrencyCost>(1);
				CurrencyCost cost;
				cost.currency = row.CostCurrencyID;
				cost.amount = row.CostCurrencyValue;
				costs.Add(cost);

				if (CurrencyUtil.TrySpendAll(currency, costs) == false)
				{
					return FuncResult.Error("not enough currency");
				}

				target.grade = (int)row.ToGrade;

				return commit(pet, currency, costs, target);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// 펫 장착 + 코스튬 착용 저장. 보유하지 않은 것·타입이 맞지 않는 코스튬은 거절한다(0 = 해제).
		public Stream SaveAppearance()
		{
			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				SaveAppearanceRequest req = JsonConvert.DeserializeObject<SaveAppearanceRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				if (MyData.Load("USER_PET", out PetDto pet, out string petErr) == false)
				{
					return FuncResult.Error(petErr);
				}

				if (MyData.Load("USER_COSTUME", out CostumeDto costume, out string costumeErr) == false)
				{
					return FuncResult.Error(costumeErr);
				}

				if (req.equippedPetId != 0 && findPet(pet, req.equippedPetId) == null)
				{
					return FuncResult.Error("pet not owned: " + req.equippedPetId);
				}

				if (isValidCostume(costume, req.costumeWeaponId, CostumeType.Weapon) == false)
				{
					return FuncResult.Error("invalid weapon costume: " + req.costumeWeaponId);
				}

				if (isValidCostume(costume, req.costumeBodyId, CostumeType.Body) == false)
				{
					return FuncResult.Error("invalid body costume: " + req.costumeBodyId);
				}

				pet.equippedPetId = req.equippedPetId;
				costume.equippedWeaponId = req.costumeWeaponId;
				costume.equippedBodyId = req.costumeBodyId;

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate("USER_PET", new Where(), toParam(pet)));
				tx.Add(TransactionValue.SetUpdate("USER_COSTUME", new Where(), toParam(costume)));

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
				}

				SaveAppearanceResponse response = new SaveAppearanceResponse();
				response.success = true;
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 공용 ──────────────────────────────────────────────────────

		// 테이블 로드 → 요청 파싱 → PET·CURRENCY 로드.
		private static bool begin<T>(out T req, out PetDto pet, out CurrencyDto currency, out string err) where T : class
		{
			req = null;
			pet = null;
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

			if (MyData.Load("USER_PET", out pet, out err) == false)
			{
				return false;
			}

			return MyData.Load("USER_CURRENCY", out currency, out err);
		}

		// 원자 저장 후 응답 — 펫 최종 상태와 차감량.
		private static Stream commit(PetDto pet, CurrencyDto currency, List<CurrencyCost> costs, PetEntryDto changed)
		{
			List<TransactionValue> tx = new List<TransactionValue>();
			tx.Add(TransactionValue.SetUpdate("USER_PET", new Where(), toParam(pet)));
			tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), toParam(currency)));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				return FuncResult.Error("Transaction failed: " + txResult.GetErrorCode());
			}

			return respond(costs, changed);
		}

		private static Stream respond(List<CurrencyCost> costs, PetEntryDto changed)
		{
			PetGrowthResponse response = new PetGrowthResponse();
			response.success = true;
			response.pet = changed;
			response.spent = CurrencyUtil.ToSpentDto(costs);
			return FuncResult.Json(response);
		}

		private static bool fillEnhanceCost(int level, List<CurrencyCost> costs)
		{
			costs.Clear();

			EDT.Currency currency;
			int amount;
			if (PetRules.TryGetEnhanceCost(level, out currency, out amount) == false)
			{
				return false;
			}

			CurrencyCost cost;
			cost.currency = currency;
			cost.amount = amount;
			costs.Add(cost);
			return true;
		}

		private static PetEntryDto findPet(PetDto pet, int petId)
		{
			for (int i = 0; i < pet.pets.Count; i++)
			{
				PetEntryDto entry = pet.pets[i];
				if (entry != null && entry.petId == petId)
				{
					return entry;
				}
			}

			return null;
		}

		// 0 은 해제라 항상 통과. 아니면 보유 + 타입 일치.
		private static bool isValidCostume(CostumeDto costume, int costumeId, CostumeType type)
		{
			if (costumeId == 0)
			{
				return true;
			}

			if (costume.owned.Contains(costumeId) == false)
			{
				return false;
			}

			Table_Costume.Row row = Table_Costume.Get(costumeId);
			return row != null && row.CostumeType == type;
		}

		private static Param toParam(object dto)
		{
			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(dto));
			return param;
		}
	}
}
