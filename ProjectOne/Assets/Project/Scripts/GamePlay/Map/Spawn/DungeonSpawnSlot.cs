using UnityEngine;

namespace ProjectOne.Map
{
	// 던전 스폰 슬롯 — 그리드맵 프리팹에 배치한다.
	//
	// **익명 슬롯이다.** 씬은 "여기가 스폰 자리"만 알려주고 뭐가 나올지는 모른다 (설계 7장).
	// 구성은 테이블(DungeonStage.MonsterSpawnGroupIDs)이 정하고 던전 매니저가 슬롯에 배치한다.
	//
	// 필드와 참조 방향이 반대인 이유 — 던전은 씬 하나를 여러 단계가 공유하므로
	// 씬에 구성을 박을 수 없다 (설계 1.3).
	public class DungeonSpawnSlot : MonoBehaviour
	{
		[Tooltip("슬롯 번호. 던전 매니저가 이 순서로 채운다")]
		[SerializeField] private int _slotIndex;

		[Tooltip("스폰 반경. 0이면 이 자리에 정확히")]
		[SerializeField] private float _radius = 1.5f;

		public int SlotIndex
		{
			get { return _slotIndex; }
		}

		public float Radius
		{
			get { return _radius; }
		}

		public Vector3 Position
		{
			get { return this.transform.position; }
		}

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
		// 몬스터 스폰 포인트와 한눈에 구분되어야 한다.
		private static readonly Color GizmoColor = new Color(1f, 0.55f, 0.1f);

		// OnDrawGizmosSelected 를 따로 두지 않는다 — 선택된 오브젝트는 둘 다 불려서
		// 라벨이 같은 자리에 두 번 그려진다. 하나만 두고 안에서 선택 여부를 묻는다.
		private void OnDrawGizmos()
		{
			bool selected = UnityEditor.Selection.Contains(this.gameObject);
			SpawnRadiusGizmo.Draw(this.transform, _radius, GizmoColor, selected, "슬롯 " + _slotIndex);
		}
#endif
	}
}
