using UnityEngine;

namespace ProjectOne.Map
{
	// NPC 배치 마커 — 그리드맵 프리팹에 배치한다 (퀘스트 설계 5.3).
	//
	// 몬스터 스폰 포인트와 참조 방향이 반대다. 몬스터는 씬이 테이블을 지목하지만,
	// NPC 는 **테이블이 씬을 지목한다** — `NpcSpawn` 이 `(MapID, SpawnPointID)` 로 자리를 찾는다.
	// 좌표는 씬 에디터에서 눈으로, 등장 조건은 테이블에서 관리하는 하이브리드다.
	//
	// 마커는 위치만 담당한다. 여러 NPC 가 조건부로 같은 자리를 나눠 쓸 수 있다.
	//
	// 위험 — 씬에서 마커를 지우면 테이블은 멀쩡한데 NPC 만 조용히 안 나온다.
	// NpcSpawner 가 로드 시 존재를 검증하고 경고를 낸다.
	public class NpcSpawnPoint : MonoBehaviour
	{
		[Tooltip("NpcSpawn.SpawnPointID — 맵 안에서만 유니크하면 된다")]
		[SerializeField] private int _spawnPointId;

		public int SpawnPointId
		{
			get { return _spawnPointId; }
		}

		public Vector3 Position
		{
			get { return this.transform.position; }
		}
	}
}
