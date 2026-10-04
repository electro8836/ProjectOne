using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BackEnd;
using BackEnd.Leaderboard;
using LitJson;
using Newtonsoft.Json;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 랭킹·프로필 갱신.
	//
	// 테이블은 둘이다.
	//   USER_RANKDATA (비공개)  랭킹 점수 컬럼. 뒤끝 리더보드가 컬럼을 가져다 쓴다(리더보드 대상 테이블은 비공개여야 한다).
	//                           지금은 BattlePower 하나 — 다른 랭킹은 컬럼과 리더보드를 추가하고 submitScore 를 부른다.
	//   USER_PROFILE  (공개)    정보 스냅샷(PlayerProfileDto). 랭킹과 무관한 정보 보기용이라 남이 읽을 수 있어야 한다.
	//
	// 전투력은 서버가 저장 데이터로 계산한다(BattlePowerRules) — 클라가 보내는 값은 없다.
	// 전투력이 바뀌는 펑션(장착·강화·마스터리·펫·레벨업)이 저장을 끝낸 뒤 Refresh 를 부른다.
	//
	// 점수는 최고 전투력이다 — 계산값이 저장값보다 높을 때만 오른다(파밍 세트로 낮아져도 유지).
	// 전 유저가 리더보드에 올라간다. 리더보드 항목은 콘솔에서만 지울 수 있어 순위권 밖을 정리하지 않는다.
	public static class RankOps
	{
		public const string ProfileTable = "USER_PROFILE";
		private const string RankTable = "USER_RANKDATA";

		// 전투력 랭킹 — 리더보드 제목과 점수 컬럼.
		public const string BattlePowerBoard = "BattlePower";
		private const string BattlePowerColumn = "BattlePower";

		// 리더보드 제목 → uuid. 콘솔에서 다시 만들면 바뀌므로 제목으로 찾아 캐시한다(워밍 컨테이너 재사용).
		private static readonly Dictionary<string, string> _leaderboardUuids = new Dictionary<string, string>();

		// 이미 읽은 DTO 는 넘기고 없는 것은 null 로 준다 — 없는 것만 서버에서 읽는다.
		// 본래 행동은 이미 저장된 뒤라, 실패해도 호출한 펑션은 성공이다. 원인만 돌려준다(성공이면 null).
		// 어긋난 값은 다음 갱신 때 다시 맞춰진다.
		public static string Refresh(LoadoutDto loadout, InventoryDto inventory, MasteryDto mastery, PetDto pet, CostumeDto costume)
		{
			try
			{
				string err;
				if (GameData.EnsureLoaded(out err) == false)
				{
					return "rank: " + err;
				}

				if (loadout == null && MyData.Load("USER_LOADOUT", out loadout, out err) == false)
				{
					return "rank: " + err;
				}

				if (inventory == null && MyData.Load("USER_INVENTORY", out inventory, out err) == false)
				{
					return "rank: " + err;
				}

				if (mastery == null && MyData.Load("USER_MASTERY", out mastery, out err) == false)
				{
					return "rank: " + err;
				}

				if (pet == null && MyData.Load("USER_PET", out pet, out err) == false)
				{
					return "rank: " + err;
				}

				if (costume == null && MyData.Load("USER_COSTUME", out costume, out err) == false)
				{
					return "rank: " + err;
				}

				int power = BattlePowerRules.Calculate(loadout, inventory, mastery, pet);

				// 점수가 먼저다 — 최고 전투력이 프로필에도 실린다. 점수 저장이 실패해도 프로필은 쓴다.
				int best;
				string scoreErr = submitScore(BattlePowerBoard, BattlePowerColumn, power, out best);
				string profileErr = saveProfile(buildProfile(power, best, loadout, inventory, mastery, costume));

				if (scoreErr != null)
				{
					return "rank: " + scoreErr;
				}

				return (profileErr != null) ? "profile: " + profileErr : null;
			}
			catch (Exception ex)
			{
				return "rank: " + ex.ToString();
			}
		}

		// 캐릭터 레벨과 마스터리 레벨 합을 한 값으로 — 경험치를 주는 펑션이 앞뒤를 비교해 레벨이 올랐을 때만 갱신한다.
		public static int LevelKey(LoadoutDto loadout, MasteryDto mastery)
		{
			return loadout.level * 100000 + BattlePowerRules.TotalMasteryLevel(mastery);
		}

		public static bool ResolveLeaderboardUuid(string title, out string uuid, out string err)
		{
			if (_leaderboardUuids.TryGetValue(title, out uuid) == true)
			{
				err = null;
				return true;
			}

			var listResult = Backend.Leaderboard.User.GetLeaderboards();
			if (!listResult.IsSuccess())
			{
				err = "Leaderboard List Failed: " + listResult.GetStatusCode() + " " + listResult.GetErrorCode() + " " + listResult.GetMessage();
				return false;
			}

			List<LeaderboardTableItem> tables = listResult.GetLeaderboardTableList();
			for (int i = 0; i < tables.Count; i++)
			{
				if (tables[i].title == title)
				{
					uuid = tables[i].uuid;
					_leaderboardUuids[title] = uuid;
					err = null;
					return true;
				}
			}

			err = "leaderboard not found: " + title;
			return false;
		}

		// 리더보드·테이블 숫자는 문자열로 오고 소수점이 붙을 수 있다.
		public static int ParseInt(string value)
		{
			double parsed;
			if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) == false)
			{
				return 0;
			}

			return (int)parsed;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 랭킹 점수 저장 — 최고 기록일 때만 올리고 리더보드에 반영한다. best 는 저장된 최고 점수(실패해도 아는 만큼 채운다).
		// 행이 없으면 만든다. 유저당 한 행이고 랭킹마다 컬럼이 하나씩이다.
		private static string submitScore(string board, string column, int score, out int best)
		{
			best = score;

			var getResult = Backend.GameData.GetMyData(RankTable, new Where());
			if (!getResult.IsSuccess())
			{
				return RankTable + " Get Failed: " + getResult.GetErrorCode();
			}

			JsonData rows = getResult.FlattenRows();
			string rowInDate = null;
			int stored = 0;
			if (rows.Count > 0)
			{
				rowInDate = rows[0]["inDate"].ToString();

				// 나중에 추가된 컬럼은 예전 행에 없을 수 있다.
				if (rows[0].ContainsKey(column) == true)
				{
					stored = ParseInt(rows[0][column].ToString());
				}
			}

			if (stored > best)
			{
				best = stored;
			}

			// 이미 올라가 있고 기록이 오르지 않았으면 쓰지 않는다.
			if (rowInDate != null && best == stored)
			{
				return null;
			}

			string uuid;
			string err;
			if (ResolveLeaderboardUuid(board, out uuid, out err) == false)
			{
				return err;
			}

			Param param = new Param();
			param.Add(column, best);

			if (rowInDate == null)
			{
				var insertResult = Backend.GameData.Insert(RankTable, param);
				if (!insertResult.IsSuccess())
				{
					return RankTable + " Insert Failed: " + insertResult.GetErrorCode();
				}

				rowInDate = insertResult.GetInDate();
			}

			// 이 호출로 갱신해야 리더보드에 반영된다(일반 Update 는 반영되지 않는다).
			var updateResult = Backend.Leaderboard.User.UpdateMyDataAndRefreshLeaderboard(uuid, RankTable, rowInDate, param);
			if (!updateResult.IsSuccess())
			{
				return "Leaderboard Update Failed: " + updateResult.GetStatusCode() + " " + updateResult.GetErrorCode() + " " + updateResult.GetMessage();
			}

			return null;
		}

		// 정보 스냅샷 저장 — 달라졌을 때만 쓴다.
		private static string saveProfile(PlayerProfileDto profile)
		{
			var getResult = Backend.GameData.GetMyData(ProfileTable, new Where());
			if (!getResult.IsSuccess())
			{
				return ProfileTable + " Get Failed: " + getResult.GetErrorCode();
			}

			string data = JsonConvert.SerializeObject(profile);
			Param param = new Param();
			param.Add("Data", data);

			JsonData rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				var insertResult = Backend.GameData.Insert(ProfileTable, param);
				return insertResult.IsSuccess() ? null : ProfileTable + " Insert Failed: " + insertResult.GetErrorCode();
			}

			if (rows[0].ContainsKey("Data") == true && rows[0]["Data"].ToString() == data)
			{
				return null;
			}

			var updateResult = Backend.GameData.Update(ProfileTable, new Where(), param);
			return updateResult.IsSuccess() ? null : ProfileTable + " Update Failed: " + updateResult.GetErrorCode();
		}

		private static PlayerProfileDto buildProfile(int battlePower, int bestBattlePower, LoadoutDto loadout, InventoryDto inventory, MasteryDto mastery, CostumeDto costume)
		{
			PlayerProfileDto profile = new PlayerProfileDto();
			profile.level = loadout.level;
			profile.masteryLevel = BattlePowerRules.TotalMasteryLevel(mastery);
			profile.battlePower = battlePower;
			profile.bestBattlePower = bestBattlePower;
			profile.weaponCostumeId = costume.equippedWeaponId;
			profile.bodyCostumeId = costume.equippedBodyId;

			for (int i = 0; i < inventory.equipments.Count; i++)
			{
				EquipmentInstanceDto equipment = inventory.equipments[i];
				if (equipment != null && equipment.equippedSlot != (int)EDT.EquipSlotTypes.None)
				{
					profile.equipped.Add(equipment);
				}
			}

			return profile;
		}
	}

	// 랭킹 조회 펑션.
	public class RankingFunctions
	{
		// 뒤끝 리더보드는 한 번에 50명까지만 읽힌다. 화면에는 100위까지만 보여 준다 — 클라가 두 번에 나눠 받는다.
		private const int PageSize = 50;
		private const int ListLimit = 100;

		// RankList — offset(0 또는 50)부터 50명. 첫 페이지에는 내 순위도 싣는다.
		public Stream RankList()
		{
			try
			{
				int offset = 0;
				if (Backend.HasKey("req") == true)
				{
					RankListRequest req = JsonConvert.DeserializeObject<RankListRequest>(Backend.Content["req"].ToString());
					if (req != null && req.offset > 0 && req.offset < ListLimit)
					{
						offset = (req.offset / PageSize) * PageSize;
					}
				}

				// 첫 페이지 — 목록을 읽기 전에 내 점수·정보를 맞춘다. 실패해도 목록은 내려주고 원인만 error 로 알린다.
				string refreshErr = null;
				if (offset == 0)
				{
					refreshErr = RankOps.Refresh(null, null, null, null, null);
				}

				if (RankOps.ResolveLeaderboardUuid(RankOps.BattlePowerBoard, out string uuid, out string uuidErr) == false)
				{
					return FuncResult.Error(uuidErr);
				}

				var topResult = Backend.Leaderboard.User.GetLeaderboard(uuid, PageSize, offset);
				if (!topResult.IsSuccess())
				{
					return FuncResult.Error("Leaderboard Get Failed: " + topResult.GetStatusCode() + " " + topResult.GetErrorCode() + " " + topResult.GetMessage());
				}

				List<UserLeaderboardItem> top = topResult.GetUserLeaderboardList();

				RankListResponse response = new RankListResponse();
				response.success = true;
				response.error = refreshErr;
				response.top = new RankEntryDto[top.Count];
				for (int i = 0; i < top.Count; i++)
				{
					response.top[i] = toEntry(top[i]);
				}

				// 내 순위 — 아직 리더보드에 없으면 실패한다. 그때는 rank 0(순위 외)으로 둔다.
				response.mine = new RankEntryDto();
				if (offset == 0)
				{
					var myResult = Backend.Leaderboard.User.GetMyLeaderboard(uuid, 0);
					if (myResult.IsSuccess() == true)
					{
						List<UserLeaderboardItem> mine = myResult.GetUserLeaderboardList();
						if (mine.Count > 0)
						{
							response.mine = toEntry(mine[0]);
						}
					}
				}

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		// RankProfile — 다른 유저의 정보 스냅샷(공개 테이블).
		public Stream RankProfile()
		{
			try
			{
				if (Backend.HasKey("req") == false)
				{
					return FuncResult.Error("req key is not exist");
				}

				RankProfileRequest req = JsonConvert.DeserializeObject<RankProfileRequest>(Backend.Content["req"].ToString());
				if (req == null || string.IsNullOrEmpty(req.inDate) == true)
				{
					return FuncResult.Error("req parse failed");
				}

				Where where = new Where();
				where.Equal("owner_inDate", req.inDate);
				var getResult = Backend.GameData.Get(RankOps.ProfileTable, where, 1);
				if (!getResult.IsSuccess())
				{
					return FuncResult.Error(RankOps.ProfileTable + " Get Failed: " + getResult.GetStatusCode() + " " + getResult.GetErrorCode());
				}

				JsonData rows = getResult.FlattenRows();
				if (rows.Count == 0)
				{
					return FuncResult.Error("profile not found: " + req.inDate);
				}

				RankProfileResponse response = new RankProfileResponse();
				response.success = true;
				response.profile = JsonConvert.DeserializeObject<PlayerProfileDto>(rows[0]["Data"].ToString());
				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}

		private static RankEntryDto toEntry(UserLeaderboardItem item)
		{
			RankEntryDto entry = new RankEntryDto();
			entry.inDate = item.gamerInDate;
			entry.nickname = item.nickname;
			entry.rank = RankOps.ParseInt(item.rank);
			entry.battlePower = RankOps.ParseInt(item.score);
			return entry;
		}
	}
}
