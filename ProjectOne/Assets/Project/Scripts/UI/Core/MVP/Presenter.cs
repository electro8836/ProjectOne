using System.Threading;
using Cysharp.Threading.Tasks;

namespace ProjectOne.UI
{
	// MVP Presenter 베이스(순수 C#). Model(Account 도메인) 조회·조작과 Event 구독을 담당하고,
	// View(IView 파생)에는 "무엇을 그릴지"만 지시한다(Passive View — View 는 결정하지 않는다).
	//
	// 생명주기 표준: View(UIScreen 파생)가
	//   Awake       → presenter.Initialize(this)
	//   OnOpenAsync → presenter.OnOpenAsync(ct)
	//   OnCloseAsync→ presenter.OnCloseAsync()
	//   OnDestroy   → presenter.Dispose()
	// 로 위임한다. DI 컨테이너 없이 View 가 자기 Presenter 를 직접 생성한다.
	public abstract class Presenter<TView> where TView : class, IView
	{
		protected TView view;

		// View 연결 + 구독 시작 — View 의 Awake 에서 1회 호출.
		public void Initialize(TView target)
		{
			view = target;
			OnInitialize();
		}

		// 화면 열림 — 초기 데이터 로드/표시. View.OnOpenAsync 에서 위임.
		public virtual UniTask OnOpenAsync(CancellationToken ct)
		{
			return UniTask.CompletedTask;
		}

		// 화면 닫힘 — 정리/서버 flush 등. View.OnCloseAsync 에서 위임.
		public virtual UniTask OnCloseAsync()
		{
			return UniTask.CompletedTask;
		}

		// 구독 해제 + 정리 — View 의 OnDestroy 에서 호출.
		public void Dispose()
		{
			OnDispose();
			view = null;
		}

		// 하위 훅 — Event 구독은 여기서(메서드 그룹 연결, 람다 금지).
		protected virtual void OnInitialize()
		{
		}

		// 하위 훅 — Event 구독 해제는 여기서.
		protected virtual void OnDispose()
		{
		}
	}
}
