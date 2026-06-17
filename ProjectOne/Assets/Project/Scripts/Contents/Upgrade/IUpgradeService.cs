using Cysharp.Threading.Tasks;

namespace ProjectOne.UserData
{
	// 강화/제작 실행 결과 코드 — 추후 서버 응답 코드와 동일한 형태로 클라가 수신한다.
	public enum UpgradeResult
	{
		Success,            // 성공
		Fail,               // 확률 실패(강화 전용) — 재화만 소모, 레벨 유지
		MaxLevel,           // 이미 최대 강화레벨
		NotEnoughCurrency,  // 재화 부족
		NotEnoughMaterial,  // 재료 부족
		InvalidTarget,      // 대상/테이블 없음 또는 미보유
	}

	// 강화 실행 응답
	public readonly struct EnchantResponse
	{
		public readonly UpgradeResult Result;
		public readonly int ItemId;
		public readonly int Level;   // 처리 후 강화레벨

		public EnchantResponse(UpgradeResult result, int itemId, int level)
		{
			Result = result;
			ItemId = itemId;
			Level = level;
		}

		public bool IsSuccess
		{
			get { return Result == UpgradeResult.Success; }
		}
	}

	// 제작 실행 응답
	public readonly struct CraftResponse
	{
		public readonly UpgradeResult Result;
		public readonly int ResultItemId;

		public CraftResponse(UpgradeResult result, int resultItemId)
		{
			Result = result;
			ResultItemId = resultItemId;
		}

		public bool IsSuccess
		{
			get { return Result == UpgradeResult.Success; }
		}
	}

	// 강화/제작 실행 추상화 — 차감·확률·획득 처리와 응답 반환을 담당.
	// 지금은 로컬(클라 확률) 구현, 이후 서버 구현으로 UpgradeSystem.SetService 만 교체한다.
	// 표시용 조회(가능 여부/비용/확률)는 UpgradeSystem 의 정적 메서드로, 서버 전환 후에도 클라가 계산한다.
	public interface IUpgradeService
	{
		UniTask<EnchantResponse> RequestEnchant(int itemId);
		UniTask<CraftResponse> RequestCraft(int craftId);
	}
}
