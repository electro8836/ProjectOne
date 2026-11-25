using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.CameraSystem;
using ProjectOne.Currency;
using ProjectOne.Dungeon;
using ProjectOne.Network;
using ProjectOne.Shared;

namespace ProjectOne.UI
{
	// 계속하기 팝업에서 유저가 고른 것.
	public enum ContinueChoice
	{
		Exit,     // 나가기 — 마을로 복귀
		Revive,   // 무료 부활 — 필드
		Retry,    // 유료 부활 — 던전 (서버 재화 차감까지 끝난 상태)
	}

	// 계속하기 팝업 표시 데이터.
	//
	// **문구와 비용 공식을 여기 팩토리에 모은다.** 호출부(WorldDirector·DungeonDirector)가
	// 각자 문자열을 들면 같은 문구가 세 군데로 흩어져 한쪽만 고쳐지는 사고가 난다.
	// 호출부는 ForFieldDeath() 처럼 의도만 적는다.
	public struct ContinuePopupData
	{
		public string title;
		public string desc;

		public bool showRevive;   // 무료 부활 (필드)
		public bool showRetry;    // 유료 부활 (던전)
		public bool showExit;

		public EDT.Currency costType;
		public int cost;

		// 필드 사망 — 시작 지점에서 조건 없이 다시 시작한다.
		public static ContinuePopupData ForFieldDeath()
		{
			ContinuePopupData data = new ContinuePopupData();
			data.title = "당신은 사망했습니다.";
			data.desc = "영웅은 마지막 체크 포인트에서 부활합니다.";
			data.showRevive = true;
			return data;
		}

		// 던전 사망 — 남은 부활 횟수가 있으면 유료 부활을 제안하고, 없으면 나가기만 남긴다.
		//
		// 비용 = RevivalCost + (부활횟수 - 1) × RevivalCostRatioStep × RevivalCost
		// 부활횟수는 이번에 시도하는 회차이므로 usedCount + 1 이다.
		public static ContinuePopupData ForDungeonDeath(Table_Dungeon.Row dungeon, int usedCount)
		{
			ContinuePopupData data = new ContinuePopupData();
			data.title = "당신은 사망했습니다.";
			data.showExit = true;

			int max = (dungeon != null) ? dungeon.MaxRevivalCount : 0;
			int remaining = max - usedCount;
			if (remaining <= 0)
			{
				data.desc = "부활 횟수를 모두 사용해서 더이상 부활 할 수 없습니다.";
				return data;
			}

			int tryCount = usedCount + 1;
			data.desc = "재화를 사용하여 " + remaining + "회 부활 할 수 있습니다.\n부활 하시겠습니까?";
			data.showRetry = true;
			data.costType = dungeon.RevivalCostType;
			data.cost = DungeonRules.GetRevivalCost(dungeon, tryCount);
			return data;
		}

		// 미궁 불에 닿음 — 부활 없이 즉시 실패다. 나가기만 남긴다.
		public static ContinuePopupData ForLabyrinthFire()
		{
			ContinuePopupData data = new ContinuePopupData();
			data.title = "당신은 사망했습니다.";
			data.desc = "무너지는 미궁에 휩쓸렸습니다.\n정비를 위해 마을로 돌아갑니다.";
			data.showExit = true;
			return data;
		}

		// 제한시간 초과 — 되돌릴 수 없다. 나가기만 남긴다.
		public static ContinuePopupData ForTimeout()
		{
			ContinuePopupData data = new ContinuePopupData();
			data.title = "시간 초과";
			data.desc = "제한 시간이 모두 소모되었습니다.\n정비를 위해 마을로 돌아갑니다.";
			data.showExit = true;
			return data;
		}
	}

	// 히어로 사망 연출 — 팝업이 뜨기 전에 보여줄 것을 보여준다.
	//
	// 연출 값을 필드/던전 디렉터에 각각 두면 한쪽만 고쳐진다. 여기 한 곳에 모은다.
	public static class DeathSequence
	{
		// 사망부터 팝업까지 총 대기(초)
		private const float TotalSeconds = 3f;

		// 줌인 목표 크기와 걸리는 시간
		private const float ZoomSize = 3f;
		private const float ZoomInSeconds = 2f;

		// 부활 후 원래 크기로 되돌아가는 시간
		private const float ZoomResetSeconds = 0.5f;

		// 사망 애니메이션은 UnitBase.Die 가 이미 재생한다 — 여기서는 카메라와 대기만 맡는다.
		//
		// **timeScale 을 건드리지 않는다.** 0 이면 애니메이션도 줌 코루틴도 멈춰 연출이 통째로 죽는다.
		// 게임을 얼리는 것은 팝업이 떠 있는 동안의 몫이다.
		public static async UniTask PlayAsync(CancellationToken ct)
		{
			if (CameraManager.HasInstance == true)
			{
				CameraManager.Instance.ZoomTo(ZoomSize, ZoomInSeconds);
			}

			await UniTask.Delay(System.TimeSpan.FromSeconds(TotalSeconds), cancellationToken: ct);
		}

		// 부활 시 줌 복귀. 나가기(씬 전환)로 끝나면 부를 필요가 없다.
		public static void ResetZoom()
		{
			if (CameraManager.HasInstance == true)
			{
				CameraManager.Instance.ZoomReset(ZoomResetSeconds);
			}
		}
	}

	// 사망·시간초과 시 뜨는 계속하기 팝업(UIPrefab_ContinuePopup, UI Toolkit). 레이아웃은 UIToolkit/Popups/Dungeon/ContinuePopup.uxml.
	//
	// 세 상황(필드 사망 / 던전 사망 / 시간 초과)이 같은 프리팹을 쓰고, 무엇을 보여줄지는
	// ContinuePopupData 가 정한다. 게임 정지(Time.timeScale)는 호출부가 담당한다.
	public class ContinuePopup : ToolkitScreen
	{
		private readonly ToolkitIconLoader _icons = new ToolkitIconLoader();

		private Label _titleText;
		private Label _descText;
		private Button _retryButton;	// 유료 부활
		private Button _exitButton;
		private Button _reviveButton;	// 무료 부활
		private VisualElement _costIcon;
		private Label _costText;

		private ContinuePopupData _data;
		private UniTaskCompletionSource<ContinueChoice> _choiceSource;

		// 부활 요청 응답 대기 중 — 연타로 두 번 차감되지 않게 막는다.
		private bool _isReviving;
		private bool _isDestroyed;

		protected override void OnBind(VisualElement root)
		{
			_titleText = root.Q<Label>("title");
			_descText = root.Q<Label>("desc");
			_retryButton = root.Q<Button>("retry");
			_exitButton = root.Q<Button>("exit");
			_reviveButton = root.Q<Button>("revive");
			_costIcon = root.Q("cost-icon");
			_costText = root.Q<Label>("cost");

			_retryButton.clicked += onRetryClicked;
			_exitButton.clicked += onExitClicked;
			_reviveButton.clicked += onReviveClicked;
		}

		protected override void OnDestroy()
		{
			_isDestroyed = true;
			_icons.ReleaseAll();
			_choiceSource?.TrySetCanceled();
			base.OnDestroy();
		}

		// 표시 후 선택까지 대기.
		public async UniTask<ContinueChoice> ShowAsync(ContinuePopupData data, CancellationToken ct)
		{
			_data = data;
			apply();

			_choiceSource = new UniTaskCompletionSource<ContinueChoice>();

			using (ct.Register(onCanceled))
			{
				return await _choiceSource.Task;
			}
		}

		private void apply()
		{
			_titleText.text = _data.title;
			_descText.text = _data.desc;

			setShown(_retryButton, _data.showRetry);
			setShown(_exitButton, _data.showExit);
			setShown(_reviveButton, _data.showRevive);

			if (_data.showRetry == false)
			{
				return;
			}

			_costText.text = _data.cost.ToString();

			// 잔액이 모자라면 누를 수 없다. 소모 실패로 조용히 아무 일도 안 일어나는 것보다 낫다.
			_retryButton.SetEnabled(CurrencyManager.Instance.GetAmount(_data.costType) >= _data.cost);

			Table_Currency.Row row = Table_Currency.Get(_data.costType);
			string address = (row != null) ? row.Icon : string.Empty;
			_icons.SetAsync(_costIcon, address, this.GetCancellationTokenOnDestroy()).Forget();
		}

		private static void setShown(VisualElement element, bool shown)
		{
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
		}

		// ── 입력 ──────────────────────────────────────────────────────

		// 서버(DungeonRevive)가 부활 상한 판정과 재화 차감을 해야 부활이 확정된다.
		// 버튼이 비활성이라 정상 흐름에선 잔액 부족에 도달하지 않는다.
		private void onRetryClicked()
		{
			if (_isReviving == true)
			{
				return;
			}

			DungeonReviveRequest request = new DungeonReviveRequest();
			request.runId = DungeonRunLedger.Instance.RunId;
			_isReviving = true;
			NetworkManager.Instance.RequestDungeonRevive(request, onRevived);
		}

		// 차감은 서버에 이미 저장됐다 — 로컬도 같은 양을 뺀다(증감 반영). 실패하면 팝업을 그대로 두어 나가기를 고를 수 있게 한다.
		private void onRevived(bool success, DungeonReviveResponse data, string error)
		{
			_isReviving = false;

			if (success == false || data == null)
			{
				Debug.LogWarning($"[ContinuePopup] 던전 부활 실패: {error}");
				return;
			}

			if (data.spent != null)
			{
				CurrencyManager.Instance.TrySpend((EDT.Currency)data.spent.currencyId, data.spent.amount);
			}

			if (_isDestroyed == true)
			{
				return;
			}

			_choiceSource?.TrySetResult(ContinueChoice.Retry);
		}

		private void onReviveClicked()
		{
			_choiceSource?.TrySetResult(ContinueChoice.Revive);
		}

		private void onExitClicked()
		{
			_choiceSource?.TrySetResult(ContinueChoice.Exit);
		}

		private void onCanceled()
		{
			_choiceSource?.TrySetCanceled();
		}
	}
}
