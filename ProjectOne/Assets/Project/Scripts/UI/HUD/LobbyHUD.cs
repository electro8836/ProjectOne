using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;

namespace ProjectOne.UI
{
	// 로비 씬(3.Lobby) HUD.
	// 캐릭터 화면 중앙 배치 + 내비게이션 버튼들 (캐릭터/인벤토리/상점/던전 입장 등)
	public class LobbyHUD : UIScreen
	{
		[SerializeField] private UIButton _testButton;

		[SerializeField] private UITabButton _testTabButton1;
		[SerializeField] private UITabButton _testTabButton2;

		[SerializeField] private UIButton _characterButton;
		[SerializeField] private UIButton _inventoryButton;
		[SerializeField] private UIButton _shopButton;
		[SerializeField] private UIButton _dungeonEnterButton;

		private void Awake()
		{
			//_characterButton.OnClickEvent += onCharacterClicked;
			//_inventoryButton.OnClickEvent += onInventoryClicked;
			//_shopButton.OnClickEvent += onShopClicked;
			//_dungeonEnterButton.OnClickEvent += onBattleEnterClicked;

			_testButton.OnClickEvent += onBattleEnterClicked;
			_testTabButton1.OnClickEvent += onInventoryClicked;
			_testTabButton2.OnClickEvent += onShopClicked;

			Debug.Log("로비씬UI 로드!");
		}

		private void OnDestroy()
		{
			//_characterButton.OnClickEvent -= onCharacterClicked;
			//_inventoryButton.OnClickEvent -= onInventoryClicked;
			//_shopButton.OnClickEvent -= onShopClicked;
			//_dungeonEnterButton.OnClickEvent -= onBattleEnterClicked;

			_testButton.OnClickEvent -= onBattleEnterClicked;
			_testTabButton1.OnClickEvent -= onInventoryClicked;
			_testTabButton2.OnClickEvent -= onShopClicked;
		}

		private void onCharacterClicked()
		{
			// TODO: 캐릭터 화면 팝업 열기
			Debug.Log("캐릭터 버튼 누름!");
		}

		private void onInventoryClicked()
		{
			// TODO: 인벤토리 팝업 열기
		}

		private void onShopClicked()
		{
			// TODO: 상점 팝업 열기
		}

		private void onBattleEnterClicked()
		{
			GameFlow.Instance.ChangeStateAsync(new BattleState()).Forget();
		}
	}
}
