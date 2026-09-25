using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.Ranking
{
	// 랭킹 데이터 출처. 지금은 가짜 데이터(DummyRankingProvider)이고, 서버 연동 시 구현체만 바꾼다.
	public interface IRankingProvider
	{
		UniTask<RankingResult> GetRankingAsync(CancellationToken ct);

		UniTask<PlayerProfile> GetProfileAsync(string playerId, CancellationToken ct);
	}
}
