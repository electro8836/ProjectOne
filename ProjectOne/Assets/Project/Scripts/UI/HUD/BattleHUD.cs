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
		// 카드스킬 구매창 Addressable 주소
		private const string CARD_SHOP_ADDRESS = "Prefab_CardSkillBuy";

		[Header("위젯")]
		[SerializeField] private WaveInfoTitle _waveInfoTitle;
		[SerializeField] private BossUI _bossUI;
		[SerializeField] private MonsterCount _monsterCount;
		[SerializeField] private EssenceCount _essenceCount;

		[Header("공통")]
		[SerializeField] private UIButton _openSelectButton;
		// 상점 기믹 범위 진입 시 노출되는 카드스킬 구매창 열기 버튼
		[SerializeField] private UIButton _openShopButton;

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

			if (_openShopButton != null)
			{
				_openShopButton.OnClickEvent += onShopClicked;
				_openShopButton.gameObject.SetActive(false);
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

			if (_openShopButton != null)
			{
				_openShopButton.OnClickEvent -= onShopClicked;
			}

			_openSelectSource?.TrySetCanceled();
		}

		// 상점 기믹이 호출 — 히어로가 범위에 들고 날 때 상점 열기 버튼을 노출/숨김.
		public void ShowShopButton(bool show)
		{
			if (_openShopButton != null)
			{
				_openShopButton.gameObject.SetActive(show);
			}
		}

		// 상점 열기 버튼 클릭 → 카드스킬 구매창을 오버레이로 연다.
		private void onShopClicked()
		{
			UIManager.Instance.OpenOverlayAsync<CardSkillBuyUI>(CARD_SHOP_ADDRESS, this.GetCancellationTokenOnDestroy()).Forget();
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
			DungeonDirector director = UnityEngine.Object.FindAnyObjectByType<DungeonDirector>();
			if (director != null)
			{
				director.RequestExit();
			}
		}
	}
}
