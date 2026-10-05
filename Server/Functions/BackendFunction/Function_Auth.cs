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
		// 최초 지급 보상 그룹 — Reward 테이블에서 목록(기본 장비·시작 재화)을 관리한다.
		private const int StarterRewardGroupId = 100;

		// GetUserData — 로그인 스냅샷 번들. 서버가 USER_* 도메인 테이블을 읽어 한 응답으로 조립한다.
		// 신규/기존 무관하게 도메인 행을 ensure(없으면 생성)해 개발 중 추가된 테이블을 기존 유저에게 자동 마이그레이션한다.
		// 신규 계정이면 최초 지급 후 USER_INFO 를 생성한다(아래 4번).
		public Stream GetUserData()
		{
			string tableName = "USER_INFO";

			try
			{
				if (GameData.EnsureLoaded(out string loadErr) == false)
				{
					return FuncResult.Error(loadErr);
				}

				// 1. 현재 로그인한 유저의 행이 이미 존재하는지 조회
				var getResult = Backend.GameData.GetMyData(tableName, new Where());

				if (!getResult.IsSuccess())
				{
					return FuncResult.Error("Data Get Failed: " + getResult.GetErrorCode());
				}

				JsonData rows = getResult.FlattenRows();
				GetUserDataResponse response = new GetUserDataResponse();
				response.success = true;

				// 2. USER_INFO — 유저 존재 앵커. 생성은 최초 지급과 같은 트랜잭션에서 한다(4번).
				bool isNewAccount = rows.Count == 0;

				// 3. 도메인 행 ensure(없으면 생성) + 그 데이터를 응답 번들에 실어 보낸다.
				//    새 도메인 테이블이 추가되면 여기 ensure + 응답 세팅 한 쌍만 더하면 된다.
				//    (콘솔에 실제 존재하는 테이블만 대상 — 없는 테이블은 GetMyData 가 실패한다.)
				if (ensureDomainRow("USER_CURRENCY", JsonConvert.SerializeObject(new CurrencyDto()), out string currencyJson, out string currencyErr) == false)
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

				if (ensureDomainRow("USER_DUNGEON", JsonConvert.SerializeObject(new DungeonProgressDto()), out string dungeonJson, out string dungeonErr) == false)
				{
					return FuncResult.Error(dungeonErr);
				}

				response.dungeonProgress = JsonConvert.DeserializeObject<DungeonProgressDto>(dungeonJson);

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

				if (ensureDomainRow("USER_SHOP", JsonConvert.SerializeObject(new ShopDto()), out string shopJson, out string shopErr) == false)
				{
					return FuncResult.Error(shopErr);
				}

				response.shop = JsonConvert.DeserializeObject<ShopDto>(shopJson);

				// 히어로패스 — 시즌 기준일은 행이 처음 생기는 날(= 계정 생성일)이다.
				HeroPassDto heroPassDefault = new HeroPassDto();
				heroPassDefault.startResetDay = HeroPassOps.Today();
				if (ensureDomainRow("USER_HEROPASS", JsonConvert.SerializeObject(heroPassDefault), out string heroPassJson, out string heroPassErr) == false)
				{
					return FuncResult.Error(heroPassErr);
				}

				response.heroPass = JsonConvert.DeserializeObject<HeroPassDto>(heroPassJson);
				HeroPassRules.EnsureSeason(response.heroPass, HeroPassOps.Today());

				// 필드 처치 배치 정산 세션 — 로그인마다 새 시드를 발급하고 epoch 를 올린다(필드 드랍 장비 UID 대역).
				if (ensureDomainRow("USER_FIELD", JsonConvert.SerializeObject(new FieldSessionDto()), out string fieldJson, out string fieldErr) == false)
				{
					return FuncResult.Error(fieldErr);
				}

				FieldSessionDto field = JsonConvert.DeserializeObject<FieldSessionDto>(fieldJson);
				if (startFieldSession(field, out string sessionErr) == false)
				{
					return FuncResult.Error(sessionErr);
				}

				response.field = field;

				// 4. 신규 계정 — 최초 지급(Reward 그룹) + USER_INFO 생성을 하나의 트랜잭션으로 묶는다.
				//    USER_INFO 가 지급과 함께 생기므로, 실패하면 다음 로그인에 다시 시도되고 성공하면 다시 지급되지 않는다.
				if (isNewAccount == true)
				{
					// [임시] 테스트용 재화 충전 — 정식 전환 시 삭제. 저장은 grantStarter 트랜잭션에 함께 실린다.
					devTopUpCurrency(response.currency);

					if (grantStarter(tableName, response, out string starterErr) == false)
					{
						return FuncResult.Error(starterErr);
					}
				}

				// 랭킹 — 기존 계정은 로그인 때 점수·정보를 맞춘다(테이블 변경으로 전투력이 달라졌을 수 있다).
				// 신규 계정은 가입 경로를 늘리지 않도록 건너뛴다 — 첫 장착·강화나 랭킹 조회 때 올라간다.
				if (isNewAccount == false)
				{
					RankOps.Refresh(response.loadout, response.inventory, response.mastery, response.pet, response.costume);
				}

				// 5. 닉네임 — 없으면 Player + 8자리 숫자로 부여. 실패해도 로그인은 막지 않고 원인만 error 로 알린다(다음 로그인에 재시도).
				if (NicknameOps.EnsureNickname(out string nickname, out string nicknameErr) == false)
				{
					response.error = nicknameErr;
				}

				response.nickname = nickname;
				response.nicknameChangeCount = (isNewAccount == false) ? NicknameOps.ReadChangeCount(rows[0]) : 0;

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

		// 새 필드 세션 — 이전 세션의 미정산 처치는 버린다(클라 원장도 로그인 때 비운다).
		// 시드는 예측할 수 없어야 하므로 암호학적 난수로 만든다.
		private static bool startFieldSession(FieldSessionDto field, out string err)
		{
			FieldSessions.Renew(field);

			Param param = new Param();
			param.Add("Data", JsonConvert.SerializeObject(field));
			var updateResult = Backend.GameData.Update("USER_FIELD", new Where(), param);
			if (!updateResult.IsSuccess())
			{
				err = "USER_FIELD Update Failed: " + updateResult.GetErrorCode();
				return false;
			}

			err = null;
			return true;
		}

		// [임시] 테스트용 재화 충전 — 정식 전환 시 삭제.
		// 신규 계정 생성 시 1회, 모든 재화를 DevCurrencyAmount 까지 채운다(DTO 만 바꾼다).
		private const int DevCurrencyAmount = 10000000;

		private static void devTopUpCurrency(CurrencyDto currency)
		{

			System.Array values = System.Enum.GetValues(typeof(EDT.Currency));
			for (int i = 0; i < values.Length; i++)
			{
				int currencyId = (int)values.GetValue(i);
				if (currencyId == (int)EDT.Currency.None)
				{
					continue;
				}

				CurrencyAmountDto entry = null;
				for (int j = 0; j < currency.amounts.Count; j++)
				{
					if (currency.amounts[j] != null && currency.amounts[j].currencyId == currencyId)
					{
						entry = currency.amounts[j];
						break;
					}
				}

				if (entry == null)
				{
					entry = new CurrencyAmountDto();
					entry.currencyId = currencyId;
					currency.amounts.Add(entry);
				}

				if (entry.amount < DevCurrencyAmount)
				{
					entry.amount = DevCurrencyAmount;
				}
			}
		}

		// 최초 지급 — 응답 DTO 에 직접 반영한 뒤 그대로 저장한다(응답과 저장값이 같다).
		private static bool grantStarter(string infoTableName, GetUserDataResponse response, out string err)
		{
			List<RolledReward> rolled = new List<RolledReward>();
			RewardRoller.Roll(StarterRewardGroupId, 0, new ServerRandomSource(), rolled, null);

			RewardApplier applier = new RewardApplier(response.inventory, response.currency);
			applier.ApplyAll(rolled);

			Param infoParam = new Param();
			infoParam.Add("Exp", 0);
			Param inventoryParam = new Param();
			inventoryParam.Add("Data", JsonConvert.SerializeObject(response.inventory));
			Param currencyParam = new Param();
			currencyParam.Add("Data", JsonConvert.SerializeObject(response.currency));

			List<TransactionValue> tx = new List<TransactionValue>();
			tx.Add(TransactionValue.SetInsert(infoTableName, infoParam));
			tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), inventoryParam));
			tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), currencyParam));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				err = "Starter grant failed: " + txResult.GetErrorCode();
				return false;
			}

			err = null;
			return true;
		}

		// 빈 인벤토리 JSON
		private static string buildEmptyInventoryJson()
		{
			return JsonConvert.SerializeObject(new InventoryDto());
		}
	}
}
