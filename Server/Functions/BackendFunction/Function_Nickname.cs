using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 닉네임 — 최초 로그인 때 뒤끝 계정 닉네임을 자동으로 부여한다. 형식 Player + 8자리 숫자(예: Player48291034).
	//
	// 값은 계정 gamerId(UUID)를 섞어 1억 안으로 줄인 것이라 계정끼리 겹칠 수 있다.
	// 뒤끝이 중복 닉네임을 거부(409)하므로, 겹치면 시도 번호를 섞은 다음 값으로 다시 만든다 — 중복은 생기지 않는다.
	// 펑션 컨텍스트에서는 Backend.UID 가 비어 있어 gamerId 를 쓴다. 키 값은 서버에만 둔다.
	public static class NicknameOps
	{
		private const string Prefix = "Player";
		private const ulong Range = 100000000UL;	// 8자리

		private const ulong XorKey = 0x5DEECE66DA3B1F27UL;
		private const ulong Multiplier = 0x9E3779B97F4A7C15UL;
		private const ulong AttemptStep = 0x2545F4914F6CDD1DUL;
		private const int MaxAttempts = 10;

		public const string InfoTable = "USER_INFO";
		public const string ChangeCountColumn = "NicknameChangeCount";

		// 닉네임이 있으면 그대로, 없으면 만든다. 실패하면 nickname 은 빈 값이고 err 에 원인이 남는다(로그인은 막지 않는다).
		public static bool EnsureNickname(out string nickname, out string err)
		{
			nickname = string.Empty;

			var infoResult = Backend.BMember.GetUserInfoV2();
			if (!infoResult.IsSuccess())
			{
				err = "GetUserInfoV2 Failed: " + infoResult.GetStatusCode() + " " + infoResult.GetErrorCode();
				return false;
			}

			string raw = infoResult.GetReturnValue();
			JObject root = parseRaw(raw);
			JToken info = (root != null && root["row"] != null) ? root["row"] : root;

			string current = readString(info, "nickname");
			if (string.IsNullOrEmpty(current) == false)
			{
				nickname = current;
				err = null;
				return true;
			}

			ulong seed;
			if (TryGetSeed(readString(info, "gamerId"), out seed) == false)
			{
				err = "gamerId not found: " + raw;
				return false;
			}

			for (int attempt = 0; attempt < MaxAttempts; attempt++)
			{
				string candidate = MakeNickname(seed, attempt);
				var createResult = Backend.BMember.CreateNickname(candidate);
				if (createResult.IsSuccess() == true)
				{
					nickname = candidate;
					err = null;
					return true;
				}

				// 중복이면 다음 값으로 — 그 밖의 실패는 다음 로그인에 다시 시도한다.
				if (createResult.GetStatusCode() != "409")
				{
					err = "CreateNickname Failed: " + candidate + " " + createResult.GetStatusCode() + " " + createResult.GetErrorCode();
					return false;
				}
			}

			err = "CreateNickname duplicated " + MaxAttempts + " times: seed " + seed;
			return false;
		}

		// attempt 0 이 기본 값이고, 중복일 때마다 attempt 를 올려 다른 값을 만든다.
		public static string MakeNickname(ulong seed, int attempt)
		{
			ulong mixed = unchecked((seed ^ XorKey) * Multiplier + (ulong)attempt * AttemptStep);
			ulong n = mixed % Range;
			return Prefix + n.ToString("D8", CultureInfo.InvariantCulture);
		}

		// gamerId(UUID 32자리 16진수)를 앞뒤 64비트로 나눠 XOR 로 접는다.
		public static bool TryGetSeed(string gamerId, out ulong seed)
		{
			seed = 0;
			if (string.IsNullOrEmpty(gamerId) == true)
			{
				return false;
			}

			string hex = gamerId.Replace("-", string.Empty);
			if (hex.Length != 32)
			{
				return false;
			}

			ulong hi;
			ulong lo;
			if (ulong.TryParse(hex.Substring(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hi) == false
				|| ulong.TryParse(hex.Substring(16, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lo) == false)
			{
				return false;
			}

			seed = hi ^ lo;
			return true;
		}

		// USER_INFO 행에서 닉네임 변경 횟수를 읽는다. 컬럼이 아직 없으면(한 번도 안 바꿈) 0.
		public static int ReadChangeCount(JsonData infoRow)
		{
			if (infoRow == null || ((IDictionary)infoRow).Contains(ChangeCountColumn) == false)
			{
				return 0;
			}

			int count;
			if (int.TryParse(infoRow[ChangeCountColumn].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) == false)
			{
				return 0;
			}

			return count;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static JObject parseRaw(string raw)
		{
			if (string.IsNullOrEmpty(raw) == true)
			{
				return null;
			}

			JsonSerializerSettings settings = new JsonSerializerSettings();
			settings.DateParseHandling = DateParseHandling.None;
			return JsonConvert.DeserializeObject<JObject>(raw, settings);
		}

		private static string readString(JToken token, string key)
		{
			if (token == null || token.Type != JTokenType.Object)
			{
				return null;
			}

			JToken value = token[key];
			if (value == null || value.Type == JTokenType.Null)
			{
				return null;
			}

			return value.ToString();
		}
	}

	// 닉네임 변경 — 규칙(NicknameRules)·중복·비용을 서버가 판정한다. 첫 변경은 무료, 그 뒤로는 다이아 고정 비용.
	//
	// 계정 닉네임 변경과 게임 데이터 저장은 한 트랜잭션으로 묶을 수 없다.
	// 닉네임을 먼저 바꾸고 차감을 나중에 한다 — 저장이 실패하면 무료로 바꾼 셈이 되지만, 재화만 빠지는 것보다 낫다.
	public class NicknameFunctions
	{
		public Stream ChangeNickname()
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

				ChangeNicknameRequest req = JsonConvert.DeserializeObject<ChangeNicknameRequest>(Backend.Content["req"].ToString());
				if (req == null)
				{
					return FuncResult.Error("req parse failed");
				}

				NicknameError nicknameError = NicknameRules.Validate(req.nickname);
				if (nicknameError != NicknameError.None)
				{
					return FuncResult.Error(NicknameRules.ErrorInvalid + ": " + nicknameError);
				}

				if (NicknameOps.EnsureNickname(out string current, out string currentErr) == false)
				{
					return FuncResult.Error(currentErr);
				}

				if (current == req.nickname)
				{
					return FuncResult.Error(NicknameRules.ErrorSame);
				}

				var infoResult = Backend.GameData.GetMyData(NicknameOps.InfoTable, new Where());
				if (!infoResult.IsSuccess())
				{
					return FuncResult.Error(NicknameOps.InfoTable + " Get Failed: " + infoResult.GetErrorCode());
				}

				JsonData infoRows = infoResult.FlattenRows();
				if (infoRows.Count == 0)
				{
					return FuncResult.Error(NicknameOps.InfoTable + " row not found");
				}

				int changeCount = NicknameOps.ReadChangeCount(infoRows[0]);
				int costAmount = NicknameRules.GetCost(changeCount);

				// 유료면 닉네임을 바꾸기 전에 잔액부터 확인한다.
				CurrencyDto currency = null;
				if (costAmount > 0)
				{
					if (MyData.Load("USER_CURRENCY", out currency, out string curErr) == false)
					{
						return FuncResult.Error(curErr);
					}

					CurrencyCost cost = new CurrencyCost();
					cost.currency = NicknameRules.CostCurrency;
					cost.amount = costAmount;
					List<CurrencyCost> costs = new List<CurrencyCost>();
					costs.Add(cost);

					if (CurrencyUtil.TrySpendAll(currency, costs) == false)
					{
						return FuncResult.Error(NicknameRules.ErrorNotEnoughCurrency);
					}
				}

				var updateResult = Backend.BMember.UpdateNickname(req.nickname);
				if (!updateResult.IsSuccess())
				{
					if (updateResult.GetStatusCode() == "409")
					{
						return FuncResult.Error(NicknameRules.ErrorDuplicated);
					}

					return FuncResult.Error("UpdateNickname Failed: " + updateResult.GetStatusCode() + " " + updateResult.GetErrorCode());
				}

				changeCount++;

				Param infoParam = new Param();
				infoParam.Add(NicknameOps.ChangeCountColumn, changeCount);

				List<TransactionValue> tx = new List<TransactionValue>();
				tx.Add(TransactionValue.SetUpdate(NicknameOps.InfoTable, new Where(), infoParam));
				if (currency != null)
				{
					Param currencyParam = new Param();
					currencyParam.Add("Data", JsonConvert.SerializeObject(currency));
					tx.Add(TransactionValue.SetUpdate("USER_CURRENCY", new Where(), currencyParam));
				}

				var txResult = Backend.GameData.TransactionWriteV2(tx);
				if (!txResult.IsSuccess())
				{
					return FuncResult.Error("Nickname changed but save failed: " + txResult.GetErrorCode());
				}

				ChangeNicknameResponse response = new ChangeNicknameResponse();
				response.success = true;
				response.nickname = req.nickname;
				response.changeCount = changeCount;
				if (costAmount > 0)
				{
					response.spent = new CurrencyAmountDto();
					response.spent.currencyId = (int)NicknameRules.CostCurrency;
					response.spent.amount = costAmount;
				}

				return FuncResult.Json(response);
			}
			catch (Exception ex)
			{
				return FuncResult.Error("Server Error: " + ex.ToString());
			}
		}
	}
}
