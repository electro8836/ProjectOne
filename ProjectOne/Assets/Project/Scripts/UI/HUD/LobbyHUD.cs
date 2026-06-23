using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;
using ProjectOne.Battle;
using ProjectOne.Event;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 로비 씬(3.Lobby) HUD.
	// 캐릭터 화면 중앙 배치 + 내비게이션 버튼들 (캐릭터/인벤토리/상점/던전 입장 등)
	public class LobbyHUD : UIScreen
	{
		[SerializeField] private UIButton _testButton;

		[Header("하단 탭")]
		[SerializeField] private TabGroup _tabGroup;

		[Header("임시 전투 진입 파라미터 (던전 선택 UI 구현 전까지)")]
		[SerializeField] private int _testMapId = 1;

		// 탭이 열 화면의 Addressable 주소 (탭→화면 매핑은 코드에서 관리)
		private const string EQUIPMENT_ADDRESS = "UI_Equipment";

		private void Awake()
		{
			_testButton.OnClickEvent += onBattleEnterClicked;

			// 배타 선택은 TabGroup이 담당하고, 여기선 선택된 탭의 화면 처리만 한다.
			_tabGroup.OnTabChanged += onTabChanged;
			EventManager.Instance.Subscribe<OverlayClosedEvent>(onOverlayClosed);

			Debug.Log("로비씬UI 로드!");
		}

		private void OnDestroy()
		{
			_testButton.OnClickEvent -= onBattleEnterClicked;

			_tabGroup.OnTabChanged -= onTabChanged;
			EventManager.Instance.Unsubscribe<OverlayClosedEvent>(onOverlayClosed);
		}

		private void onTabChanged(int index)
		{
			tabFlowAsync((LobbyMenuTab)index).Forget();
		}

		// 열린 오버레이를 조용히 닫고(선택 유지), 선택된 탭에 연결된 화면을 연다.
		private async UniTask tabFlowAsync(LobbyMenuTab tab)
		{
			await UIManager.Instance.CloseAllOverlaysAsync();

			switch (tab)
			{
				case LobbyMenuTab.Equipment:
					await UIManager.Instance.OpenOverlayAsync<EquipmentUI>(EQUIPMENT_ADDRESS, this.GetCancellationTokenOnDestroy());
					break;
			}
		}

		// 사용자가 화면을 닫아 오버레이 스택이 비면 탭 선택을 해제한다.
		private void onOverlayClosed(OverlayClosedEvent e)
		{
			_tabGroup.ClearSelection();
		}

		private void onBattleEnterClicked()
		{
			BattleContext ctx = new BattleContext();
			ctx.MapId = _testMapId;
			ctx.CharacterId = Account.Instance.Loadout.Selected;

			GameFlow.Instance.ChangeStateAsync(new BattleState(ctx)).Forget();
		}
	}
}
