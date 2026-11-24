using UnityEngine;
using ProjectOne.Event;

namespace ProjectOne.UI
{
	// 지도 설정 항목. 앞 셋은 시스템 설정, 나머지는 지도 아이콘 종류별 표시.
	public enum MapSetting
	{
		MinimapEnabled,		// HUD 에 미니맵을 띄운다
		MinimapText,		// 미니맵 안 아이콘 이름 글자
		TeleportButton,		// 순간이동 버튼 — 버튼이 아직 없어 값만 저장한다
		Chest,				// 보물상자 — 지도에 표시할 보물상자가 아직 없다
		Boss,
		Altar,				// 제단 — 지도에 표시할 제단이 아직 없다
		Quest,				// 퀘스트 — 지도에 표시할 퀘스트 표시가 아직 없다
		Npc,
		Portal
	}

	// 지도 설정 — 기기 로컬(PlayerPrefs)에 저장한다. 처음에는 모두 켜져 있다.
	// 바꾸면 MapSettingChangedEvent 를 내 미니맵·지도 화면이 다시 적용한다.
	public static class MapSettings
	{
		private const string KeyPrefix = "MapSetting_";

		public static bool Get(MapSetting setting)
		{
			return PlayerPrefs.GetInt(KeyPrefix + setting.ToString(), 1) == 1;
		}

		public static void Set(MapSetting setting, bool on)
		{
			if (Get(setting) == on)
			{
				return;
			}

			PlayerPrefs.SetInt(KeyPrefix + setting.ToString(), on ? 1 : 0);
			PlayerPrefs.Save();
			EventManager.Instance.Publish(new MapSettingChangedEvent());
		}
	}
}
