using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Map
{
	// 스프라이트로 그린 장애물(통·바위·구조물)이 자기 발자국을 차단 영역으로 등록하는 마커.
	//
	// 이 프로젝트의 이동 차단은 전부 TilemapGrid 의 셀 캐시에서 나온다 — Physics2D 는 쓰지 않는다.
	// 원래는 장애물마다 투명 타일을 칠해야 했지만, 이 컴포넌트를 붙이면 TilemapGrid 가 맵 베이크 때
	// 사각형이 덮는 셀을 차단으로 마킹한다. 오브젝트를 옮겨도 차단 영역이 따라온다.
	//
	// 정적 프롭 전용이다. 베이크는 맵 생성 직후 1회뿐이라 런타임에 움직여도 갱신되지 않는다.
	// 체력을 갖거나 부술 수 있어야 하는 것(문 등)은 이걸로 만들 수 없다 — 유닛(UnitBase)으로 간다.
	// TargetResolver 가 UnitBase 만 훑기 때문에, 유닛이 아니면 히어로 자동전투가 타겟으로 잡지 못한다.
	//
	// 사각형이 셀 격자(0.32)와 어긋나면 경로탐색이 근사가 된다 — TilemapGrid.applyBlockersToPathing 주석 참고.
	// 현재 용도는 마을 건물 같은 정적 프롭이다.
	//
	// 차단과 함께 유닛과 같은 축의 Y 정렬도 맡는다 (ApplySorting).
	// 정렬값은 위치로 정해지는 정적 데이터라 에디터에서 구워 프리팹에 박아 둔다 — 런타임 계산이 없다.
	[ExecuteAlways]
	[RequireComponent(typeof(BoxCollider2D))]
	[RequireComponent(typeof(SortingGroup))]
	public class MapBlocker : MonoBehaviour
	{
		// 유닛과 같은 레이어여야 Y 정렬이 서로 비교된다.
		// SortingGroup 은 그룹 자신의 레이어로 통째 정렬되므로, 자식이 GamePlay 여도 그룹이 Default 면
		// 건물이 Floor 보다도 뒤로 밀려 바닥 타일맵에 가려진다.
		private const string SORTING_LAYER = "GamePlay";

		// 차단 범위. 씬 뷰에서 박스 편집 핸들로 크기를 맞출 수 있어 BoxCollider2D 를 쓴다 —
		// 물리 충돌 용도가 아니라 사각형을 저작하는 수단일 뿐이다.
		[SerializeField] private BoxCollider2D _box;

		// true 면 발사체와 AI 시야까지 막는다 (Tilemap_Wall 과 동급).
		// false 면 유닛 이동만 막고 발사체는 넘어간다 (Tilemap_Pit 과 동급).
		[SerializeField] private bool _blocksProjectile = true;

		// 이 프롭 전체의 앞뒤를 결정한다. 자식 SpriteRenderer 들의 sortingOrder 는 그룹 내부
		// 상대 순서로 남으므로(건물 본체 0 / 지붕 100 같은) 건드리지 않는다.
		// 자식 렌더러가 하나뿐인 프롭에도 그대로 붙인다 — 분기를 없애 경로를 하나로 유지한다.
		[SerializeField] private SortingGroup _sortingGroup;

		// worldY → sortingOrder 변환 정밀도. UnitAnimator._precision 과 반드시 같은 값이어야
		// 유닛과 같은 축에서 비교된다. 바꿀 때는 양쪽을 함께 바꾼다.
		[SerializeField] private float _sortPrecision = 1000f;

		public bool BlocksProjectile
		{
			get { return _blocksProjectile; }
		}

		private void Reset()
		{
			_box = this.GetComponent<BoxCollider2D>();
			_sortingGroup = this.GetComponent<SortingGroup>();
		}

		// 차단 사각형의 바닥(min.y)을 발밑으로 보고 sortingOrder 를 정한다. 값이 실제로 달라졌으면 true.
		// 공식은 UnitAnimator.UpdateSorting 과 동일하다 — 같은 축에서 비교돼야 하기 때문이다.
		//
		// transform.position.y 를 쓰지 않는 이유 — 프롭마다 스프라이트 피벗이 제각각이라
		// (바닥 0.056 / 중앙 0.5 / 좌하단 0,0) 루트 Y 가 발밑을 뜻하지 않는다.
		// 반면 차단 사각형의 바닥은 모든 프롭에서 일관되게 지면과 닿는 선이다.
		//
		// 월드 Y 가 아니라 맵 루트 기준 상대 Y 를 쓴다. 프리팹 루트에 저장된 좌표가 0 이 아니어도
		// (MapManager 가 배치할 때 덮어쓰므로 authored 값은 의미가 없다) 에디터에서 구운 값이
		// 런타임 값과 같아야 하기 때문이다. 맵이 y=0 에 놓이면 상대 Y 가 곧 런타임 월드 Y 다.
		public bool ApplySorting()
		{
			if (_sortingGroup == null || _box == null)
			{
				return false;
			}

			Vector2 min;
			Vector2 max;
			if (GetWorldRect(out min, out max) == false)
			{
				return false;
			}

			float sortY = min.y - mapRootY();

			// Y 가 클수록(위) 뒤로 → 음수
			int order = -Mathf.RoundToInt(sortY * _sortPrecision);
			int layerId = SortingLayer.NameToID(SORTING_LAYER);
			if (_sortingGroup.sortingOrder == order && _sortingGroup.sortingLayerID == layerId)
			{
				return false;
			}

			_sortingGroup.sortingOrder = order;
			_sortingGroup.sortingLayerID = layerId;

			return true;
		}

		// 맵 루트(TilemapGrid 가 붙은 프리팹 루트)의 월드 Y. 맵 밖에 놓인 블로커는 0 을 돌려
		// 월드 Y 를 그대로 쓰게 한다.
		//
		// includeInactive 를 켜는 이유 — 프리팹 편집 컨텍스트(PrefabUtility.LoadPrefabContents)의
		// 오브젝트는 activeInHierarchy 가 false 라, 기본 오버로드를 쓰면 루트를 못 찾고 조용히
		// 월드 Y 로 떨어진다. 그러면 에디터에서 구운 값이 컨텍스트마다 달라진다.
		private float mapRootY()
		{
			TilemapGrid grid = this.GetComponentInParent<TilemapGrid>(true);
			if (grid == null)
			{
				return 0f;
			}

			return grid.transform.position.y;
		}

#if UNITY_EDITOR
		// 에디터에서 위치나 박스를 바꾸면 그 자리에서 다시 굽는다 — 런타임에 계산할 필요가 없도록
		// 프리팹에 값을 박아 둔다. 트랜스폼 이동은 OnValidate 가 잡지 못해 Update 로 받는다.
		//
		// 맵은 y = 0 에 배치된다는 전제다 (MapManager.LoadMapAsync 는 Vector3.zero,
		// 필드는 GetFieldOrigin 이 Act 1 에서 y = 0). Act 2 이상은 y 오프셋이 10000 이라
		// 유닛 sortingOrder 부터 int16 을 넘겨 정렬 자체가 성립하지 않는다 — 별도 문제다.
		private void OnValidate()
		{
			bakeSortingInEditor();
		}

		private void Update()
		{
			if (Application.isPlaying == true)
			{
				return;
			}

			bakeSortingInEditor();
		}

		private void bakeSortingInEditor()
		{
			if (ApplySorting() == false)
			{
				return;
			}

			UnityEditor.EditorUtility.SetDirty(_sortingGroup);
		}
#endif

		// 차단 사각형의 월드 AABB. 회전은 지원하지 않는다 — 프롭은 전부 무회전이다.
		//
		// Collider2D.bounds 를 쓰지 않는 이유 — 베이크가 맵 인스턴스화 직후에 일어나
		// Physics2D 동기화가 끝났다는 보장이 없다. 트랜스폼에서 직접 계산하면 타이밍과 무관하다.
		public bool GetWorldRect(out Vector2 min, out Vector2 max)
		{
			min = Vector2.zero;
			max = Vector2.zero;

			if (_box == null)
			{
				Debug.LogError($"[MapBlocker] BoxCollider2D 가 연결되지 않았습니다 — {this.name}");
				return false;
			}

			Vector3 center = this.transform.TransformPoint(new Vector3(_box.offset.x, _box.offset.y, 0f));
			Vector3 scale  = this.transform.lossyScale;
			Vector2 half   = new Vector2(Mathf.Abs(_box.size.x * scale.x), Mathf.Abs(_box.size.y * scale.y)) * 0.5f;

			min = new Vector2(center.x - half.x, center.y - half.y);
			max = new Vector2(center.x + half.x, center.y + half.y);

			return true;
		}
	}
}
