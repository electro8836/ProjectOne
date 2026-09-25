using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 전투력 랭킹 팝업 Presenter — 랭킹을 받아 그리고, 슬롯을 누르면 그 플레이어의 정보 팝업을 띄운다.
	public sealed class RankingPopupPresenter : Presenter<RankingPopup>
	{
		// [임시] 서버 랭킹이 생기면 뒤끝 구현체로 교체한다.
		private readonly IRankingProvider _provider = new DummyRankingProvider();

		protected override void OnInitialize()
		{
			view.OnPlayerSelected += onPlayerSelected;
		}

		protected override void OnDispose()
		{
			view.OnPlayerSelected -= onPlayerSelected;
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			(bool cancelled, RankingResult result) = await _provider.GetRankingAsync(ct).SuppressCancellationThrow();
			if (cancelled == true || result == null)
			{
				return;
			}

			view.RenderRanking(result.top, result.mine);
			view.Reveal();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private void onPlayerSelected(string playerId)
		{
			openPlayerInfoAsync(playerId, view.GetDestroyToken()).Forget();
		}

		// 정보 팝업은 이 팝업 위에 뜬다 — 랭킹 팝업이 닫히면(파괴 토큰) 함께 닫힌다.
		private async UniTaskVoid openPlayerInfoAsync(string playerId, CancellationToken ct)
		{
			(bool cancelled, PlayerProfile profile) = await _provider.GetProfileAsync(playerId, ct).SuppressCancellationThrow();
			if (cancelled == true || profile == null)
			{
				return;
			}

			await UIManager.Instance.ShowPlayerInfoPopupAsync(profile, ct).SuppressCancellationThrow();
		}
	}
}
