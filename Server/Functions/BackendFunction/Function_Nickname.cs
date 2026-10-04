using System.Globalization;
using BackEnd;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
}
