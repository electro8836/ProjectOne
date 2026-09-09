using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Event;
using ProjectOne.Field;

namespace ProjectOne.UI
{
	// 월드 화면 Presenter — 지금 어디에 있는지 조회와 팝업 열기를 담당한다.
	//
	// 던전 4종 중 골드만 연결한다. 나머지 셋은 콘텐츠가 아직 없어 버튼을 눌러도 아무 일도 없다 —
	// 빈 팝업을 띄우는 대신 반응을 주지 않는 쪽을 택했다.
	public sealed class WorldPresenter : Presenter<WorldUI>
	{
		private const string ACT_LIST_POPUP_ADDRESS = "UIPrefab_ActListPopup";
		private const string GOLD_DUNGEON_POPUP_ADDRESS = "UIPrefab_GoldDungeonPopup";

		// 마을의 Map.ID. 필드 밖에 있을 때 위치 이름을 여기서 가져온다.
		private const int TOWN_MAP_ID = 1;

		private CancellationTokenSource _renderCts;	// 렌더 단위 취소 (아이콘 로드 경합 방지)

		protected override void OnInitialize()
		{
			view.OnActClicked += onActClicked;
			view.OnGoldDungeonClicked += onGoldDungeonClicked;
			view.OnHomeClicked += onHomeClicked;

			// 필드를 옮기면 위치와 진행도가 함께 바뀐다.
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			view.OnActClicked -= onActClicked;
			view.OnGoldDungeonClicked -= onGoldDungeonClicked;
			view.OnHomeClicked -= onHomeClicked;

			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
		}

		public override UniTask OnOpenAsync(CancellationToken ct)
		{
			render();
			return UniTask.CompletedTask;
		}

		// ── View 입력 핸들러 ──────────────────────────────────────────────

		private void onActClicked()
		{
			showActListAsync().Forget();
		}

		// 이동으로 팝업이 닫혔으면 월드 창도 함께 닫는다 — 목적지에 도착했는데 지도를 계속 띄워 둘 이유가 없다.
		// 이동이 아니었다면(닫기·딤) 그 사이 바뀐 것이 있을 수 있으니 다시 그린다.
		private async UniTaskVoid showActListAsync()
		{
			(bool cancelled, bool moved) = await UIManager.Instance
				.ShowActListPopupAsync(ACT_LIST_POPUP_ADDRESS, view.GetDestroyToken())
				.SuppressCancellationThrow();

			if (cancelled == true)
			{
				return;
			}

			if (moved == true)
			{
				UIManager.Instance.CloseWindowAsync().Forget();
				return;
			}

			render();
		}

		private void onGoldDungeonClicked()
		{
			UIManager.Instance
				.ShowGoldDungeonPopupAsync(GOLD_DUNGEON_POPUP_ADDRESS, view.GetDestroyToken())
				.Forget();
		}

		// 창을 닫는다. 마지막 창이면 WindowClosedEvent 가 발행되어 네비게이션 바의 탭 선택도 함께 풀린다.
		private void onHomeClicked()
		{
			UIManager.Instance.CloseWindowAsync().Forget();
		}

		private void onGameStateChanged(GameStateChangedEvent e)
		{
			render();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 아이콘 로드가 await 라 연속 호출이 겹칠 수 있다 — 직전 렌더는 취소한다.
		private void render()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderAsync(buildInfo(), _renderCts.Token).Forget();
		}

		private WorldInfoData buildInfo()
		{
			int actId = FieldProgress.GetCurrentActId();
			Table_Act.Row act = Table_Act.Get(actId);

			WorldInfoData data;
			data.actIconAddress = act != null ? act.Icon : string.Empty;
			data.actName = act != null ? act.Name : string.Empty;
			data.locationName = getLocationName();
			data.progress = FieldProgress.GetActProgress(actId);

			return data;
		}

		// 필드 안이면 그 필드 이름, 밖이면 마을 이름. 둘 다 못 찾으면 빈 문자열로 두고 표시만 비운다.
		private static string getLocationName()
		{
			int fieldId = FieldProgress.GetCurrentFieldId();
			if (fieldId > 0)
			{
				Table_Field.Row field = Table_Field.Get(fieldId);
				if (field != null)
				{
					return field.Name;
				}
			}

			Table_Map.Row town = Table_Map.Get(TOWN_MAP_ID);
			return town != null ? town.Name : string.Empty;
		}
	}
}
