using UnityEngine;

namespace ProjectOne.Map
{
	// 필드 스폰 포인트 — 그리드맵 프리팹에 배치한다.
	//
	// **위치와 구성 모두 씬이 결정한다.** 씬이 테이블(MonsterSpawn.GroupID)을 참조하는 방향이며,
	// 반대로 테이블이 씬의 스폰 포인트를 참조하지 않는다 (설계 6장).
	// 그래야 스폰 포인트를 지우거나 100개를 새로 깔아도 테이블이 한 글자도 바뀌지 않는다.
	//
	// 이름을 키로 쓰지 않는 이유 — 유니티가 복제 시 "(1)" 을 붙이는데, 깨져도 에러 없이
	// 몬스터만 안 나온다 (설계 12장 폐기안).
	public class MonsterSpawnPoint : MonoBehaviour
	{
		[Tooltip("MonsterSpawn.GroupID — 여기 뜰 몬스터 조합")]
		[SerializeField] private int _spawnGroupId;

		[Tooltip("스폰 반경. 0이면 이 자리에 정확히 (설계 7장)")]
		[SerializeField] private float _radius = 1.5f;

		public int SpawnGroupId
		{
			get { return _spawnGroupId; }
		}

		public float Radius
		{
			get { return _radius; }
		}

		public Vector3 Position
		{
			get { return this.transform.position; }
		}

		// 반경 안의 임의 위치. Count 가 2 이상인데 Radius=0 이면 겹치므로 호출자가 최소 간격을 준다.
		public Vector3 RandomPosition()
		{
			if (_radius <= 0f)
			{
				return Position;
			}

			Vector2 offset = Random.insideUnitCircle * _radius;
			return Position + new Vector3(offset.x, offset.y, 0f);
		}

#if UNITY_EDITOR
		// 던전 슬롯과 한눈에 구분되어야 한다.
		private static readonly Color GizmoColor = new Color(0.2f, 0.85f, 0.9f);

		// OnDrawGizmosSelected 를 따로 두지 않는다 — 선택된 오브젝트는 둘 다 불려서
		// 라벨이 같은 자리에 두 번 그려진다. 하나만 두고 안에서 선택 여부를 묻는다.
		private void OnDrawGizmos()
		{
			bool selected = UnityEditor.Selection.Contains(this.gameObject);
			SpawnRadiusGizmo.Draw(this.transform, _radius, GizmoColor, selected, "그룹 " + _spawnGroupId);
		}
#endif
	}
}
