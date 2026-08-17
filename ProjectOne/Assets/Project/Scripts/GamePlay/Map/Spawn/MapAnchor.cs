using UnityEngine;

namespace ProjectOne.Map
{
	// 맵의 히어로 시작 / 부활 지점 — 맵당 하나.
	//
	// 좌표를 Map 테이블에 두지 않는 이유는 지형이 바뀔 때마다 어긋나기 때문이다 (맵 설계 7.1).
	// 맵당 하나뿐이라 테이블 행조차 필요 없다.
	//
	// 마커가 없는 맵은 그리드 중심으로 폴백한다 — 존재 검증은 빌드 전 씬 순회 툴이 한다(STEP 16).
	public class MapAnchor : MonoBehaviour
	{
		public Vector3 Position
		{
			get { return this.transform.position; }
		}
	}
}
