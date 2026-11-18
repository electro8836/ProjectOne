using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace ProjectOne.UI
{
	// 메인 HUD 오른쪽 사이드바 — 랭킹·히어로패스·출석 팝업 버튼. 접기 버튼으로 버튼 묶음을 화면 밖으로 민다.
	// 접기는 USS 전환(translate 1초)이다 — 미는 동안은 접기 버튼을 잠근다.
	public sealed class HudSideBar
	{
		private const long SlideMs = 1000;

		private readonly VisualElement _bar;
		private readonly Button _foldButton;
		private readonly CancellationToken _ct;

		private bool _isExpanded = true;

		public HudSideBar(VisualElement root, CancellationToken ct)
		{
			_bar = root.Q("side-bar");
			_foldButton = root.Q<Button>("side-fold");
			_ct = ct;

			_foldButton.clicked += onFoldClicked;
			root.Q<Button>("side-rank").clicked += onRankClicked;
			root.Q<Button>("side-pass").clicked += onPassClicked;
			root.Q<Button>("side-login").clicked += onLoginClicked;
		}

		private void onFoldClicked()
		{
			_isExpanded = !_isExpanded;
			_bar.EnableInClassList("side-bar--folded", _isExpanded == false);

			_foldButton.SetEnabled(false);
			_foldButton.schedule.Execute(onSlideComplete).StartingIn(SlideMs);
		}

		private void onSlideComplete()
		{
			_foldButton.SetEnabled(true);
		}

		// 팝업이 닫힐 때까지 돌아오지 않는다 — 취소는 HUD 가 사라질 때다.
		private void onRankClicked()
		{
			UIManager.Instance.ShowRankingPopupAsync(_ct).SuppressCancellationThrow().Forget();
		}

		private void onPassClicked()
		{
			UIManager.Instance.ShowHeroPassPopupAsync(_ct).SuppressCancellationThrow().Forget();
		}

		private void onLoginClicked()
		{
			UIManager.Instance.ShowDailyBonusPopupAsync(_ct).SuppressCancellationThrow().Forget();
		}
	}
}
