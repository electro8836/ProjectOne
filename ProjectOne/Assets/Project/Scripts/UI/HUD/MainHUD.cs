using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Flow;

namespace ProjectOne.UI
{
	// 로비 씬(3.Lobby) HUD.
	// 캐릭터 화면 중앙 배치 + 내비게이션 버튼들 (캐릭터/인벤토리/상점/던전 입장 등)
	public class MainHUD : UIScreen
	{
		[SerializeField] private UIButton _characterButton;
		[SerializeField] private UIButton _inventoryButton;
		[SerializeField] private UIButton _shopButton;
		[SerializeField] private UIButton _dungeonEnterButton;

		private void Awake()
		{
			_characterButton.onClick.AddListener(onCharacterClicked);
			_inventoryButton.onClick.AddListener(onInventoryClicked);
			_shopButton.onClick.AddListener(onShopClicked);
			_dungeonEnterButton.onClick.AddListener(onDungeonEnterClicked);
		}

		private void OnDestroy()
		{
			_characterButton.onClick.RemoveListener(onCharacterClicked);
			_inventoryButton.onClick.RemoveListener(onInventoryClicked);
			_shopButton.onClick.RemoveListener(onShopClicked);
			_dungeonEnterButton.onClick.RemoveListener(onDungeonEnterClicked);
		}

		private void onCharacterClicked()
		{
			// TODO: 캐릭터 화면 팝업 열기
		}

		private void onInventoryClicked()
		{
			// TODO: 인벤토리 팝업 열기
		}

		private void onShopClicked()
		{
			// TODO: 상점 팝업 열기
		}

		private void onDungeonEnterClicked()
		{
			GameFlow.Instance.ChangeStateAsync(new DungeonState()).Forget();
		}
	}
}
