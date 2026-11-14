using System.Collections.Generic;
using EDT;
using UnityEngine;
using ProjectOne.Event;
using ProjectOne.Field;
using ProjectOne.UI;
using ProjectOne.Unit;

namespace ProjectOne.Map
{
	// 구역 관문 — 목적지 구역이 열리기 전까지 장막으로 길목을 막는다.
	//
	// 목적지 구역(Table_Zone)의 ReqQuestID 를 클리어하지 못했으면 잠긴다. 잠긴 동안은 장막(MapBlocker)이
	// 길을 막고, 안내 영역(_warningArea)에 들어오면 해금 조건(선행 퀘스트명)을 제한 메시지로 띄운다.
	// 퀘스트 상태가 바뀔 때마다 다시 평가한다.
	//
	// 같은 월드의 구역은 이어져 있으므로 장막만 쓴다. 이동 구역(_area)은 다른 월드로 넘어가는 관문에만 둔다 —
	// 열린 관문의 _area 에 들어오면 로딩 후 목적지 월드의 역방향 관문(_arrivalPoint)에 도착한다.
	//
	// Physics2D 트리거를 쓰지 않는다 — NpcProximityTrigger 와 같은 이유로 히어로 좌표만 비교한다.
	public class ZoneGate : MonoBehaviour
	{
		[Tooltip("이 관문이 여는 구역 (Table_Zone.ID)")]
		[SerializeField] private int _targetZoneId;

		[Tooltip("다른 월드로 넘어가는 이동 구역. 같은 월드 안의 관문이면 비워 둔다.")]
		[SerializeField] private BoxCollider2D _area;

		[Tooltip("다른 월드에서 이 관문으로 돌아온 히어로가 설 자리. 이동 구역 밖에 둬야 도착 즉시 되돌아가지 않는다.")]
		[SerializeField] private Transform _arrivalPoint;

		[Tooltip("잠긴 동안 켜지는 장막 (검정 스프라이트 + MapBlocker)")]
		[SerializeField] private GameObject _curtain;

		[Tooltip("잠긴 동안 히어로가 들어오면 해금 조건을 띄우는 영역. 장막 앞까지 덮도록 장막보다 넓게 둔다.")]
		[SerializeField] private BoxCollider2D _warningArea;

		private bool _isLocked;

		// 해금 조건 안내를 띄운 상태. 바뀔 때만 Show/Hide 한다 — 제한 메시지는 Hide 전까지 떠 있다.
		private bool _warningShown;

		// 이동 구역 안에 있는 동안 켜져 있는 빗장. 벗어났다 다시 들어와야 재발동한다.
		private bool _inside;

		public int TargetZoneId
		{
			get { return _targetZoneId; }
		}

		public Vector3 ArrivalPosition
		{
			get { return _arrivalPoint != null ? _arrivalPoint.position : this.transform.position; }
		}

		// 씬 로드 중에 돈다 — 뒤이은 TilemapGrid.InitializeFlowField 가 장막 상태를 그대로 굽는다.
		private void Awake()
		{
			EventManager.Instance.Subscribe<QuestChangeEvent>(onQuestChanged);
			applyLock(evaluateLocked());
		}

		private void OnDestroy()
		{
			EventManager.Instance.Unsubscribe<QuestChangeEvent>(onQuestChanged);
			setWarning(false);
		}

		private void Update()
		{
			if (_isLocked == true)
			{
				_inside = false;

				UnitBase lockedHero = findAliveHero();
				setWarning(lockedHero != null && BoxArea.Contains(_warningArea, lockedHero.CachedPos) == true);
				return;
			}

			if (_area == null)
			{
				return;
			}

			UnitBase hero = findAliveHero();
			if (hero == null || BoxArea.Contains(_area, hero.CachedPos) == false)
			{
				_inside = false;
				return;
			}

			if (_inside == true)
			{
				return;
			}

			_inside = true;

			if (WorldDirector.HasInstance == false)
			{
				return;
			}

			// 출발 구역은 이 관문의 도착 지점이 놓인 구역이다 — 목적지 월드에서 역방향 관문을 찾는 키가 된다.
			WorldDirector.Instance.MoveByGate(_targetZoneId, MapManager.Instance.GetMapIdAt(ArrivalPosition));
		}

		// ── 잠금 ──────────────────────────────────────────────────────

		// 목적지 구역이 열렸는지는 ZoneProgress 가 판정한다 — 월드 화면과 같은 기준이어야 한다.
		private bool evaluateLocked()
		{
			Table_Zone.Row zone = Table_Zone.Get(_targetZoneId);
			if (zone == null)
			{
				Debug.LogError($"[ZoneGate] Table_Zone.Get({_targetZoneId}) == null — {this.name}");
				return true;
			}

			return ZoneProgress.IsOpen(zone) == false;
		}

		private void applyLock(bool locked)
		{
			_isLocked = locked;

			if (_curtain != null)
			{
				_curtain.SetActive(locked);
			}

			if (locked == false)
			{
				setWarning(false);
			}
		}

		// 해금 조건 안내 — 목적지 구역의 ReqQuestID 퀘스트명을 띄운다.
		private void setWarning(bool show)
		{
			if (_warningShown == show || UIManager.HasInstance == false)
			{
				return;
			}

			_warningShown = show;

			if (show == false)
			{
				UIManager.Instance.HideWarningMessage();
				return;
			}

			string message = buildWarningMessage();
			if (message == null)
			{
				return;
			}

			UIManager.Instance.ShowWarningMessage(message);
		}

		private string buildWarningMessage()
		{
			Table_Zone.Row zone = Table_Zone.Get(_targetZoneId);
			if (zone == null)
			{
				return null;
			}

			Table_Quest.Row quest = Table_Quest.Get(zone.ReqQuestID);
			if (quest == null)
			{
				Debug.LogError($"[ZoneGate] Table_Quest.Get({zone.ReqQuestID}) == null — {this.name}");
				return null;
			}

			return $"\"{quest.Name}\" 퀘스트 완료 후 개방";
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

#if UNITY_EDITOR
		// 도착 지점을 이동 구역 안에 두는 실수를 씬 뷰에서 바로 보이게 한다.
		private void OnDrawGizmos()
		{
			BoxArea.DrawGizmo(_area, Color.magenta);
			BoxArea.DrawGizmo(_warningArea, Color.yellow);

			if (_arrivalPoint != null)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(_arrivalPoint.position, 0.3f);
			}
		}
#endif
	}
}
