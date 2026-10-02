using System.Collections.Generic;
using System.IO;
using BackEnd;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectOne.Shared;

namespace BackendFunction
{
	// 펑션 응답 직렬화 공용 헬퍼 — 모든 핸들러가 공유.
	public static class FuncResult
	{
		// 에러 응답. StringToStream 으로 원문 그대로 반환(JsonToStream 은 문자열을 한 번 더 직렬화해 이중 인코딩됨).
		public static Stream Error(string err)
		{
			JObject error = new JObject();
			error.Add("status", "fail");
			error.Add("error", err);
			return Backend.StringToStream(error.ToString());
		}

		// 성공/일반 객체 응답. 클라는 GetReturnValueByUnmarshall() 로 이 JSON 문자열을 그대로 받는다.
		public static Stream Json(object payload)
		{
			return Backend.StringToStream(JsonConvert.SerializeObject(payload));
		}
	}

	// 차트 조회 공용 헬퍼 — 차트 "이름"으로 파일 ID 를 해석한다.
	// 차트 파일 ID 는 재업로드 시마다 바뀌므로 고정인 이름으로 조회하고, 해석 결과를 캐시한다(워밍 컨테이너 재사용, 콜드스타트 시에만 재조회).
	public static class ChartUtil
	{
		private static readonly Dictionary<string, string> _chartFileIdCache = new Dictionary<string, string>();

		// 차트 콘텐츠(행) 캐시 — 정적 차트 데이터를 워밍 컨테이너 동안 재사용해 매 호출 재다운로드를 막는다.
		// 차트 재업로드 시 워밍 컨테이너는 콜드스타트/재배포 전까지 옛 데이터를 볼 수 있다(fileId 캐시와 동일 특성).
		private static readonly Dictionary<string, JsonData> _chartRowsCache = new Dictionary<string, JsonData>();

		// 차트 이름으로 행(FlattenRows)을 반환한다. 최초 1회만 네트워크 조회하고 이후 캐시 반환.
		public static bool GetChartRows(string chartName, out JsonData rows, out string err)
		{
			if (_chartRowsCache.TryGetValue(chartName, out rows) == true)
			{
				err = null;
				return true;
			}

			if (ResolveChartFileId(chartName, out string fileId, out err) == false)
			{
				return false;
			}

			var result = Backend.Chart.GetChartContents(fileId);
			if (!result.IsSuccess())
			{
				err = "Failed to get chart " + chartName + ": " + result.GetErrorCode();
				return false;
			}

			rows = result.FlattenRows();
			_chartRowsCache[chartName] = rows;
			err = null;
			return true;
		}

		// chartName 에 해당하는 차트 파일 ID 를 반환한다. 성공 시 캐시에 저장.
		public static bool ResolveChartFileId(string chartName, out string fileId, out string err)
		{
			if (_chartFileIdCache.TryGetValue(chartName, out fileId))
			{
				err = null;
				return true;
			}

			var listBro = Backend.Chart.GetChartListV2();
			if (!listBro.IsSuccess())
			{
				fileId = null;
				err = "Failed to get chart list: " + listBro.GetErrorCode();
				return false;
			}

			JsonData rows = listBro.FlattenRows();
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i]["chartName"].ToString() == chartName)
				{
					fileId = rows[i]["selectedChartFileId"].ToString();
					_chartFileIdCache[chartName] = fileId;
					err = null;
					return true;
				}
			}

			fileId = null;
			err = "chart not found: " + chartName;
			return false;
		}
	}

	// 인벤토리 DTO 공용 조작 — 소모 아이템 차감(지식의 서·상자 열쇠 등).
	public static class InventoryUtil
	{
		// 보유 수량이 충분하면 차감하고 true. 0 이 되면 항목을 지운다.
		public static bool TrySpendItem(InventoryDto inventory, int itemId, int count)
		{
			for (int i = 0; i < inventory.items.Count; i++)
			{
				OwnedItemDto item = inventory.items[i];
				if (item != null && item.itemId == itemId)
				{
					if (item.count < count)
					{
						return false;
					}

					item.count -= count;
					if (item.count <= 0)
					{
						inventory.items.RemoveAt(i);
					}

					return true;
				}
			}

			return false;
		}
	}

	// 재화 DTO 공용 조작 — 성장 비용 차감.
	public static class CurrencyUtil
	{
		// 전부 충분할 때만 차감하고 true — 부분 차감이 남지 않도록 검사를 먼저 끝낸다.
		// 같은 재화가 두 번 나올 수 있어(승급의 ReqCurrency + ReqGoldCount) 재화별 합계로 검사한다.
		public static bool TrySpendAll(CurrencyDto currency, List<CurrencyCost> costs)
		{
			for (int i = 0; i < costs.Count; i++)
			{
				int required = 0;
				for (int j = 0; j < costs.Count; j++)
				{
					if (costs[j].currency == costs[i].currency)
					{
						required += costs[j].amount;
					}
				}

				CurrencyAmountDto owned = find(currency, (int)costs[i].currency);
				if (owned == null || owned.amount < required)
				{
					return false;
				}
			}

			for (int i = 0; i < costs.Count; i++)
			{
				find(currency, (int)costs[i].currency).amount -= costs[i].amount;
			}

			return true;
		}

		private static CurrencyAmountDto find(CurrencyDto currency, int currencyId)
		{
			for (int i = 0; i < currency.amounts.Count; i++)
			{
				CurrencyAmountDto entry = currency.amounts[i];
				if (entry != null && entry.currencyId == currencyId)
				{
					return entry;
				}
			}

			return null;
		}
	}

	// 내 도메인 행(유저당 1행) 조회 공용 헬퍼 — Data 컬럼의 DTO JSON 을 읽는다.
	public static class MyData
	{
		// 행이 없으면 실패 — GetUserData 가 로그인 시 먼저 ensure 한다.
		public static bool Load<T>(string tableName, out T data, out string err) where T : class, new()
		{
			data = null;
			var getResult = Backend.GameData.GetMyData(tableName, new Where());
			if (!getResult.IsSuccess())
			{
				err = tableName + " Get Failed: " + getResult.GetErrorCode();
				return false;
			}

			JsonData rows = getResult.FlattenRows();
			if (rows.Count == 0)
			{
				err = tableName + " row not found";
				return false;
			}

			data = JsonConvert.DeserializeObject<T>(rows[0]["Data"].ToString());
			if (data == null)
			{
				data = new T();
			}

			err = null;
			return true;
		}
	}
}
