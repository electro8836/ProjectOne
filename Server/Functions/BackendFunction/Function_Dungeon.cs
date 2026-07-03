using System;
using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 던전 클리어 — 서버 권위로 (1) 경험치 가산, (2) 요청 보상 검증, (3) 상자 확률 롤 + 지급을 처리한다.
	// 추첨 흐름은 Function_Gacha 와 동일 패턴(등급 가중 추첨 → 등급 풀 균등 추첨 + TransactionWriteV2).
	//
	// 필요한 뒤끝 차트(콘솔 차트 이름/컬럼 — 실제 업로드 차트와 일치시킬 것):
	//   Dungeon         : DungeonID, Exp, ExtraExp
	//   DungeonReward    : DungeonID, StageRollRewardIDs(";" 구분), StageFixRewardIDs(";" 구분)
	//   RewardItem       : RewardItemID, RewardType("Equipment"/"Material"/"Skill"/"Currency"), RewardSourceID
	//   Reward_Equipment : EquipmentSourceID, Grade, Weight
	//   Reward_Material  : MaterialSourceID, MaterialGrade, Weight
	//   Reward_CardSkill : CardSkillSourceID, CardGrade, Weight
	//   Reward_Currency  : CurrencySourceID, CurrencyType, MinCount, MaxCount
	//   Equipment        : ItemID, Grade                (실제 장비 풀 — 가챠와 공용)
	//   Material         : MaterialID, MaterialGrade     (실제 재료 풀)
	//   CardSkill        : CardSkillID, CardGrade        (실제 카드스킬 풀)
	//
	// 지속: 경험치(USER_CHARACTER) + 장비/재료(USER_INVENTORY) + 카드스킬(USER_CARDSKILL) + 재화(USER_CURRENCY)
	//       를 하나의 트랜잭션으로 원자 지급한다.
	public class Dungeon
	{
		// RewardTypes(edt_enums) 정수 — 응답 rewardType 및 RewardItem 차트 RewardType 문자열 매핑.
		private const int RewardTypeEquipment = 1;
		private const int RewardTypeMaterial = 2;
		private const int RewardTypeSkill = 3;
		private const int RewardTypeCurrency = 4;

		// 호출마다 새 인스턴스 → 워밍 컨테이너 동일 시드 위험 회피(정적 캐시 금지).
		private readonly Random _rng = new Random();

		public Stream DungeonClear()
		{
			try
			{
				// 1. 요청 파싱
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				DungeonClearRequest req = JsonConvert.DeserializeObject<DungeonClearRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				// 2. 실패(사망→귀환 등) — 지급 없이 로그만. 성공 응답에 빈 결과.
				if (req.cleared == false)
				{
					DungeonClearResponse failLog = new DungeonClearResponse();
					failLog.success = true;
					failLog.exp = 0;
					failLog.rewards = new GrantedRewardDto[0];
					return FuncResult.Json(failLog);
				}

				// 3. 경험치 결정(Dungeon 차트, 서버 권위)
				if (resolveClearExp(req.dungeonId, req.extraCleared, out int rewardExp, out string expErr) == false)
				{
					return FuncResult.Error(expErr);
				}

				// 4. 보상 검증(DungeonReward 차트) — 요청 상자가 해당 던전에서 획득 가능한 것들인지.
				if (validateRewardIds(req.dungeonId, req.boxes, out string validateErr) == false)
				{
					return FuncResult.Error(validateErr);
				}

				// 5. 상자 롤 — 타입별 등급 가중 추첨 후 실제 아이템/재화 확정.
				List<GrantedRewardDto> granted = new List<GrantedRewardDto>();
				if (rollBoxes(req.boxes, granted, out string rollErr) == false)
				{
					return FuncResult.Error(rollErr);
				}

				// 6. 원자 지급(경험치 + 장비/재료/카드스킬/재화 + 던전 클리어 기록).
				if (grantRewards(req.characterId, req.dungeonId, rewardExp, granted, out int newExp, out string grantErr) == false)
				{
					return FuncResult.Error(grantErr);
				}

				// 7. 응답
				DungeonClearResponse response = new DungeonClearResponse();
				response.success = true;
				response.exp = newExp;
				response.rewards = granted.ToArray();
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// ── 경험치 ────────────────────────────────────────────────────

		// Dungeon 차트에서 dungeonId 의 Exp(+추가 클리어 시 ExtraExp)를 합산한다.
		private bool resolveClearExp(int dungeonId, bool extraCleared, out int rewardExp, out string err)
		{
			rewardExp = 0;
			if (loadChartRows("Dungeon", out JsonData rows, out err) == false)
			{
				return false;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				if (int.Parse(rows[i]["DungeonID"].ToString()) == dungeonId)
				{
					rewardExp = int.Parse(rows[i]["Exp"].ToString());
					if (extraCleared == true)
					{
						rewardExp += int.Parse(rows[i]["ExtraExp"].ToString());
					}

					return true;
				}
			}

			err = "invalid dungeonId: " + dungeonId;
			return false;
		}

		// ── 보상 검증 ──────────────────────────────────────────────────

		// DungeonReward 차트에서 해당 던전의 모든 스테이지 행을 순회해 허용 RewardID 집합을 구성하고,
		// 요청 상자의 모든 rewardItemId 가 집합에 있는지 검증한다(하나라도 없으면 실패).
		private bool validateRewardIds(int dungeonId, RewardBoxDto[] boxes, out string err)
		{
			err = null;
			if (boxes == null || boxes.Length == 0)
			{
				return true;
			}

			if (loadChartRows("DungeonReward", out JsonData rows, out err) == false)
			{
				return false;
			}

			HashSet<int> allowed = new HashSet<int>();
			for (int i = 0; i < rows.Count; i++)
			{
				if (int.Parse(rows[i]["DungeonID"].ToString()) != dungeonId)
				{
					continue;
				}

				collectSemicolonIds(rows[i]["StageRollRewardIDs"].ToString(), allowed);
				collectSemicolonIds(rows[i]["StageFixRewardIDs"].ToString(), allowed);
			}

			for (int i = 0; i < boxes.Length; i++)
			{
				if (boxes[i] == null || allowed.Contains(boxes[i].rewardItemId) == false)
				{
					err = "invalid reward request";
					return false;
				}
			}

			return true;
		}

		// ";" 구분 정수 목록을 파싱해 집합에 추가한다(빈 토큰 무시).
		private static void collectSemicolonIds(string raw, HashSet<int> target)
		{
			if (string.IsNullOrEmpty(raw) == true)
			{
				return;
			}

			string[] tokens = raw.Split(';');
			for (int i = 0; i < tokens.Length; i++)
			{
				string token = tokens[i].Trim();
				if (token.Length == 0)
				{
					continue;
				}

				if (int.TryParse(token, out int id) == true)
				{
					target.Add(id);
				}
			}
		}

		// ── 상자 롤 ────────────────────────────────────────────────────

		// 각 상자를 RewardItem 차트로 타입 조회 후 count 회 추첨한다.
		private bool rollBoxes(RewardBoxDto[] boxes, List<GrantedRewardDto> granted, out string err)
		{
			err = null;
			if (boxes == null || boxes.Length == 0)
			{
				return true;
			}

			if (buildRewardItemMap(out Dictionary<int, RewardItemInfo> rewardItems, out err) == false)
			{
				return false;
			}

			for (int b = 0; b < boxes.Length; b++)
			{
				RewardBoxDto box = boxes[b];
				if (box == null || box.count <= 0)
				{
					continue;
				}

				if (rewardItems.TryGetValue(box.rewardItemId, out RewardItemInfo info) == false)
				{
					err = "unknown rewardItemId: " + box.rewardItemId;
					return false;
				}

				for (int n = 0; n < box.count; n++)
				{
					if (rollOne(info, box.isBonus, granted, out err) == false)
					{
						return false;
					}
				}
			}

			return true;
		}

		// 상자 1개를 타입별로 추첨해 granted 에 1건 추가한다.
		private bool rollOne(RewardItemInfo info, bool isBonus, List<GrantedRewardDto> granted, out string err)
		{
			switch (info.rewardType)
			{
			case RewardTypeEquipment:
				return rollGradeItem(info.sourceId, "Reward_Equipment", "EquipmentSourceID", "Grade",
					"Equipment", "ItemID", "Grade", RewardTypeEquipment, isBonus, granted, out err);
			case RewardTypeMaterial:
				return rollGradeItem(info.sourceId, "Reward_Material", "MaterialSourceID", "MaterialGrade",
					"Material", "MaterialID", "MaterialGrade", RewardTypeMaterial, isBonus, granted, out err);
			case RewardTypeSkill:
				return rollGradeItem(info.sourceId, "Reward_CardSkill", "CardSkillSourceID", "CardGrade",
					"CardSkill", "CardSkillID", "CardGrade", RewardTypeSkill, isBonus, granted, out err);
			case RewardTypeCurrency:
				return rollCurrency(info.sourceId, isBonus, granted, out err);
			default:
				err = "unsupported rewardType: " + info.rewardType;
				return false;
			}
		}

		// 등급 가중 추첨(Reward_* 그룹) → 등급 풀(실제 아이템 차트) 균등 추첨.
		private bool rollGradeItem(int sourceId, string rewardChart, string sourceCol, string rewardGradeCol,
			string itemChart, string itemIdCol, string itemGradeCol, int rewardType, bool isBonus,
			List<GrantedRewardDto> granted, out string err)
		{
			// 1. Reward_* 그룹에서 (등급, 가중치) 수집
			if (loadChartRows(rewardChart, out JsonData rewardRows, out err) == false)
			{
				return false;
			}

			List<string> grades = new List<string>();
			List<int> weights = new List<int>();
			int weightSum = 0;
			for (int i = 0; i < rewardRows.Count; i++)
			{
				if (int.Parse(rewardRows[i][sourceCol].ToString()) != sourceId)
				{
					continue;
				}

				grades.Add(rewardRows[i][rewardGradeCol].ToString());
				int weight = int.Parse(rewardRows[i]["Weight"].ToString());
				weights.Add(weight);
				weightSum += weight;
			}

			if (weightSum <= 0)
			{
				err = rewardChart + " group empty or zero weight for sourceId: " + sourceId;
				return false;
			}

			// 2. 등급 가중 추첨
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

			// 3. 실제 아이템 풀(등급별) 균등 추첨
			if (buildPoolByGrade(itemChart, itemIdCol, itemGradeCol, out Dictionary<string, List<int>> pool, out err) == false)
			{
				return false;
			}

			if (pool.TryGetValue(pickedGrade, out List<int> gradePool) == false || gradePool.Count == 0)
			{
				err = itemChart + " has no item for grade: " + pickedGrade;
				return false;
			}

			int itemId = gradePool[_rng.Next(0, gradePool.Count)];
			granted.Add(makeGranted(rewardType, itemId, 1, isBonus));
			return true;
		}

		// 재화 — Reward_Currency 그룹에서 uniform 추출 후 Min~Max 수량 롤.
		private bool rollCurrency(int sourceId, bool isBonus, List<GrantedRewardDto> granted, out string err)
		{
			if (loadChartRows("Reward_Currency", out JsonData rows, out err) == false)
			{
				return false;
			}

			List<int> currencyIds = new List<int>();
			List<int> minCounts = new List<int>();
			List<int> maxCounts = new List<int>();
			for (int i = 0; i < rows.Count; i++)
			{
				if (int.Parse(rows[i]["CurrencySourceID"].ToString()) != sourceId)
				{
					continue;
				}

				int currencyId = currencyNameToId(rows[i]["CurrencyType"].ToString());
				if (currencyId == 0)
				{
					err = "invalid CurrencyType in Reward_Currency: " + rows[i]["CurrencyType"].ToString();
					return false;
				}

				currencyIds.Add(currencyId);
				minCounts.Add(int.Parse(rows[i]["MinCount"].ToString()));
				maxCounts.Add(int.Parse(rows[i]["MaxCount"].ToString()));
			}

			if (currencyIds.Count == 0)
			{
				err = "Reward_Currency group empty for sourceId: " + sourceId;
				return false;
			}

			int pick = _rng.Next(0, currencyIds.Count);
			int amount = _rng.Next(minCounts[pick], maxCounts[pick] + 1);
			granted.Add(makeGranted(RewardTypeCurrency, currencyIds[pick], amount, isBonus));
			return true;
		}

		private static GrantedRewardDto makeGranted(int rewardType, int itemId, int count, bool isBonus)
		{
			GrantedRewardDto dto = new GrantedRewardDto();
			dto.rewardType = rewardType;
			dto.itemId = itemId;
			dto.count = count;
			dto.isBonus = isBonus;
			return dto;
		}

		// ── 지급(트랜잭션) ─────────────────────────────────────────────

		// 경험치(USER_CHARACTER) + 장비/재료(USER_INVENTORY) + 카드스킬(USER_CARDSKILL) + 재화(USER_CURRENCY)
		// + 던전 클리어 기록(USER_DUNGEON) 을 하나의 트랜잭션으로 원자 지급한다.
		private bool grantRewards(int characterId, int dungeonId, int rewardExp, List<GrantedRewardDto> granted, out int newExp, out string err)
		{
			newExp = 0;

			// 1. USER_CHARACTER — 전투 캐릭터 exp 가산
			if (loadMyData("USER_CHARACTER", out JsonData charRows, out err) == false)
			{
				return false;
			}

			if (charRows.Count == 0)
			{
				err = "USER_CHARACTER row not found";
				return false;
			}

			CharacterDto character = JsonConvert.DeserializeObject<CharacterDto>(charRows[0]["Data"].ToString());
			if (character == null)
			{
				err = "USER_CHARACTER parse failed";
				return false;
			}

			OwnedCharacterDto target = findCharacter(character, characterId);
			if (target == null)
			{
				err = "character not owned: " + characterId;
				return false;
			}

			target.exp += rewardExp;
			newExp = target.exp;

			// 획득 타입 판별 — 해당 테이블이 필요할 때만 로드/기록해 불필요한 왕복을 없앤다.
			bool needInventory = false;
			bool needCurrency = false;
			bool needCardSkill = false;
			for (int i = 0; i < granted.Count; i++)
			{
				switch (granted[i].rewardType)
				{
				case RewardTypeEquipment:
				case RewardTypeMaterial:
					needInventory = true;
					break;
				case RewardTypeSkill:
					needCardSkill = true;
					break;
				case RewardTypeCurrency:
					needCurrency = true;
					break;
				}
			}

			// 2. USER_INVENTORY — 장비/재료 가산(있을 때만)
			InventoryDto inventory = null;
			if (needInventory == true)
			{
				if (loadMyData("USER_INVENTORY", out JsonData invRows, out err) == false)
				{
					return false;
				}

				inventory = invRows.Count > 0
					? JsonConvert.DeserializeObject<InventoryDto>(invRows[0]["Data"].ToString())
					: new InventoryDto();
				if (inventory == null)
				{
					inventory = new InventoryDto();
				}
			}

			// 3. USER_CURRENCY — 재화 가산(있을 때만)
			CurrencyDto currency = null;
			if (needCurrency == true)
			{
				if (loadMyData("USER_CURRENCY", out JsonData curRows, out err) == false)
				{
					return false;
				}

				currency = curRows.Count > 0
					? JsonConvert.DeserializeObject<CurrencyDto>(curRows[0]["Data"].ToString())
					: new CurrencyDto();
				if (currency == null)
				{
					currency = new CurrencyDto();
				}
			}

			// 4. USER_CARDSKILL — 카드스킬 가산(있을 때만)
			CardSkillBookDto cardSkillBook = null;
			if (needCardSkill == true)
			{
				if (loadMyData("USER_CARDSKILL", out JsonData cardRows, out err) == false)
				{
					return false;
				}

				cardSkillBook = cardRows.Count > 0
					? JsonConvert.DeserializeObject<CardSkillBookDto>(cardRows[0]["Data"].ToString())
					: new CardSkillBookDto();
				if (cardSkillBook == null)
				{
					cardSkillBook = new CardSkillBookDto();
				}
			}

			// 5. USER_DUNGEON — 클리어 기록 추가(항상, 중복 방지)
			if (loadMyData("USER_DUNGEON", out JsonData dungeonRows, out err) == false)
			{
				return false;
			}

			ClearedDungeonsDto clearedDungeons = dungeonRows.Count > 0
				? JsonConvert.DeserializeObject<ClearedDungeonsDto>(dungeonRows[0]["Data"].ToString())
				: new ClearedDungeonsDto();
			if (clearedDungeons == null)
			{
				clearedDungeons = new ClearedDungeonsDto();
			}

			if (clearedDungeons.dungeonIds.Contains(dungeonId) == false)
			{
				clearedDungeons.dungeonIds.Add(dungeonId);
			}

			for (int i = 0; i < granted.Count; i++)
			{
				GrantedRewardDto g = granted[i];
				switch (g.rewardType)
				{
				case RewardTypeEquipment:
				case RewardTypeMaterial:
					addToInventory(inventory, g.itemId, g.count);
					break;
				case RewardTypeSkill:
					addToCardSkill(cardSkillBook, g.itemId, g.count);
					break;
				case RewardTypeCurrency:
					addToCurrency(currency, g.itemId, g.count);
					break;
				}
			}

			// 6. 트랜잭션 원자 쓰기 — 항상 캐릭터+던전, 획득 타입이 있는 테이블만 추가. 함수 컨텍스트라 new Where().
			List<TransactionValue> tx = new List<TransactionValue>();

			Param charParam = new Param();
			charParam.Add("Data", JsonConvert.SerializeObject(character));
			tx.Add(TransactionValue.SetUpdate("USER_CHARACTER", new Where(), charParam));

			if (needInventory == true)
			{
				Param invParam = new Param();
				invParam.Add("Data", JsonConvert.SerializeObject(inventory));
				tx.Add(TransactionValue.SetUpdate("USER_INVENTORY", new Where(), invParam));
			}

			if (needCurrency == true)
			{
				Param curParam = new Param();
				curParam.Add("Data", JsonConvert.SerializeObject(currency));
				tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), curParam));
			}

			if (needCardSkill == true)
			{
				Param cardParam = new Param();
				cardParam.Add("Data", JsonConvert.SerializeObject(cardSkillBook));
				tx.Add(TransactionValue.SetUpdate("USER_CARDSKILL", new Where(), cardParam));
			}

			Param dungeonParam = new Param();
			dungeonParam.Add("Data", JsonConvert.SerializeObject(clearedDungeons));
			tx.Add(TransactionValue.SetUpdate("USER_DUNGEON", new Where(), dungeonParam));

			var txResult = Backend.GameData.TransactionWriteV2(tx);
			if (!txResult.IsSuccess())
			{
				err = "Transaction failed: " + txResult.GetErrorCode();
				return false;
			}

			return true;
		}

		private static void addToInventory(InventoryDto inventory, int itemId, int count)
		{
			for (int i = 0; i < inventory.items.Count; i++)
			{
				if (inventory.items[i].itemId == itemId)
				{
					inventory.items[i].count += count;
					return;
				}
			}

			OwnedItemDto item = new OwnedItemDto();
			item.itemId = itemId;
			item.count = count;
			item.enhanceLevel = 0;
			inventory.items.Add(item);
		}

		private static void addToCurrency(CurrencyDto currency, int currencyId, int amount)
		{
			for (int i = 0; i < currency.amounts.Count; i++)
			{
				if (currency.amounts[i].currencyId == currencyId)
				{
					currency.amounts[i].amount += amount;
					return;
				}
			}

			CurrencyAmountDto entry = new CurrencyAmountDto();
			entry.currencyId = currencyId;
			entry.amount = amount;
			currency.amounts.Add(entry);
		}

		// 신규 카드스킬은 enchantLevel=1(클라 CardSkillBook.DefaultEnchantLevel)로 추가, 보유 중이면 개수만 가산.
		private static void addToCardSkill(CardSkillBookDto book, int cardSkillId, int count)
		{
			for (int i = 0; i < book.cardSkills.Count; i++)
			{
				if (book.cardSkills[i].cardSkillId == cardSkillId)
				{
					book.cardSkills[i].ownedCount += count;
					return;
				}
			}

			OwnedCardSkillDto entry = new OwnedCardSkillDto();
			entry.cardSkillId = cardSkillId;
			entry.ownedCount = count;
			entry.enchantLevel = 1;
			book.cardSkills.Add(entry);
		}

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

		// ── 차트/데이터 로드 유틸 ──────────────────────────────────────

		// RewardItem 차트 → rewardItemId 별 (타입, 소스ID) 맵.
		private bool buildRewardItemMap(out Dictionary<int, RewardItemInfo> map, out string err)
		{
			map = new Dictionary<int, RewardItemInfo>();
			if (loadChartRows("RewardItem", out JsonData rows, out err) == false)
			{
				return false;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				int id = int.Parse(rows[i]["RewardItemID"].ToString());
				RewardItemInfo info = new RewardItemInfo();
				info.rewardType = rewardTypeNameToId(rows[i]["RewardType"].ToString());
				info.sourceId = int.Parse(rows[i]["RewardSourceID"].ToString());
				map[id] = info;
			}

			return true;
		}

		// 실제 아이템 차트 → 등급 문자열별 아이템ID 풀.
		private bool buildPoolByGrade(string chartName, string idCol, string gradeCol,
			out Dictionary<string, List<int>> pool, out string err)
		{
			pool = new Dictionary<string, List<int>>();
			if (loadChartRows(chartName, out JsonData rows, out err) == false)
			{
				return false;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				string grade = rows[i][gradeCol].ToString();
				int id = int.Parse(rows[i][idCol].ToString());
				if (pool.TryGetValue(grade, out List<int> list) == false)
				{
					list = new List<int>();
					pool[grade] = list;
				}

				list.Add(id);
			}

			return true;
		}

		// 차트 이름으로 행 조회 — ChartUtil 이 콘텐츠까지 캐시(워밍 컨테이너 재사용).
		private static bool loadChartRows(string chartName, out JsonData rows, out string err)
		{
			return ChartUtil.GetChartRows(chartName, out rows, out err);
		}

		// 함수 컨텍스트에서 내 도메인 행 조회.
		private static bool loadMyData(string tableName, out JsonData rows, out string err)
		{
			rows = null;
			var result = Backend.GameData.GetMyData(tableName, new Where());
			if (!result.IsSuccess())
			{
				err = "Failed to get " + tableName + ": " + result.GetErrorCode();
				return false;
			}

			rows = result.FlattenRows();
			err = null;
			return true;
		}

		// ── enum 이름 → 정수 매핑 (EDT 는 서버 미컴파일 → 직접 정의. edt_enums.cs 변경 시 갱신) ──

		// RewardTypes(edt_enums): None,Equipment,Material,Skill,Currency.
		private static int rewardTypeNameToId(string name)
		{
			switch (name)
			{
				case "Equipment": return RewardTypeEquipment;
				case "Material": return RewardTypeMaterial;
				case "Skill": return RewardTypeSkill;
				case "Currency": return RewardTypeCurrency;
				default: return 0; // None
			}
		}

		// CurrencyInfo(edt_enums): Gold=1, Dia=2, ... (Function_Gacha 와 동일).
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

		// RewardItem 차트 1행 요약.
		private struct RewardItemInfo
		{
			public int rewardType;
			public int sourceId;
		}
	}
}
