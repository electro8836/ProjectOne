using UnityEngine;

namespace ProjectOne.Map
{
	// 씬에 배치하는 맵 담을 그릇. 로드된 그리드맵 인스턴스의 부모다.
	//
	// 매니저(MapManager)와 나누는 이유 — 매니저는 로드 상태·플로우필드·좌표 인덱스를 들고 영속해야 하고,
	// 실제 맵 오브젝트는 씬과 함께 나고 죽어야 한다. 컨테이너가 씬에 있으면 씬 전환이 곧 정리다.
	//
	// 씬에 배치하지 않아도 동작한다 — MapManager 가 자기 자식으로 하나 만들어 쓴다.
	public class MapContainer : MonoBehaviour
	{
		private void Awake()
		{
			MapManager.Instance.RegisterContainer(this);
		}

		private void OnDestroy()
		{
			// 종료 중이면 매니저를 새로 만들지 않는다.
			if (MapManager.HasInstance == true)
			{
				MapManager.Instance.UnregisterContainer(this);
			}
		}
	}
}
