using System.Collections.Generic;

namespace ProjectOne.UI
{
	// UIManager.ShowPopupAsync 로 여는 UI Toolkit 팝업 목록 (UI구조_설계.md 4.1).
	// 같은 ID 의 팝업을 다시 열면 앞의 것은 닫히고, 다른 ID 의 팝업은 위에 겹친다.
	public enum UIPopupId
	{
		None = 0,

		Common
	}

	// UIPopupId → Addressable 주소. UIScreenCatalog 와 같은 방식 — 주소는 이 표 한 곳이 소유한다.
	public static class UIPopupCatalog
	{
		private static readonly Dictionary<UIPopupId, string> _addresses = new Dictionary<UIPopupId, string>
		{
			{ UIPopupId.Common,	"UIPrefab_CommonPopupToolkit" }
		};

		// 주소를 못 찾으면 빈 문자열. 호출자(UIManager)가 에러를 낸다.
		public static string GetAddress(UIPopupId id)
		{
			string address;
			if (_addresses.TryGetValue(id, out address) == true)
			{
				return address;
			}

			return string.Empty;
		}
	}
}
