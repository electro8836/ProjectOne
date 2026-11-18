using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 레벨업 알림(UIPrefab_LevelUpMessage, UI Toolkit). 레이아웃은 UIToolkit/HUD/Notice/LevelUpMessage.uxml.
	// 숙련도 레벨업 이벤트를 스스로 구독해 띄운다. 작게 시작해 튀어나온 뒤 잠시 머물다 사라진다(USS 전환).
	//
	// 같은 프레임에 여러 번 오면(한 번에 여러 레벨) 마지막 것만 남겨 이어서 보여 준다.
	public class LevelUpMessage : ToolkitScreen
	{
		private const string MasteryDesc = "숙련도 능력이 강화되었습니다!";

		private const long PopMs = 250;
		private const long HoldMs = 2000;
		private const long FadeMs = 500;

		private struct Entry
		{
			public string levelUp;
			public int level;
			public string desc;
		}

		private VisualElement _box;
		private Label _levelUp;
		private Label _level;
		private Label _desc;

		private IVisualElementScheduledItem _step;
		private int _shownFrame = -1;
		private bool _playing;
		private bool _hasPending;
		private Entry _pending;

		protected override void OnBind(VisualElement root)
		{
			_box = root.Q("levelup");
			_levelUp = root.Q<Label>("levelup-text");
			_level = root.Q<Label>("level-text");
			_desc = root.Q<Label>("desc-text");

			EventManager.Instance.Subscribe<MasteryLevelUpEvent>(onMasteryLevelUp);
		}

		protected override void OnDestroy()
		{
			if (IsBound == true)
			{
				EventManager.Instance.Unsubscribe<MasteryLevelUpEvent>(onMasteryLevelUp);
			}

			base.OnDestroy();
		}

		private void onMasteryLevelUp(MasteryLevelUpEvent evt)
		{
			Table_WeaponMastery.Row row = Table_WeaponMastery.Get(evt.Mastery);
			if (row == null)
			{
				Debug.LogError($"[LevelUpMessage] WeaponMastery 테이블에 없는 마스터리입니다: {evt.Mastery}");
				return;
			}

			Entry entry = new Entry();
			entry.levelUp = row.Name + " 숙련도 레벨업!";
			entry.level = evt.Level;
			entry.desc = MasteryDesc;
			request(entry);
		}

		// 같은 프레임에 이미 띄웠으면 다음 차례로 미룬다. 아니면 지금 것을 끊고 새로 띄운다.
		private void request(Entry entry)
		{
			if (_playing == true && Time.frameCount == _shownFrame)
			{
				_pending = entry;
				_hasPending = true;
				return;
			}

			_hasPending = false;
			show(entry);
		}

		private void show(Entry entry)
		{
			if (_step != null)
			{
				_step.Pause();
			}

			_levelUp.text = entry.levelUp;
			_level.text = "레벨 " + entry.level.ToString();
			_desc.text = entry.desc;

			_playing = true;
			_shownFrame = Time.frameCount;

			// 작게 · 투명에서 시작 — 전환 없이 즉시 되돌린 뒤 다음 프레임에 튀어나오게 한다.
			_box.RemoveFromClassList("levelup--shown");
			_box.RemoveFromClassList("levelup--fading");
			_step = _box.schedule.Execute(popIn);
		}

		private void popIn()
		{
			_box.AddToClassList("levelup--shown");
			_step = _box.schedule.Execute(fadeOut).StartingIn(PopMs + HoldMs);
		}

		private void fadeOut()
		{
			_box.AddToClassList("levelup--fading");
			_step = _box.schedule.Execute(finish).StartingIn(FadeMs);
		}

		private void finish()
		{
			_box.RemoveFromClassList("levelup--shown");
			_box.RemoveFromClassList("levelup--fading");
			_playing = false;

			if (_hasPending == true)
			{
				_hasPending = false;
				show(_pending);
			}
		}
	}
}
