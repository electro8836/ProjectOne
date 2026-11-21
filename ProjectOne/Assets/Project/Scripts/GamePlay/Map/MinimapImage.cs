using UnityEngine;

namespace ProjectOne.Map
{
	// 미니맵 지도 이미지 — 맵 프리팹의 TilemapGrid 와 같은 오브젝트에 붙는다.
	//
	// 실시간으로 찍지 않는다. 에디터 메뉴(Tools/Minimap)가 타일맵 셀을 단색으로 칠한 PNG 를 굽고
	// 이 컴포넌트에 이미지와 이미지가 덮는 범위(그리드 기준 로컬 좌표)를 채워 둔다.
	// 지형을 고치면 메뉴를 다시 돌린다.
	//
	// 회전·스케일은 지원하지 않는다 — 맵은 원점에 스케일 1 로 놓인다.
	public class MinimapImage : MonoBehaviour
	{
		[SerializeField] private Texture2D _texture;

		[Tooltip("이미지 왼쪽 아래 — 이 오브젝트 위치 기준")]
		[SerializeField] private Vector2 _localMin;

		[Tooltip("이미지 오른쪽 위 — 이 오브젝트 위치 기준")]
		[SerializeField] private Vector2 _localMax;

		public Texture2D Texture
		{
			get { return _texture; }
		}

		public Vector2 WorldMin
		{
			get { return (Vector2)this.transform.position + _localMin; }
		}

		public Vector2 WorldMax
		{
			get { return (Vector2)this.transform.position + _localMax; }
		}

#if UNITY_EDITOR
		// 굽기 도구 전용
		public void SetBaked(Texture2D texture, Vector2 localMin, Vector2 localMax)
		{
			_texture = texture;
			_localMin = localMin;
			_localMax = localMax;
		}
#endif
	}
}
