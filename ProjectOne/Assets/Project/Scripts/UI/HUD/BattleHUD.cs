using UnityEngine;

namespace ProjectOne.UI
{
	// 던전 씬(5.Dungeon) HUD.
	// 영웅 HP/MP, 스킬 슬롯, 던전 진행 상황 + 나가기 버튼
	public class BattleHUD : UIScreen
	{
		[SerializeField] private UIButton _exitButton;

		// TODO: HeroHPBar, SkillSlot 등 하위 컴포넌트는 각자 이벤트 구독

		private void Awake()
		{
			_exitButton.OnClickEvent += onExitClicked;
		}

		private void OnDestroy()
		{
			_exitButton.OnClickEvent -= onExitClicked;
		}

		private void onExitClicked()
		{
			// TODO: 던전 퇴장 시 전이할 상태 결정 (로비 복귀 등)
		}
	}
}
