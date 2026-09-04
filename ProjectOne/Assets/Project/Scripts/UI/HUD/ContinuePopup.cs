using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EDT;
using ProjectOne.CameraSystem;
using ProjectOne.Currency;
using ProjectOne.Resources;

namespace ProjectOne.UI
{
	// 계속하기 팝업에서 유저가 고른 것.
	public enum ContinueChoice
	{
		Exit,     // 나가기 — 마을로 복귀
		Revive,   // 무료 부활 — 필드
		Retry,    // 유료 부활 — 던전 (재화 소모까지 끝난 상태)
	}

	// 계속하기 팝업 표시 데이터.
	//
	// **문구와 비용 공식을 여기 팩토리에 모은다.** 호출부(FieldDirector·DungeonDirector)가
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
			data.cost = Mathf.RoundToInt(dungeon.RevivalCost + (tryCount - 1) * dungeon.RevivalCostRatioStep * dungeon.RevivalCost);
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

	// 사망·시간초과 시 뜨는 계속하기 팝업(UIPrefab_ContinuePopup).
	//
	// 세 상황(필드 사망 / 던전 사망 / 시간 초과)이 같은 프리팹을 쓰고, 무엇을 보여줄지는
	// ContinuePopupData 가 정한다. 게임 정지(Time.timeScale)는 호출부가 담당한다.
	public class ContinuePopup : UIScreen
	{
		[Header("문구")]
		[SerializeField] private TMP_Text _titleText;   // Text_Title
		[SerializeField] private TMP_Text _descText;    // Text_Desc

		[Header("버튼")]
		[SerializeField] private UIButton _retryButton;    // Button_Retry — 유료 부활
		[SerializeField] private UIButton _exitButton;     // Button_Exit
		[SerializeField] private UIButton _reviveButton;   // Button_Revive — 무료 부활

		[Header("부활 비용")]
		[SerializeField] private Image _costIcon;       // Button_Retry/Group/Icon
		[SerializeField] private TMP_Text _costText;    // Button_Retry/Group/CostText

		private ContinuePopupData _data;
		private UniTaskCompletionSource<ContinueChoice> _choiceSource;

		// 참조카운트 해제용 아이콘 주소 추적 (아틀라스 스프라이트는 null 로 두어 대상 제외)
		private string _iconAddress;

		private void Awake()
		{
			_retryButton.OnClickEvent += onRetryClicked;
			_exitButton.OnClickEvent += onExitClicked;
			_reviveButton.OnClickEvent += onReviveClicked;
		}

		private void OnDestroy()
		{
			_retryButton.OnClickEvent -= onRetryClicked;
			_exitButton.OnClickEvent -= onExitClicked;
			_reviveButton.OnClickEvent -= onReviveClicked;

			releaseIcon();
			_choiceSource?.TrySetCanceled();
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
			if (_titleText != null)
			{
				_titleText.text = _data.title;
			}

			if (_descText != null)
			{
				_descText.text = _data.desc;
			}

			_retryButton.gameObject.SetActive(_data.showRetry);
			_exitButton.gameObject.SetActive(_data.showExit);
			_reviveButton.gameObject.SetActive(_data.showRevive);

			if (_data.showRetry == false)
			{
				return;
			}

			if (_costText != null)
			{
				_costText.text = _data.cost.ToString();
			}

			// 잔액이 모자라면 누를 수 없다. 소모 실패로 조용히 아무 일도 안 일어나는 것보다 낫다.
			_retryButton.interactable = (CurrencyManager.Instance.GetAmount(_data.costType) >= _data.cost);

			applyCostIconAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		// 재화 아이콘 — 아틀라스에 있으면 동기로 즉시 세팅(ItemSlot 과 같은 방식).
		private async UniTaskVoid applyCostIconAsync(CancellationToken ct)
		{
			if (_costIcon == null)
			{
				return;
			}

			Table_Currency.Row row = Table_Currency.Get(_data.costType);
			string address = (row != null) ? row.Icon : string.Empty;
			if (string.IsNullOrEmpty(address) == true)
			{
				_costIcon.enabled = false;
				return;
			}

			Sprite atlasSprite = AtlasManager.Instance.Get(address);
			if (atlasSprite != null)
			{
				_costIcon.sprite = atlasSprite;
				_costIcon.enabled = true;
				return;
			}

			_costIcon.enabled = false;

			(bool cancelled, Sprite icon) = await ResourceManager.Instance.AcquireAsync<Sprite>(address, ct).SuppressCancellationThrow();
			if (cancelled == true || icon == null)
			{
				return;
			}

			_iconAddress = address;
			_costIcon.sprite = icon;
			_costIcon.enabled = true;
		}

		private void releaseIcon()
		{
			if (string.IsNullOrEmpty(_iconAddress) == false && ResourceManager.HasInstance == true)
			{
				ResourceManager.Instance.Release(_iconAddress);
				_iconAddress = null;
			}
		}

		// ── 입력 ──────────────────────────────────────────────────────

		// 재화 소모가 성공해야 부활이 확정된다. 버튼이 비활성이라 정상 흐름에선 잔액 부족에 도달하지 않는다.
		private void onRetryClicked()
		{
			if (CurrencyManager.Instance.TrySpend(_data.costType, _data.cost) == false)
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
