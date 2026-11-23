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
	// 메인 HUD 현재 퀘스트 — 카드(아이콘·제목, 내용 2줄, 목표 종류·진행 수, 게이지) + 오른쪽에 붙은 접기 버튼.
	// 목표를 달성하면 테두리·게이지·버튼이 초록으로 바뀐다(quest-info--met) — 접혀 있어도 버튼으로 알 수 있다.
	//
	// 카드를 누르면 UI 완료형 달성 퀘스트는 수령하고, 그 밖에는 퀘스트 목록 팝업을 연다.
	// 접기는 USS 전환(translate)이다 — 미는 동안은 접기 버튼을 잠근다.
	public sealed class HudQuestInfo
	{
		private const long SlideMs = 350;

		private readonly VisualElement _info;
		private readonly VisualElement _frame;
		private readonly Label _title;
		private readonly Label _desc;
		private readonly Label _label;
		private readonly Label _count;
		private readonly VisualElement _fill;
		private readonly Button _foldButton;
		private readonly CancellationToken _ct;

		private bool _isExpanded = true;

		private int _lastQuestId = -1;
		private string _lastDesc;
		private string _lastCount;
		private float _lastRatio = -1f;
		private int _lastMet = -1;	// -1 미정 / 0 미달성 / 1 달성

		public HudQuestInfo(VisualElement root, CancellationToken ct)
		{
			_info = root.Q("quest-info");
			_frame = root.Q("quest-frame");
			_title = root.Q<Label>("quest-title");
			_desc = root.Q<Label>("quest-desc");
			_label = root.Q<Label>("quest-label");
			_count = root.Q<Label>("quest-count");
			_fill = root.Q("quest-fill");
			_foldButton = root.Q<Button>("quest-fold");
			_ct = ct;

			_frame.RegisterCallback<ClickEvent>(onFrameClicked);
			_frame.AddToClassList("sfx-click");	// 클릭음 대상(UIManager)
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
				_label.text = GetTargetLabel(baked.row.QuestTargetType);
			}

			int current = book.GetCurrentCount(baked);
			int required = QuestBook.GetRequiredCount(baked);

			if (_lastDesc != baked.row.Desc)
			{
				_lastDesc = baked.row.Desc;
				_desc.text = baked.row.Desc;
			}

			string count = $"{current}/{required}";
			if (_lastCount != count)
			{
				_lastCount = count;
				_count.text = count;
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
				_info.EnableInClassList("quest-info--met", met == 1);
			}
		}

		// 진행도 앞에 붙는 목표 종류 — 퀘스트 목록 팝업도 쓴다
		public static string GetTargetLabel(QuestTargetType type)
		{
			switch (type)
			{
				case QuestTargetType.MonsterKill:
					return "몬스터 처치";
				case QuestTargetType.EliteKill:
					return "엘리트 처치";
				case QuestTargetType.BossKill:
					return "보스 처치";
				case QuestTargetType.DungeonClear:
					return "던전 클리어";
				case QuestTargetType.ReachBattlePower:
					return "전투력";
				default:
					return string.Empty;
			}
		}

		private void onFrameClicked(ClickEvent evt)
		{
			QuestBook book = Account.Instance.Quests;
			QuestCatalog.BakedQuest baked = book.GetCurrentBaked();
			if (baked != null && baked.row.CompleteType == QuestCompleteType.UI && book.IsObjectiveMet(baked.row.ID) == true)
			{
				book.TryComplete(baked.row.ID);
				return;
			}

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
