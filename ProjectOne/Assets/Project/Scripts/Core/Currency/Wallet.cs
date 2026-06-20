using System.Collections.Generic;
using EDT;
using ProjectOne.Event;
using ProjectOne.Shared;

namespace ProjectOne.UserData
{
	// 재화 도메인 모델 — 보유 재화 수량을 보관/변경한다(인메모리).
	// 공유 DTO(CurrencyDto)를 받아 변환 보유하고, 저장/전송 시 ToDto() 로 역변환한다.
	// 서버 권위: 영속은 서버(Backnd 함수)가 담당하고, 클라는 변경 시 변경 알림만 발행한다.
	public sealed class Wallet
	{
		private readonly Dictionary<CurrencyInfo, int> _index = new Dictionary<CurrencyInfo, int>();

		public Wallet(CurrencyDto dto)
		{
			buildIndex(dto);
		}

		// ── 공개 API ──────────────────────────────────────────────────

		// 보유 수량 — 미존재 시 0
		public int GetAmount(CurrencyInfo type)
		{
			int value;
			if (_index.TryGetValue(type, out value) == true)
			{
				return value;
			}

			return 0;
		}

		// 수량 설정 — 이전값과 같으면 무시, 변경 시 변경 알림
		public void SetAmount(CurrencyInfo type, int amount)
		{
			int prev = GetAmount(type);
			if (prev == amount)
			{
				return;
			}

			_index[type] = amount;
			EventManager.Instance.Publish(new ResourceChangeEvent(type, prev, amount));
		}

		// 직렬화 DTO 로 변환 — 저장/전송 시 사용. None 은 제외(enum 순회로 foreach 회피).
		public CurrencyDto ToDto()
		{
			CurrencyDto dto = new CurrencyDto();
			System.Array types = System.Enum.GetValues(typeof(CurrencyInfo));
			for (int i = 0; i < types.Length; i++)
			{
				CurrencyInfo type = (CurrencyInfo)types.GetValue(i);
				if (type == CurrencyInfo.None)
				{
					continue;
				}

				int amount;
				if (_index.TryGetValue(type, out amount) == false)
				{
					continue;
				}

				CurrencyAmountDto entry = new CurrencyAmountDto();
				entry.currencyId = (int)type;
				entry.amount = amount;
				dto.amounts.Add(entry);
			}

			return dto;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void buildIndex(CurrencyDto dto)
		{
			_index.Clear();
			if (dto == null)
			{
				return;
			}

			for (int i = 0; i < dto.amounts.Count; i++)
			{
				CurrencyAmountDto entry = dto.amounts[i];
				if (entry == null || entry.currencyId == (int)CurrencyInfo.None)
				{
					continue;
				}

				_index[(CurrencyInfo)entry.currencyId] = entry.amount;
			}
		}
	}
}
