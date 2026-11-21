using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 메인 HUD 맵 이름 — 필드면 "월드-순서 구역명", 던전이면 "던전명 - 스테이지".
	// 마을에서 숨기는 것은 MainHud 의 맥락 묶음이 맡는다.
	public sealed class HudMapInfo
	{
		private readonly Label _text;

		public HudMapInfo(VisualElement root)
		{
			_text = root.Q<Label>("map-text");

			EventManager.Instance.Subscribe<ZoneChangedEvent>(onZoneChanged);
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(onDungeonStageStarted);
			refreshCurrent();
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<ZoneChangedEvent>(onZoneChanged);
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(onDungeonStageStarted);
		}

		// HUD 가 이미 진행 중인 필드·던전 위에 세워지면 이벤트를 놓친다 — 지금 상태를 직접 읽는다.
		private void refreshCurrent()
		{
			if (ProjectOne.Field.WorldDirector.HasInstance == true && ProjectOne.Field.WorldDirector.Instance.CurrentZoneId > 0)
			{
				showField(ProjectOne.Field.WorldDirector.Instance.CurrentZoneId);
				return;
			}

			if (ProjectOne.Dungeon.DungeonDirector.HasInstance == true && ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType != EDT.Dungeon.None)
			{
				showDungeon(ProjectOne.Dungeon.DungeonDirector.Instance.DungeonType, ProjectOne.Dungeon.DungeonDirector.Instance.Stage);
			}
		}

		private void onZoneChanged(ZoneChangedEvent e)
		{
			showField(e.ZoneId);
		}

		private void onDungeonStageStarted(DungeonStageStartedEvent e)
		{
			showDungeon(e.DungeonType, e.Stage);
		}

		private void showField(int zoneId)
		{
			Table_Zone.Row zone = Table_Zone.Get(zoneId);
			if (zone == null)
			{
				Debug.LogError($"[HudMapInfo] Table_Zone.Get({zoneId}) == null");
				return;
			}

			_text.text = zone.WorldID.ToString() + "-" + zone.Order.ToString() + " " + zone.Name;
		}

		private void showDungeon(EDT.Dungeon dungeonType, int stage)
		{
			Table_Dungeon.Row dungeon = Table_Dungeon.Get(dungeonType);
			if (dungeon == null)
			{
				Debug.LogError($"[HudMapInfo] Table_Dungeon.Get({dungeonType}) == null");
				return;
			}

			_text.text = dungeon.Name + " - " + stage.ToString();
		}
	}
}
