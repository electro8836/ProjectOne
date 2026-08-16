using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using EDT;
using ProjectOne.Currency;
using ProjectOne.Dungeon;

namespace ProjectOne.UI
{
	// 던전 사망 시 부활/귀환 선택창(Prefab_DungeonContinue).
	// 부활 재화·비용·최대 횟수는 Table_Dungeon 이 소유한다 — 던전마다 값이 다르다 (맵 설계 6절).
	// 게임 정지(Time.timeScale=0)는 호출부(DungeonDirector)가 담당한다.
	public class DungeonContinueUI : UIScreen
	{
		[Header("선택")]
		[SerializeField] private UIButton _reviveButton;   // 부활(다이아 소모)
		[SerializeField] private UIButton _returnButton;   // 로비 귀환(누적 보상 소멸)
		[SerializeField] private TMP_Text _costText;       // 부활 비용 — "x소모량"
		[SerializeField] private TMP_Text _reviveLabelText; // Text_Continue — "부활(남은횟수/총횟수)"

		// 부활 비용 = RevivalCost + RevivalCostStep × (회차 - 1)
		private int _reviveCost;

		// 소모 재화 — 던전마다 다르다
		private EDT.Currency _costType;

		// 남은 부활 횟수 (0 이면 부활 불가)
		private int _remainingRevives;

		// 선택 결과 — true=부활, false=귀환
		private UniTaskCompletionSource<bool> _choiceSource;

		private void Awake()
		{
			_reviveButton.OnClickEvent += onReviveClicked;
			_returnButton.OnClickEvent += onReturnClicked;
		}

		private void OnDestroy()
		{
			_reviveButton.OnClickEvent -= onReviveClicked;
			_returnButton.OnClickEvent -= onReturnClicked;
			_choiceSource?.TrySetCanceled();
		}

		// 비용/부활횟수 표시 후 부활(true)/귀환(false) 선택까지 대기.
		public async UniTask<bool> WaitChoiceAsync(EDT.Currency costType, int reviveCost, int remainingRevives, int totalRevives, CancellationToken ct)
		{
			_costType = costType;
			_reviveCost = reviveCost;
			_remainingRevives = remainingRevives;
			refreshState(remainingRevives, totalRevives);

			_choiceSource = new UniTaskCompletionSource<bool>();

			using (ct.Register(onCanceled))
			{
				return await _choiceSource.Task;
			}
		}

		// 비용("x소모량")/부활횟수("부활(남은/총)") 텍스트 갱신 + 부활 불가 시 버튼 비활성.
		// 부활 버튼은 남은 횟수가 있고 재화가 충분할 때만 활성화한다.
		private void refreshState(int remainingRevives, int totalRevives)
		{
			if (_costText != null)
			{
				_costText.text = "x" + _reviveCost;
			}

			if (_reviveLabelText != null)
			{
				_reviveLabelText.text = "부활(" + remainingRevives + "/" + totalRevives + ")";
			}

			bool canAfford = CurrencyManager.Instance.GetAmount(_costType) >= _reviveCost;
			_reviveButton.interactable = remainingRevives > 0 && canAfford;
		}

		private void onReviveClicked()
		{
			// 남은 부활 횟수 없으면 무시 (버튼 비활성 상태라 정상 흐름에선 도달하지 않음)
			if (_remainingRevives <= 0)
			{
				return;
			}

			// 재화 소모 성공 시에만 부활 확정 (버튼 비활성 상태라 정상 흐름에선 잔액 부족에 도달하지 않음)
			if (CurrencyManager.Instance.TrySpend(_costType, _reviveCost) == false)
			{
				return;
			}

			_choiceSource?.TrySetResult(true);
		}

		private void onReturnClicked()
		{
			_choiceSource?.TrySetResult(false);
		}

		private void onCanceled()
		{
			_choiceSource?.TrySetCanceled();
		}
	}
}
