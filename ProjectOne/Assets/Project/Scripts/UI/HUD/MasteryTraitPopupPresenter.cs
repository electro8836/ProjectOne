using System.Threading;
using Cysharp.Threading.Tasks;
using EDT;
using ProjectOne.Mastery;
using ProjectOne.UserData;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectOne.UI
{
	// 노드 팝업 1회분 렌더 데이터 — Presenter 가 Model 을 보고 계산해 View 에 넘긴다(View 는 그리기만).
	public struct MasteryNodePopupData
	{
		public string name;
		public string levelText;	// "등급 3/5"
		public string desc;
		public string requireText;	// 요구 포인트를 채웠으면 빈 문자열 — View 가 문구를 끈다
	}

	// 누른 노드의 위치를 팝업에 넘기기 위한 참조 묶음. MasteryTraitUI 가 만든다.
	//
	// 팝업은 창보다 상위 캔버스에 뜨므로 트리와 좌표계가 다르다. 스크린 좌표를 거쳐 환산하려면
	// 노드뿐 아니라 기준이 되는 뷰포트와, 노드를 화면 안으로 끌어올 스크롤까지 함께 필요하다.
	public sealed class TraitPopupAnchor
	{
		public RectTransform nodeRect;
		public ScrollRect treeScroll;
		public RectTransform treeViewport;
	}

	// 마스터리 트리 노드 팝업 Presenter — 노드 조회와 투자/회수 판정을 담당한다.
	//
	// 대상 마스터리는 항상 **현재 장착 무기의 것**이다. 트리 화면 자체가 무기 미착용이면 잠기므로
	// (MasteryTraitPresenter.applyInitialTab) 팝업이 열린 시점엔 CurrentProgress 가 반드시 있다.
	public sealed class MasteryTraitPopupPresenter : Presenter<MasteryTraitPopup>
	{
		private int _nodeId;

		// 이 팝업의 수명 토큰. 확인 팝업을 겹쳐 띄울 때 쓴다(View 에는 파괴 토큰 API 가 없다).
		private CancellationToken _ct;

		protected override void OnInitialize()
		{
			view.OnPlusClicked += onPlusClicked;
			view.OnMinusClicked += onMinusClicked;
			view.OnExitClicked += onExitClicked;
		}

		protected override void OnDispose()
		{
			view.OnPlusClicked -= onPlusClicked;
			view.OnMinusClicked -= onMinusClicked;
			view.OnExitClicked -= onExitClicked;
		}

		// 팝업 표시 — 데이터 조회 후 View 에 그리기 지시, 배치까지 끝내고 닫힘까지 대기.
		public async UniTask ShowAsync(int nodeId, TraitPopupAnchor anchor, CancellationToken ct)
		{
			_nodeId = nodeId;
			_ct = ct;

			Table_SkillTreeNode.Row node = MasteryCatalog.GetNode(nodeId);
			if (node == null)
			{
				Debug.LogError($"[MasteryTraitPopup] 트리 노드가 없습니다: {nodeId}");
				view.Reveal();	// 데이터 없음 — 숨김 상태로 갇히지 않도록 표시(닫기 가능)
				await view.WaitForCloseAsync(ct);
				return;
			}

			render();

			// 높이가 확정된 뒤라야 노드 옆 어디에 놓을지·스크롤을 얼마나 밀지 계산할 수 있다.
			view.PlaceFrame(anchor);
			view.Reveal();

			await view.WaitForCloseAsync(ct);
		}

		// ── 입력 ──────────────────────────────────────────────────────────

		private void onPlusClicked()
		{
			Table_WeaponMastery.Row mastery = Account.Instance.Mastery.CurrentMastery;
			if (mastery == null)
			{
				return;
			}

			if (Account.Instance.Mastery.TryInvest(mastery.ID, _nodeId) == true)
			{
				render();
			}
		}

		// 부분 회수가 불가능한 상황이면 잠그는 대신 "트리 전체 초기화" 라는 출구를 제시한다.
		private void onMinusClicked()
		{
			Table_WeaponMastery.Row mastery = Account.Instance.Mastery.CurrentMastery;
			if (mastery == null)
			{
				return;
			}

			RefundBlock block = Account.Instance.Mastery.GetRefundBlock(mastery.ID, _nodeId);
			if (block == RefundBlock.NoLevel)
			{
				return;	// 되돌릴 것이 없다 — 버튼도 잠겨 있다
			}

			if (block != RefundBlock.None)
			{
				confirmResetAsync().Forget();
				return;
			}

			if (Account.Instance.Mastery.TryRefund(mastery.ID, _nodeId) == true)
			{
				render();
			}
		}

		// 초기화 확인. 예를 고르면 트리를 통째로 비우고 이 팝업도 닫는다 —
		// 노드 하나를 되돌리려던 조작이 트리 전체를 바꿨으므로 보드로 돌려보낸다.
		private async UniTask confirmResetAsync()
		{
			CommonPopupData data;
			data.title = "확인";
			data.desc = "마스터리 포인트가 초기화됩니다.\n진행하시겠습니까?";
			data.button1Text = "아니오";
			data.button2Text = "예";

			// 취소가 그대로 던져지면 Forget 이 관측하지 못해 파이널라이저 스레드에서 터진다.
			(bool cancelled, CommonPopupResult result) = await UIManager.Instance
				.ShowCommonPopupAsync(data, _ct).SuppressCancellationThrow();

			if (cancelled == true || result != CommonPopupResult.Button2)
			{
				return;	// 아니오·닫기 — 이 팝업은 그대로 남는다
			}

			// 확인을 기다리는 사이 무기가 바뀌었을 수 있다 — 대상 마스터리를 다시 읽는다.
			Table_WeaponMastery.Row mastery = Account.Instance.Mastery.CurrentMastery;
			if (mastery == null)
			{
				return;
			}

			Account.Instance.Mastery.ResetTree(mastery.ID);
			view.CloseFromInput();
		}

		private void onExitClicked()
		{
			view.CloseFromInput();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		// 텍스트와 버튼 잠금을 한 번에 맞춘다. 투자·회수 직후에도 그대로 다시 호출한다.
		// Desc 는 변하지 않으므로 프레임 높이·위치는 다시 계산하지 않는다.
		private void render()
		{
			Table_SkillTreeNode.Row node = MasteryCatalog.GetNode(_nodeId);
			MasteryBook book = Account.Instance.Mastery;
			Table_WeaponMastery.Row mastery = book.CurrentMastery;
			MasteryProgress progress = book.CurrentProgress;
			if (node == null || mastery == null || progress == null)
			{
				return;
			}

			int level = progress.GetNodeLevel(_nodeId);

			// 문구와 버튼 잠금이 같은 판정에서 나와야 "버튼은 꺼졌는데 이유는 없다"가 생기지 않는다.
			InvestBlock block = progress.GetInvestBlock(node, book.AchievementPoint);

			MasteryNodePopupData data;
			data.name = node.Name;
			data.levelText = $"등급 {level}/{node.MaxLevel}";
			data.desc = node.Desc;
			data.requireText = describeBlock(block, node, progress.GetInvestedAboveRow(node.NodePos_Row));

			view.SetInfo(data);

			// Minus 는 레벨만 보고 잠근다 — 제약에 걸린 회수는 막지 않고 초기화 확인으로 넘긴다.
			bool minus = book.GetRefundBlock(mastery.ID, _nodeId) != RefundBlock.NoLevel;
			view.SetControlsInteractable(block == InvestBlock.None, minus);
		}

		// 투자 불가 사유를 안내 문구로. 만렙은 부족한 것이 아니라 다 채운 상태라 문구를 두지 않는다
		// (LevelText 의 5/5 로 이미 드러난다).
		// investedAbove 는 이 노드보다 위쪽 행에 쓴 포인트다 — 행 게이트가 세는 축이 그것이다.
		private string describeBlock(InvestBlock block, Table_SkillTreeNode.Row node, int investedAbove)
		{
			switch (block)
			{
				case InvestBlock.RequireTreePoint:
					return $"특성 포인트를 {node.RequireTreePoint - investedAbove}개 더 사용해야 합니다";

				case InvestBlock.PrevNodeNotMax:
					return "이전 노드를 최대치로 올려야 합니다";

				case InvestBlock.NoPoint:
					return "마스터리 포인트 1개가 필요합니다";
			}

			return string.Empty;
		}
	}
}
