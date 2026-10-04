using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Ranking;

namespace ProjectOne.UI
{
	// 전투력 랭킹 팝업 Presenter — 랭킹을 받아 그리고, 슬롯을 누르면 그 플레이어의 정보 팝업을 띄운다.
	public sealed class RankingPopupPresenter : Presenter<RankingPopup>
	{
		// 팝업은 열 때마다 새로 만들어진다 — 목록 캐시가 이어지도록 Provider 는 하나를 같이 쓴다.
		private static readonly IRankingProvider _provider = new BackndRankingProvider();

		// 화면에 그린 목록 — 이어 받으면 Provider 가 이 결과의 top 뒤에 붙인다.
		private RankingResult _result;
		private bool _loadingMore;

		protected override void OnInitialize()
		{
			view.OnPlayerSelected += onPlayerSelected;
			view.OnNeedMore += onNeedMore;
		}

		protected override void OnDispose()
		{
			view.OnPlayerSelected -= onPlayerSelected;
			view.OnNeedMore -= onNeedMore;
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			(bool cancelled, RankingResult result) = await _provider.GetRankingAsync(ct).SuppressCancellationThrow();
			if (cancelled == true || result == null)
			{
				return;
			}

			_result = result;

			bool rendered = await view.RenderRankingAsync(result.top, result.mine, ct);
			if (rendered == false)
			{
				return;
			}

			view.Reveal();
		}

		// ── 내부 ──────────────────────────────────────────────────────

		// 스크롤이 목록 끝에 가까워졌다 — 다음 줄이 있으면 딤 없이 이어 받는다.
		private void onNeedMore()
		{
			if (_result == null || _loadingMore == true || _provider.HasMore == false)
			{
				return;
			}

			loadMoreAsync(view.GetDestroyToken()).Forget();
		}

		private async UniTaskVoid loadMoreAsync(CancellationToken ct)
		{
			_loadingMore = true;
			int before = _result.top.Count;

			(bool cancelled, bool loaded) = await _provider.LoadMoreAsync(ct).SuppressCancellationThrow();
			_loadingMore = false;
			if (cancelled == true || loaded == false)
			{
				return;
			}

			await view.AppendRankingAsync(_result.top, before, ct);
		}

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
