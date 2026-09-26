using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectOne.Event;
using ProjectOne.Quests;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 퀘스트 목록 팝업 Presenter — 체인 전체를 깔고 각 칸의 상태를 정한다.
	//
	// 진행 중 퀘스트는 항상 1개이고 ID 오름차순이 곧 진행 순서다(QuestBook 규칙).
	// 그래서 "깼는가" 는 ClearedQuestId 와의 대소 비교 하나로 끝난다 — 수령 플래그가 따로 없다.
	// "수령 대기" 는 진행 중 퀘스트가 UI 완료형이고 목표를 달성했을 때뿐이다.
	//
	// 목록은 QuestChangeEvent 로만 다시 그린다 — 수령은 이 팝업의 보상 칸이나 QuestInfo 에서 일어나고,
	// QuestTracker 의 자동 완료까지 같은 경로로 따라온다.
	//
	// 다만 그 이벤트는 몬스터를 한 마리 잡을 때마다도 날아온다(QuestTracker.publishActive).
	// 목록에는 진행도 숫자가 없어 그때마다 그려 봐야 화면이 똑같으므로, 아래 세 값이
	// 바뀔 때만 그린다 — 그러지 않으면 보고 있던 목록이 처치마다 다시 그려진다.
	public sealed class QuestListPopupPresenter : Presenter<QuestListPopup>
	{
		private readonly List<QuestSlotData> _slotData = new List<QuestSlotData>(16);

		private Action<QuestChangeEvent> _onQuestChanged;

		// 뷰포트 맨 위로 올릴 칸 — 진행 중인 퀘스트. 없으면 -1.
		private int _focusIndex = -1;

		// buildSlotData 가 남기는 화면 상태. 슬롯이 그리는 나머지(이름·설명·보상)는 테이블 값이라
		// 런타임에 변하지 않으므로, 이 셋이 곧 목록 화면의 전부다.
		private int _currentId;
		private int _clearedId;
		private bool _currentClaimable;

		// 마지막으로 실제로 그린 값. 첫 렌더가 반드시 돌도록 없는 ID 로 시작한다.
		private int _renderedCurrentId = -1;
		private int _renderedClearedId = -1;
		private bool _renderedCurrentClaimable;

		// 렌더 단위 취소. 수령은 이벤트를 두 번 쏘므로(완료 + 다음 퀘스트 열림)
		// 아이콘 로드가 끝나기 전에 다음 렌더가 겹친다 — 직전 렌더를 접는다.
		private CancellationTokenSource _renderCts;

		protected override void OnInitialize()
		{
			_onQuestChanged = onQuestChanged;
			EventManager.Instance.Subscribe<QuestChangeEvent>(_onQuestChanged);
		}

		protected override void OnDispose()
		{
			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
				_renderCts = null;
			}

			EventManager.Instance.Unsubscribe<QuestChangeEvent>(_onQuestChanged);
		}

		public async UniTask ShowAsync(CancellationToken ct)
		{
			buildSlotData();
			markRendered();

			await view.RenderAsync(_slotData, ct);

			// 스크롤은 여기서 한 번만 맞춘다 — 이후 갱신은 사용자가 보고 있는 위치를 그대로 둔다.
			view.FocusSlot(_focusIndex);

			view.Reveal();
		}

		// ── 렌더 ──────────────────────────────────────────────────────────

		private void onQuestChanged(QuestChangeEvent e)
		{
			render();
		}

		// 진행 퀘스트가 바뀌거나 클리어 지점이 움직이거나 수령 대기가 될 때만 그린다.
		// 진행도만 오른 경우는 목록에 보이는 것이 없으므로 그냥 흘려보낸다.
		//
		// 여기서는 스크롤을 건드리지 않는다 — FocusSlot 은 팝업을 처음 열 때만 부른다.
		private void render()
		{
			buildSlotData();

			if (_renderedCurrentId == _currentId
				&& _renderedClearedId == _clearedId
				&& _renderedCurrentClaimable == _currentClaimable)
			{
				return;
			}

			markRendered();

			if (_renderCts != null)
			{
				_renderCts.Cancel();
				_renderCts.Dispose();
			}

			_renderCts = CancellationTokenSource.CreateLinkedTokenSource(view.GetDestroyToken());
			view.RenderAsync(_slotData, _renderCts.Token).Forget();
		}

		private void markRendered()
		{
			_renderedCurrentId = _currentId;
			_renderedClearedId = _clearedId;
			_renderedCurrentClaimable = _currentClaimable;
		}

		private void buildSlotData()
		{
			QuestBook book = Account.Instance.Quests;

			int clearedId = book.ClearedQuestId;
			int currentId = (book.Current.IsActive == true) ? book.Current.questId : 0;

			IReadOnlyList<QuestCatalog.BakedQuest> chain = QuestCatalog.Chain;

			_clearedId = clearedId;
			_currentId = currentId;
			_currentClaimable = false;
			_focusIndex = -1;

			_slotData.Clear();
			for (int i = 0; i < chain.Count; i++)
			{
				EDT.Table_Quest.Row row = chain[i].row;

				QuestSlotData data;
				data.questId = row.ID;
				data.name = row.Name;
				data.desc = row.Desc;
				data.isCurrent = row.ID == currentId;
				data.isCleared = row.ID <= clearedId;
				data.isClaimable = data.isCurrent
					&& row.CompleteType == EDT.QuestCompleteType.UI
					&& book.IsObjectiveMet(row.ID);
				data.rewardGroupId = row.RewardGroupID;
				_slotData.Add(data);

				if (data.isCurrent == true)
				{
					_focusIndex = i;
					_currentClaimable = data.isClaimable;
				}
			}
		}
	}
}
