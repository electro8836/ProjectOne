using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using EDT;
using ProjectOne.Event;

namespace ProjectOne.Unit
{
	// 스킬 발동 시 해당 스킬의 범위 형태(원/타겟/부채꼴/직선/도넛)를
	// 짧은 시간 동안만 표시했다가 사라지게 하는 인디케이터.
	// 스킬마다 자식 메시 오브젝트를 미리 만들어 두고 재사용한다.
	// 여러 스킬이 동시에 발동되면 각 항목이 독립적으로 동시 표시된다.
	public class SkillIndicator : MonoBehaviour
	{
		// 스킬별 인디케이터 1개를 표현하는 항목
		private sealed class Item
		{
			public SkillInfo id;
			public Transform tr;
			public bool needsFacing;  // 부채꼴/직선은 facing 방향으로 회전
			public float showTime;    // 표시 시작 예정 시각 (발동 + MotionEffectTime)
			public float hideTime;    // 숨김 예정 시각 (showTime + _displayDuration)
			public bool active;       // 처리 중(표시 대기 또는 표시)
			public bool visible;      // 메시가 실제 보이는 중
			public bool refreshPending; // 다음 showTime 도달 시 위치/방향을 다시 갱신해야 함
		}

		[SerializeField] private float _displayDuration = 0.1f;
		[SerializeField] private Color _color = new Color(1f, 0.3f, 0.3f, 0.5f);
		[SerializeField] private string _sortingLayerName = "Shadow";  // Floor 타일맵 위, 캐릭터·벽(GamePlay) 아래
		[SerializeField] private int _sortingOrder = -1;
		[SerializeField] private float _ringThickness = 0.05f;
		[SerializeField] private int _segments = 32;

		private UnitBase _owner;
		private UnitMover _mover;
		private Material _material;
		private readonly List<Item> _items = new List<Item>(8);
		private readonly Dictionary<SkillInfo, Item> _byId = new Dictionary<SkillInfo, Item>();
		private int _activeCount;

		private void Awake()
		{
			_owner = this.GetComponent<UnitBase>();
			_mover = this.GetComponent<UnitMover>();
			// 텍스처 없는 단색 렌더 (URP 2D에서 Sprites/Default 는 흰색 × color)
			_material = new Material(Shader.Find("Sprites/Default"));
			_material.color = _color;
		}

		private void OnEnable()
		{
			EventManager.Instance.Subscribe<SkillCastEvent>(OnSkillCast);
		}

		private void OnDisable()
		{
			EventManager.Instance.Unsubscribe<SkillCastEvent>(OnSkillCast);
		}

		private void OnDestroy()
		{
			if (_material != null)
			{
				Destroy(_material);
			}
		}

		// 캐릭터 스킬 구성/변경 시 외부에서 호출. 기존 항목을 모두 정리하고
		// 표시 가능한 스킬(비패시브 + 범위형)만 자식 인디케이터를 미리 생성한다.
		public void SetSkills(IReadOnlyList<SkillInfo> skillIds)
		{
			Clear();
			if (skillIds == null)
			{
				return;
			}

			for (int i = 0; i < skillIds.Count; i++)
			{
				TryAddItem(skillIds[i]);
			}
		}

		// 모든 자식 인디케이터를 파괴하고 컬렉션을 비운다. 게임 중 스킬 교체 시 호출.
		public void Clear()
		{
			for (int i = 0; i < _items.Count; i++)
			{
				if (_items[i].tr != null)
				{
					Destroy(_items[i].tr.gameObject);
				}
			}

			_items.Clear();
			_byId.Clear();
			_activeCount = 0;
		}

		// 단일 스킬에 대한 자식 인디케이터를 미리 생성한다 (비패시브 + 범위형만).
		private void TryAddItem(SkillInfo id)
		{
			if (id == SkillInfo.None || _byId.ContainsKey(id) == true)
			{
				return;
			}

			Table_SkillInfo.Row row = Table_SkillInfo.Get(id);
			if (row == null || row.CastingType == SkillCastingTypes.Passive)
			{
				return;
			}

			Mesh mesh = BuildMesh(row.ScanType, row.ScanParam1, row.ScanParam2);
			if (mesh == null)
			{
				return;  // None 등 표시 대상 아님
			}

			GameObject go = new GameObject("Indicator_" + id.ToString());
			go.transform.SetParent(this.transform, false);
			MeshFilter mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = mesh;
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = _material;
			mr.sortingLayerName = _sortingLayerName;
			mr.sortingOrder = _sortingOrder;
			// 2D 불필요 렌더 기능 차단 (VFXOptimizerWindow 와 동일 패턴)
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = LightProbeUsage.Off;
			mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
			mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			mr.allowOcclusionWhenDynamic = false;
			go.SetActive(false);

			Item item = new Item();
			item.id = id;
			item.tr = go.transform;
			item.needsFacing = (row.ScanType == SkillScanType.Sector || row.ScanType == SkillScanType.Line);
			item.showTime = 0f;
			item.hideTime = 0f;
			item.active = false;
			item.visible = false;
			item.refreshPending = false;
			_items.Add(item);
			_byId.Add(id, item);
		}

		private void OnSkillCast(SkillCastEvent evt)
		{
			if (evt.Caster != _owner)
			{
				return;
			}

			Item item;
			if (_byId.TryGetValue(evt.SkillId, out item) == false)
			{
				return;
			}

			// 효과가 적용되는 시점(발동 + MotionEffectTime)에 맞춰 표시 예약 — 위치/회전은 표시 시점에 갱신
			Table_SkillInfo.Row row = Table_SkillInfo.Get(evt.SkillId);
			float delay = (row != null) ? Mathf.Max(0f, row.MotionEffectTime) : 0f;

			item.showTime = Time.time + delay;
			item.hideTime = item.showTime + _displayDuration;
			// 매 시전마다 표시 시점에 현재 방향으로 재정렬하도록 예약 (공속이 빨라 표시가 겹쳐도 갱신됨)
			item.refreshPending = true;
			if (item.active == false)
			{
				item.active = true;
				_activeCount++;
			}
		}

		private void Update()
		{
			// 활성 항목이 없으면 매 프레임 비용 거의 0
			if (_activeCount == 0)
			{
				return;
			}

			float now = Time.time;
			for (int i = 0; i < _items.Count; i++)
			{
				Item item = _items[i];
				if (item.active == false)
				{
					continue;
				}

				// 표시 시작/갱신(MotionEffectTime 경과) — 이 시점의 중심/방향으로 갱신 후 출력.
				// visible 여부와 무관하게 새 시전이 들어올 때마다(refreshPending) 다시 정렬한다.
				if (now >= item.showTime && item.refreshPending == true)
				{
					UpdateItemTransform(item);
					if (item.visible == false)
					{
						item.tr.gameObject.SetActive(true);
						item.visible = true;
					}

					item.refreshPending = false;
				}

				// 표시 종료
				if (now >= item.hideTime)
				{
					if (item.visible == true)
					{
						item.tr.gameObject.SetActive(false);
						item.visible = false;
					}

					item.active = false;
					_activeCount--;
				}
			}
		}

		// 인디케이터 표시 시작 시점의 캐스터 중심/방향으로 자식 위치·회전을 갱신
		private void UpdateItemTransform(Item item)
		{
			Vector2 center = _owner.HitCenter;
			item.tr.position = new Vector3(center.x, center.y, 0f);
			if (item.needsFacing == true)
			{
				Vector2 facing = GetFacing();
				float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
				item.tr.rotation = Quaternion.Euler(0f, 0f, angle);
			}
		}

		// 개발용 색상 재설정 — AddComponent 직후 Awake가 끝난 뒤 호출한다.
		public void SetDevColor(Color color)
		{
			_color = color;
			if (_material != null)
			{
				_material.color = _color;
			}
		}

		private Vector2 GetFacing()
		{
			if (_mover == null)
			{
				return Vector2.right;
			}

			Vector2 facing = _mover.Facing;
			if (facing.sqrMagnitude < 1E-06f)
			{
				return Vector2.right;
			}

			return facing;
		}

		// ScanType 에 맞는 로컬 메시(원점 중심)를 생성한다. None 이면 null.
		// 파라미터 해석은 Scanner / TargetResolver 의 판정과 일치시킨다.
		private Mesh BuildMesh(SkillScanType type, float param1, float param2)
		{
			switch (type)
			{
			case SkillScanType.Circle:
				return BuildFan(param1, 360f, false);
			case SkillScanType.Target:
				return BuildRing(param1 - _ringThickness * 0.5f, param1 + _ringThickness * 0.5f);
			case SkillScanType.Sector:
				return BuildFan(param1, param2, true);
			case SkillScanType.Line:
				return BuildLineMesh(param1, param2);
			case SkillScanType.Donut:
				// Scanner.InDonut 기준: param1 = 외경, param2 = 내경
				return BuildRing(param2, param1);
			default:
				return null;
			}
		}

		// 부채꼴/원 메시. centeredOnX=true 면 +X축 중심으로 ±fullAngle/2,
		// false 면 0~fullAngle 전체. 정점은 로컬 원점 기준.
		private Mesh BuildFan(float radius, float fullAngleDeg, bool centeredOnX)
		{
			int seg = Mathf.Max(3, _segments);
			float startRad = (centeredOnX ? -fullAngleDeg * 0.5f : 0f) * Mathf.Deg2Rad;
			float stepRad = fullAngleDeg * Mathf.Deg2Rad / seg;

			Vector3[] verts = new Vector3[seg + 2];
			verts[0] = Vector3.zero;
			for (int i = 0; i <= seg; i++)
			{
				float a = startRad + stepRad * i;
				verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
			}

			int[] tris = new int[seg * 3];
			for (int i = 0; i < seg; i++)
			{
				tris[i * 3] = 0;
				tris[i * 3 + 1] = i + 1;
				tris[i * 3 + 2] = i + 2;
			}

			return CreateMesh(verts, tris);
		}

		// 도넛/링 메시. innerR~outerR 사이를 채운다. 로컬 원점 기준.
		private Mesh BuildRing(float innerR, float outerR)
		{
			if (innerR < 0f)
			{
				innerR = 0f;
			}

			int seg = Mathf.Max(3, _segments);
			float stepRad = Mathf.PI * 2f / seg;

			Vector3[] verts = new Vector3[(seg + 1) * 2];
			for (int i = 0; i <= seg; i++)
			{
				float a = stepRad * i;
				float cos = Mathf.Cos(a);
				float sin = Mathf.Sin(a);
				verts[i * 2] = new Vector3(cos * innerR, sin * innerR, 0f);
				verts[i * 2 + 1] = new Vector3(cos * outerR, sin * outerR, 0f);
			}

			int[] tris = new int[seg * 6];
			for (int i = 0; i < seg; i++)
			{
				int inner0 = i * 2;
				int outer0 = i * 2 + 1;
				int inner1 = (i + 1) * 2;
				int outer1 = (i + 1) * 2 + 1;
				int t = i * 6;
				tris[t] = inner0;
				tris[t + 1] = outer0;
				tris[t + 2] = outer1;
				tris[t + 3] = inner0;
				tris[t + 4] = outer1;
				tris[t + 5] = inner1;
			}

			return CreateMesh(verts, tris);
		}

		// 직선(사각형) 메시. origin 이 시작점, +X 로 length, 폭 ±width/2. 로컬 원점 기준.
		private Mesh BuildLineMesh(float length, float width)
		{
			float half = width * 0.5f;
			Vector3[] verts = new Vector3[4];
			verts[0] = new Vector3(0f, -half, 0f);
			verts[1] = new Vector3(0f, half, 0f);
			verts[2] = new Vector3(length, half, 0f);
			verts[3] = new Vector3(length, -half, 0f);
			int[] tris = new int[6] { 0, 1, 2, 0, 2, 3 };
			return CreateMesh(verts, tris);
		}

		private Mesh CreateMesh(Vector3[] verts, int[] tris)
		{
			Mesh mesh = new Mesh();
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
