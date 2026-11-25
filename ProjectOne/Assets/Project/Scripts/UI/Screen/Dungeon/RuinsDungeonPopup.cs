using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.UI
{
	// 유적던전 팝업의 View(MVP, UI Toolkit). UIManager.ShowRuinsDungeonPopupAsync 가 ShowAsync 로 닫힘을 기다린다.
	// 화면은 DungeonStagePopup 공용 틀이고, 어느 단계를 열 수 있는지·입장 판정은 RuinsDungeonPopupPresenter 가 한다.
	public class RuinsDungeonPopup : DungeonStagePopup
	{
		private readonly RuinsDungeonPopupPresenter _presenter = new RuinsDungeonPopupPresenter();

		protected override void InitializePresenter()
		{
			_presenter.Initialize(this);
		}

		protected override void DisposePresenter()
		{
			_presenter.Dispose();
		}

		protected override UniTask OpenPresenterAsync(CancellationToken ct)
		{
			return _presenter.OnOpenAsync(ct);
		}
	}
}
