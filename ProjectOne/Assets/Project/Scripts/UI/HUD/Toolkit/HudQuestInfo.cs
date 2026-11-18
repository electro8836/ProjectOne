using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;
using ProjectOne.Quests;
using ProjectOne.UserData;

namespace ProjectOne.UI
{
	// 메인 HUD 현재 퀘스트 — 제목·설명(진행 수)·진행 게이지, 목표 달성 시 초록 테두리.
	//
	// 틀을 누르면 UI 완료형 퀘스트를 수령하고, 오른쪽 위 버튼은 퀘스트 목록 팝업을 연다.
	// 접기는 USS 전환(translate 1초)이다 — 미는 동안은 접기 버튼을 잠근다.
	public sealed class HudQuestInfo
	{
		private const long SlideMs = 1000;

		private readonly VisualElement _info;
		private readonly VisualElement _frame;
		private readonly Label _title;
		private readonly Label _desc;
		private readonly VisualElement _fill;
		private readonly Button _foldButton;
		private readonly CancellationToken _ct;

		private bool _isExpanded = true;

		private int _lastQuestId = -1;
		private string _lastDesc;
		private float _lastRatio = -1f;
		private int _lastMet = -1;	// -1 미정 / 0 미달성 / 1 달성

		public HudQuestInfo(VisualElement root, CancellationToken ct)
		{
			_info = root.Q("quest-info");
			_frame = root.Q("quest-frame");
			_title = root.Q<Label>("quest-title");
			_desc = root.Q<Label>("quest-desc");
			_fill = root.Q("quest-fill");
			_foldButton = root.Q<Button>("quest-fold");
			_ct = ct;

			_frame.RegisterCallback<ClickEvent>(onFrameClicked);
			_frame.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
			root.Q<Button>("quest-list-button").clicked += onQuestListClicked;
			_foldButton.clicked += onFoldClicked;

			EventManager.Instance.Subscribe<QuestChangeEvent>(onQuestChanged);
			EventManager.Instance.Subscribe<CharacterChangeEvent>(onCharacterChanged);

			refresh();
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<QuestChangeEvent>(onQuestChanged);
			EventManager.Instance.Unsubscribe<CharacterChangeEvent>(onCharacterChanged);
		}

		private void onQuestChanged(QuestChangeEvent e)
		{
			refresh();
		}

		private void onCharacterChanged(CharacterChangeEvent e)
		{
			refresh();
		}

		private void refresh()
		{
			QuestBook book = Account.Instance.Quests;
			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();
			if (baked == null)
			{
				_info.style.display = DisplayStyle.None;
				return;
			}

			_info.style.display = DisplayStyle.Flex;

			int questId = baked.row.ID;
			if (_lastQuestId != questId)
			{
				_lastQuestId = questId;
				_title.text = baked.row.Name;
			}

			int current = book.GetCurrentCount(baked);
			int required = QuestBook.GetRequiredCount(baked);

			string text = $"{baked.row.Desc} ({current}/{required})";
			if (_lastDesc != text)
			{
				_lastDesc = text;
				_desc.text = text;
			}

			float ratio = (required > 0) ? Mathf.Clamp01((float)current / required) : 0f;
			if (Mathf.Approximately(_lastRatio, ratio) == false)
			{
				_lastRatio = ratio;
				_fill.style.width = Length.Percent(ratio * 100f);
			}

			int met = book.IsObjectiveMet(questId) ? 1 : 0;
			if (_lastMet != met)
			{
				_lastMet = met;
				_frame.EnableInClassList("quest-frame--met", met == 1);
			}
		}

		// 목록 버튼 위 클릭은 버튼이 처리한다 — 틀의 수령과 겹치지 않게 버튼 안에서 온 클릭은 거른다.
		private void onFrameClicked(ClickEvent evt)
		{
			if (isInsideButton((VisualElement)evt.target) == true)
			{
				return;
			}

			QuestBook book = Account.Instance.Quests;
			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();
			if (baked == null || baked.row.CompleteType != QuestCompleteType.UI)
			{
				return;
			}

			book.TryComplete(baked.row.ID);
		}

		private bool isInsideButton(VisualElement element)
		{
			while (element != null && element != _frame)
			{
				if (element is Button)
				{
					return true;
				}

				element = element.parent;
			}

			return false;
		}

		private void onQuestListClicked()
		{
			UIManager.Instance.ShowQuestListPopupAsync(_ct).Forget();
		}

		private void onFoldClicked()
		{
			_isExpanded = !_isExpanded;
			_info.EnableInClassList("quest-info--folded", _isExpanded == false);

			_foldButton.SetEnabled(false);
			_foldButton.schedule.Execute(onSlideComplete).StartingIn(SlideMs);
		}

		private void onSlideComplete()
		{
			_foldButton.SetEnabled(true);
		}
	}
}
