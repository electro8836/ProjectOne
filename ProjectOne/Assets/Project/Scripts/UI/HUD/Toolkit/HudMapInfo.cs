using UnityEngine;
using UnityEngine.UIElements;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 메인 HUD 맵 이름 — 마을이면 마을 이름, 필드면 "월드-순서 구역명", 던전이면 "던전명 - 스테이지".
	// 맥락 밖(타이틀 등)에서 숨기는 것은 MainHud 의 맥락 묶음이 맡는다.
	public sealed class HudMapInfo
	{
		private readonly Label _text;

		public HudMapInfo(VisualElement root)
		{
			_text = root.Q<Label>("map-text");

			EventManager.Instance.Subscribe<ZoneChangedEvent>(onZoneChanged);
			EventManager.Instance.Subscribe<DungeonStageStartedEvent>(onDungeonStageStarted);
			EventManager.Instance.Subscribe<GameStateChangedEvent>(onGameStateChanged);
			refreshCurrent();
		}

		public void Dispose()
		{
			EventManager.Instance.Unsubscribe<ZoneChangedEvent>(onZoneChanged);
			EventManager.Instance.Unsubscribe<DungeonStageStartedEvent>(onDungeonStageStarted);
			EventManager.Instance.Unsubscribe<GameStateChangedEvent>(onGameStateChanged);
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
				return;
			}

			showTown();
		}

		// 필드·던전 이름은 들어간 뒤 오는 이벤트가 채운다. 마을은 따로 알림이 없어 상태 전환으로 채운다.
		private void onGameStateChanged(GameStateChangedEvent e)
		{
			if (HudContexts.FromState(e.StateType) == HudContext.Town)
			{
				showTown();
			}
		}

		private void showTown()
		{
			// 마을 맵 ID 는 TownDirector 가 Begin 에서 잡는다 — 상태 전환이 그보다 먼저 와서 같은 규칙으로 직접 찾는다.
			int townMapId = ProjectOne.Town.TownDirector.FindTownMapId();
			Table_Map.Row town = Table_Map.Get(townMapId);
			if (town == null)
			{
				return;
			}

			_text.text = town.Name;
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
