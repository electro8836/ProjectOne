using UnityEngine;

namespace ProjectOne.Map
{
	// 미니맵·월드맵에 아이콘과 이름으로 표시할 오브젝트 — 맵 프리팹 안의 오브젝트에 붙인다.
	//
	// NPC·필드보스·구역 관문은 맵 데이터에서 자동으로 표시되므로 붙이지 않는다.
	// 그 밖에 지도에 보여야 하는 것(건물, 포탈 등)에만 쓴다. 꺼진 오브젝트는 표시하지 않는다.
	public class MinimapMarker : MonoBehaviour
	{
		[Tooltip("아틀라스 스프라이트 이름 (예: btn_icon_store_1)")]
		[SerializeField] private string _icon;

		[Tooltip("아이콘 옆에 표시할 이름. 비우면 아이콘만")]
		[SerializeField] private string _label;

		public string Icon
		{
			get { return _icon; }
		}

		public string Label
		{
			get { return _label; }
		}

		public Vector3 Position
		{
			get { return this.transform.position; }
		}
	}
}
