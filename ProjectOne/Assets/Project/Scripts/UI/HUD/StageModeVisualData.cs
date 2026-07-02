using UnityEngine;
using EDT;

namespace ProjectOne.UI
{
	// 스테이지 모드(MapModeType)별 아이콘 주소 매핑. 스테이지 선택 UI 등에서 조회한다.
	// (모드 한글 이름은 향후 별도 유틸로 처리 — 여기선 아이콘 주소만 관리)
	[CreateAssetMenu(menuName = "Custom UI/Stage Mode Visual Data")]
	public class StageModeVisualData : ScriptableObject
	{
		[System.Serializable]
		public struct Entry
		{
			public MapModeType mode;
			public string iconAddress;
		}

		[SerializeField] private Entry[] _entries;

		// 모드에 매핑된 아이콘 주소 (없으면 빈 문자열)
		public string GetIconAddress(MapModeType mode)
		{
			if (_entries == null)
			{
				return string.Empty;
			}

			for (int i = 0; i < _entries.Length; i++)
			{
				if (_entries[i].mode == mode)
				{
					return _entries[i].iconAddress;
				}
			}

			return string.Empty;
		}
	}
}
