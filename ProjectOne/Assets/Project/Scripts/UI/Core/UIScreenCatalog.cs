using System.Collections.Generic;
using UnityEngine;

namespace ProjectOne.UI
{
	// 게임이 열 수 있는 화면 목록.
	//
	// 주소 문자열을 화면마다 const 로 흩어 두면 "NPC 로 여는 상점"과 "메뉴로 여는 상점"이
	// 서로 다른 코드가 된다. 하나만 고치면 다른 쪽이 조용히 어긋난다.
	// 여는 쪽은 전부 이 ID 만 지목하고, 주소는 카탈로그 한 곳이 소유한다.
	public enum UIScreenId
	{
		None = 0,

		Shop,
		Equipment,
		World,
		Mastery,
		Quest,
		Settings,
		FieldSelect,
		DungeonSelect,
		Craft,
		Dialog
	}

	// UIScreenId → Addressable 주소.
	//
	// 테이블이 아니라 코드 상수인 이유 — UI 구성은 기획 데이터가 아니라 클라 구조다.
	// 화면을 추가하면 enum 과 이 표를 함께 늘린다(둘이 붙어 있어 빠뜨리기 어렵다).
	public static class UIScreenCatalog
	{
		private static readonly Dictionary<UIScreenId, string> _addresses = new Dictionary<UIScreenId, string>
		{
			{ UIScreenId.Shop,			"UIPrefab_Shop" },
			{ UIScreenId.Equipment,		"UIPrefab_Equipment" },
			{ UIScreenId.World,			"UIPrefab_World" },
			{ UIScreenId.Mastery,		"UIPrefab_MasteryTrait" },
			{ UIScreenId.Quest,			"UIPrefab_Quest" },
			{ UIScreenId.Craft,			"UIPrefab_Craft" },
			{ UIScreenId.Settings,		"UIPrefab_Settings" },
			{ UIScreenId.Dialog,		"UIPrefab_Dialog" }
		};

		// 주소를 못 찾으면 빈 문자열. 호출자(UIManager)가 경고를 낸다.
		public static string GetAddress(UIScreenId id)
		{
			string address;
			if (_addresses.TryGetValue(id, out address) == true)
			{
				return address;
			}

			return string.Empty;
		}

		// NPC 기능 → 화면. NpcType 은 상호작용 우선순위 4단계의 마지막에서만 쓰인다 (퀘스트 설계 5.5).
		public static UIScreenId FromNpcType(EDT.NpcType type)
		{
			switch (type)
			{
				case EDT.NpcType.Shop:
					return UIScreenId.Shop;

				case EDT.NpcType.Blacksmith:
					return UIScreenId.Craft;

				case EDT.NpcType.Portal:
					return UIScreenId.FieldSelect;
			}

			return UIScreenId.None;
		}
	}
}
