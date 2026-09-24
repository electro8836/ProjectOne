using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.Unit;

namespace ProjectOne.Map
{
	// 필드 맵의 이동 구역 — 히어로가 사각형 안에 들어오면 목적지 필드로 옮긴다.
	//
	// 같은 액트면 순간이동, 다른 액트면 로딩창 후 이동한다(FieldDirector.MoveByPortal 이 분기).
	// 도착 위치는 목적지 맵에서 "이 필드로 돌아오는 포털"의 ArrivalPoint 다 — 짝 ID 없이 targetFieldId 로 맞춘다.
	//
	// 목적지 필드의 ReqQuestID 를 아직 클리어하지 못했으면 잠긴다. 잠긴 동안은 장막(MapBlocker)이
	// 길을 막고 구역 진입도 무시한다. 퀘스트 상태가 바뀔 때마다 다시 평가한다.
	//
	// Physics2D 트리거를 쓰지 않는다 — NpcProximityTrigger 와 같은 이유로 히어로 좌표만 비교한다.
	public class MapPortal : MonoBehaviour
	{
		[Tooltip("이동할 필드 (Table_Field.ID)")]
		[SerializeField] private int _targetFieldId;

		// 이동 구역. 씬 뷰에서 박스 핸들로 크기를 맞추는 편집 수단일 뿐이다 (MapBlocker 와 같은 규약).
		[SerializeField] private BoxCollider2D _area;

		[Tooltip("목적지 필드에서 이 포털로 돌아온 히어로가 설 자리. 구역 밖에 둬야 도착 즉시 되돌아가지 않는다.")]
		[SerializeField] private Transform _arrivalPoint;

		[Tooltip("잠긴 동안 켜지는 장막 (검정 스프라이트 + MapBlocker)")]
		[SerializeField] private GameObject _curtain;

		private bool _isLocked;

		// 구역 안에 있는 동안 켜져 있는 빗장. 벗어났다 다시 들어와야 재발동한다.
		private bool _inside;

		public int TargetFieldId
		{
			get { return _targetFieldId; }
		}

		public Vector3 ArrivalPosition
		{
			get { return _arrivalPoint != null ? _arrivalPoint.position : this.transform.position; }
		}

		private void Reset()
		{
			_area = this.GetComponent<BoxCollider2D>();
		}

		// 맵 인스턴스화 중에 돈다 — 뒤이은 TilemapGrid.InitializeFlowField 가 장막 상태를 그대로 굽는다.
		private void Awake()
		{
			EventManager.Instance.Subscribe<QuestChangeEvent>(onQuestChanged);
			applyLock(evaluateLocked());
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<QuestChangeEvent>(onQuestChanged);
		}

		private void Update()
		{
			if (_isLocked == true)
			{
				_inside = false;
				return;
			}

			UnitBase hero = findAliveHero();
			if (hero == null || isInArea(hero.CachedPos) == false)
			{
				_inside = false;
				return;
			}

			if (_inside == true)
			{
				return;
			}

			_inside = true;

			if (FieldDirector.HasInstance == false)
			{
				return;
			}

			FieldDirector.Instance.MoveByPortal(_targetFieldId);
		}

		// ── 잠금 ──────────────────────────────────────────────────────

		// 목적지 필드가 열렸는지는 FieldProgress 가 판정한다 — 액트 목록 팝업과 같은 기준이어야 한다.
		private bool evaluateLocked()
		{
			Table_Field.Row field = Table_Field.Get(_targetFieldId);
			if (field == null)
			{
				Debug.LogError($"[MapPortal] Table_Field.Get({_targetFieldId}) == null — {this.name}");
				return true;
			}

			return FieldProgress.IsCleared(field) == false;
		}

		private void applyLock(bool locked)
		{
			_isLocked = locked;

			if (_curtain != null)
			{
				_curtain.SetActive(locked);
			}
		}

		// 장막이 실제로 바뀐 경우에만 차단 캐시를 다시 굽는다 — 진행도 갱신마다 이벤트가 온다.
		private void onQuestChanged(QuestChangeEvent e)
		{
			bool locked = evaluateLocked();
			if (locked == _isLocked)
			{
				return;
			}

			applyLock(locked);

			TilemapGrid grid = this.GetComponentInParent<TilemapGrid>();
			if (grid != null)
			{
				grid.RefreshBlockers();
			}
		}

		// ── 판정 ──────────────────────────────────────────────────────

		// 구역 사각형의 월드 AABB 안인지. 회전은 지원하지 않는다 (MapBlocker.GetWorldRect 와 같은 계산).
		private bool isInArea(Vector2 pos)
		{
			if (_area == null)
			{
				return false;
			}

			Vector3 center = this.transform.TransformPoint(new Vector3(_area.offset.x, _area.offset.y, 0f));
			Vector3 scale  = this.transform.lossyScale;
			float halfX = Mathf.Abs(_area.size.x * scale.x) * 0.5f;
			float halfY = Mathf.Abs(_area.size.y * scale.y) * 0.5f;

			return pos.x >= center.x - halfX && pos.x <= center.x + halfX
				&& pos.y >= center.y - halfY && pos.y <= center.y + halfY;
		}

		// 살아 있는 히어로 하나. NpcProximityTrigger.findAliveHero 와 같은 조회 방식이다.
		private static UnitBase findAliveHero()
		{
			if (UnitManager.HasInstance == false)
			{
				return null;
			}

			IReadOnlyList<UnitBase> heroes = UnitManager.Instance.GetByType(UnitType.Hero);
			for (int i = 0; i < heroes.Count; i++)
			{
				if (heroes[i] != null && heroes[i].IsDead == false)
				{
					return heroes[i];
				}
			}

			return null;
		}

		// 도착 지점을 구역 안에 두는 실수를 씬 뷰에서 바로 보이게 한다.
		private void OnDrawGizmos()
		{
			if (_area != null)
			{
				Gizmos.color = Color.magenta;
				Vector3 center = this.transform.TransformPoint(new Vector3(_area.offset.x, _area.offset.y, 0f));
				Vector3 scale  = this.transform.lossyScale;
				Gizmos.DrawWireCube(center, new Vector3(Mathf.Abs(_area.size.x * scale.x), Mathf.Abs(_area.size.y * scale.y), 0f));
			}

			if (_arrivalPoint != null)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(_arrivalPoint.position, 0.3f);
			}
		}
	}
}
