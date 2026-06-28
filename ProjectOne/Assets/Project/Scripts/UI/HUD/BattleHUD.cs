using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ProjectOne.Dungeon;

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
		[SerializeField] private EssenceCount _essenceCount;

		[Header("공통")]
		[SerializeField] private UIButton _openSelectButton;

		// OpenSelectButton 클릭 대기용 — WaitOpenSelectAsync 한 번에 하나씩만 사용
		private UniTaskCompletionSource _openSelectSource;

		private void Awake()
		{
			UIManager.Instance.RegisterScreen(this);

			if (_openSelectButton != null)
			{
				_openSelectButton.OnClickEvent += onOpenSelectClicked;
				_openSelectButton.gameObject.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			if (UIManager.HasInstance)
			{
				UIManager.Instance.UnregisterScreen(this);
			}

			if (_openSelectButton != null)
			{
				_openSelectButton.OnClickEvent -= onOpenSelectClicked;
			}

			_openSelectSource?.TrySetCanceled();
		}

		// OpenSelectButton 을 노출하고 클릭될 때까지 대기한다. 클릭(또는 ct 취소) 시 버튼을 다시 숨긴다.
		// 선택 UI 가 열려있는 동안에는 버튼이 숨겨져 있어야 하므로, 호출자가 클릭 직후 선택 UI 를 연다.
		public async UniTask WaitOpenSelectAsync(CancellationToken ct)
		{
			if (_openSelectButton == null)
			{
				return;
			}

			_openSelectButton.gameObject.SetActive(true);
			_openSelectSource = new UniTaskCompletionSource();
			using (ct.Register(onOpenSelectCanceled))
			{
				await _openSelectSource.Task;
			}

			_openSelectButton.gameObject.SetActive(false);
		}

		private void onOpenSelectClicked()
		{
			_openSelectSource?.TrySetResult();
		}

		private void onOpenSelectCanceled()
		{
			_openSelectSource?.TrySetCanceled();
		}

		private void onExitClicked()
		{
			DungeonDirector director = Object.FindAnyObjectByType<DungeonDirector>();
			if (director != null)
			{
				director.RequestExit();
			}
		}
	}
}
