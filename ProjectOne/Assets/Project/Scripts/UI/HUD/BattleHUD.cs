using UnityEngine;

namespace ProjectOne.UI
{
	// 4.Battle 전투 HUD 매니저.
	// 전투 위젯(웨이브 정보/보스/몬스터 수/조이스틱)을 보유하며, 각 위젯은
	// 자체적으로 전투 이벤트를 구독해 표시/갱신을 처리한다. 여기서는 참조 보유와
	// 공통 버튼(나가기) 처리만 담당한다.
	public class BattleHUD : UIScreen
	{
		[Header("위젯")]
		[SerializeField] private WaveInfoTitle _waveInfoTitle;
		[SerializeField] private BossUI _bossUI;
		[SerializeField] private MonsterCount _monsterCount;
		[SerializeField] private JoystickController _joystick;

		[Header("공통")]
		[SerializeField] private UIButton _exitButton;

		private void Awake()
		{
			if (_exitButton != null)
			{
				_exitButton.OnClickEvent += onExitClicked;
			}
		}

		private void OnDestroy()
		{
			if (_exitButton != null)
			{
				_exitButton.OnClickEvent -= onExitClicked;
			}
		}

		private void onExitClicked()
		{
			// TODO: 전투 퇴장 시 전이할 상태 결정 (로비 복귀 등)
		}
	}
}
