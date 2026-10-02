using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Map;

namespace ProjectOne.Dungeon
{
	// 유적 던전 맵의 기믹 묶음 — 맵 프리팹 루트(TilemapGrid 와 같은 오브젝트)에 붙인다.
	//
	// 코어 자리·공격 영역은 인스펙터로 직접 연결하고,
	// 개수가 맵마다 다른 공격 지점은 자식에서 모은다. 시작 지점은 기존 MapAnchor(Entry)를 쓴다.
	// 코어(보스)를 파괴하면 곧바로 클리어다 — 보상방·상자·포탈은 없다.
	//
	// 스폰 그룹 규약 (RuinsDungeon.MonsterSpawnGroupIDs)
	//   [0] 코어      → _coreSlot
	//   [1] 방어장치  → _defenseSlots
	//   [2~] 페이즈 소환 → _summonSlots (RuinsCorePhase.SpawnGroupIndex 로 지목)
	public class RuinsMap : MonoBehaviour
	{
		[SerializeField] private DungeonSpawnSlot _coreSlot;
		[SerializeField] private DungeonSpawnSlot[] _defenseSlots;
		[SerializeField] private DungeonSpawnSlot[] _summonSlots;

		[Tooltip("방어 시스템 공격 영역(전투방). 가로줄 공격이 이 높이를 등분한다")]
		[SerializeField] private BoxCollider2D _hazardArea;

		private RuinsHazardPoint[] _points;
		private MapAnchor _entry;

		public DungeonSpawnSlot CoreSlot
		{
			get { return _coreSlot; }
		}

		public IReadOnlyList<DungeonSpawnSlot> DefenseSlots
		{
			get { return _defenseSlots; }
		}

		public IReadOnlyList<DungeonSpawnSlot> SummonSlots
		{
			get { return _summonSlots; }
		}

		public IReadOnlyList<RuinsHazardPoint> HazardPoints
		{
			get
			{
				ensureCollected();
				return _points;
			}
		}

		// 시작 지점. Entry 가 없으면 맵 루트 위치다.
		public Vector3 EntryPosition
		{
			get
			{
				ensureCollected();
				return (_entry != null) ? _entry.Position : this.transform.position;
			}
		}

		public bool GetHazardRect(out Vector2 min, out Vector2 max)
		{
			return BoxArea.GetWorldRect(_hazardArea, out min, out max);
		}

		private void ensureCollected()
		{
			if (_points != null)
			{
				return;
			}

			_points = this.GetComponentsInChildren<RuinsHazardPoint>(true);
			_entry = this.GetComponentInChildren<MapAnchor>(true);
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_hazardArea, new Color(1f, 0.3f, 0.3f));

			if (_coreSlot != null)
			{
				Gizmos.color = Color.red;
				Gizmos.DrawWireSphere(_coreSlot.Position, 0.6f);
			}
		}
#endif
	}
}
