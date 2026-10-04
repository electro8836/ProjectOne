using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Ranking
{
	// 랭킹 데이터 출처. 구현체는 뒤끝 리더보드(BackndRankingProvider)다.
	public interface IRankingProvider
	{
		// 첫 목록(최대 50위)과 내 순위.
		UniTask<RankingResult> GetRankingAsync(CancellationToken ct);

		// 아직 받지 않은 다음 줄이 있는가(100위까지).
		bool HasMore { get; }

		// 다음 줄을 받아 GetRankingAsync 가 준 결과의 top 뒤에 붙인다. 붙였으면 true.
		UniTask<bool> LoadMoreAsync(CancellationToken ct);

		UniTask<PlayerProfile> GetProfileAsync(string playerId, CancellationToken ct);
	}
}
