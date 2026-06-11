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
			public Mesh mesh;         // 빌드한 범위 메시 — 트랜션트(피격자 위치 표시)가 재사용
			public bool needsFacing;  // 부채꼴/직선은 facing 방향으로 회전
			public float showTime;    // 표시 시작 예정 시각 (발동 + MotionEffectTime)
			public float hideTime;    // 숨김 예정 시각 (showTime + _displayDuration)
			public bool active;       // 처리 중(표시 대기 또는 표시)
			public bool visible;      // 메시가 실제 보이는 중
			public bool refreshPending; // 다음 showTime 도달 시 위치/방향을 다시 갱신해야 함
			public Transform fillTr;    // 캐스팅 채움용 자식(없으면 null = 비캐스팅 스킬)
			public bool isCasting;      // 캐스팅 스킬 여부 — 표시/종료를 IsCasting 폴링으로 제어
			public float castDuration;  // CastingParam(초) — 채움이 0→풀 크기로 도달하는 시간
			public float castStartTime; // 캐스팅(표시) 시작 시각
		}

		// 임의 월드 위치에 잠깐 띄우는 범위 표시 — OnHitTarget 프록이 피격자마다 동시에 띄울 수 있게 풀로 재사용.
		private sealed class Transient
		{
			public Transform tr;
			public MeshFilter mf;
			public float hideTime;
			public bool active;
		}

		[SerializeField] private float _displayDuration = 0.1f;
		[SerializeField] private Color _color = new Color(1f, 0.3f, 0.3f, 0.5f);
		[SerializeField] private Color _fillColor = new Color(1f, 0.3f, 0.3f, 0.8f);  // 캐스팅 채움 — 범위보다 진한 알파
		[SerializeField] private string _sortingLayerName = "Shadow";  // Floor 타일맵 위, 캐릭터·벽(GamePlay) 아래
		[SerializeField] private int _sortingOrder = -1;
		[SerializeField] private float _ringThickness = 0.05f;
		[SerializeField] private int _segments = 32;

		private UnitBase _owner;
		private UnitMover _mover;
		private Material _material;
		private Material _fillMaterial;
		private readonly List<Item> _items = new List<Item>(8);
		private readonly Dictionary<SkillInfo, Item> _byId = new Dictionary<SkillInfo, Item>();
		private int _activeCount;
		private readonly List<Transient> _transients = new List<Transient>(8);
		private int _activeTransients;

		private void Awake()
		{
			_owner = this.GetComponent<UnitBase>();
			_mover = this.GetComponent<UnitMover>();
			// 텍스처 없는 단색 렌더 (URP 2D에서 Sprites/Default 는 흰색 × color)
			_material = new Material(Shader.Find("Sprites/Default"));
			_material.color = _color;
			// 캐스팅 채움용 — 같은 셰이더에 진한 알파
			_fillMaterial = new Material(Shader.Find("Sprites/Default"));
			_fillMaterial.color = _fillColor;
		}

		private void OnEnable()
		{
			EventManager.Instance.Subscribe<SkillCastEvent>(OnSkillCast);
			EventManager.Instance.Subscribe<SkillProcAtTargetEvent>(OnProcAtTarget);
		}

		private void OnDisable()
		{
			EventManager.Instance.Unsubscribe<SkillCastEvent>(OnSkillCast);
			EventManager.Instance.Unsubscribe<SkillProcAtTargetEvent>(OnProcAtTarget);
		}

		private void OnDestroy()
		{
			if (_material != null)
			{
				Destroy(_material);
			}

			if (_fillMaterial != null)
			{
				Destroy(_fillMaterial);
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

				if (_items[i].fillTr != null)
				{
					Destroy(_items[i].fillTr.gameObject);
				}
			}

			_items.Clear();
			_byId.Clear();
			_activeCount = 0;

			for (int i = 0; i < _transients.Count; i++)
			{
				if (_transients[i].tr != null)
				{
					Destroy(_transients[i].tr.gameObject);
				}
			}

			_transients.Clear();
			_activeTransients = 0;
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

			IndicatorMeshBuilder builder = IndicatorMeshBuilder.Get(row.ScanType);
			if (builder == null)
			{
				return;  // None 등 표시 대상 아님
			}

			Mesh mesh = builder.Build(row.ScanParam1, row.ScanParam2, _segments, _ringThickness);

			bool isCasting = (row.CastingType == SkillCastingTypes.Casting);

			Item item = new Item();
			item.id = id;
			item.mesh = mesh;
			item.tr = CreateMeshChild("Indicator_" + id.ToString(), mesh, _material, _sortingOrder);
			item.needsFacing = (row.ScanType == SkillScanType.Sector || row.ScanType == SkillScanType.Line);
			item.showTime = 0f;
			item.hideTime = 0f;
			item.active = false;
			item.visible = false;
			item.refreshPending = false;
			item.isCasting = isCasting;
			item.castDuration = isCasting ? row.CastingParam : 0f;
			// 캐스팅 스킬은 같은 메시를 공유하는 채움 자식을 하나 더 만들어 범위 위에 그린다
			if (isCasting == true)
			{
				item.fillTr = CreateMeshChild("IndicatorFill_" + id.ToString(), mesh, _fillMaterial, _sortingOrder + 1);
			}

			_items.Add(item);
			_byId.Add(id, item);
		}

		// 단색 메시 자식 GameObject 생성 — 2D 불필요 렌더 기능 차단 (VFXOptimizerWindow 와 동일 패턴)
		private Transform CreateMeshChild(string name, Mesh mesh, Material mat, int order)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(this.transform, false);
			MeshFilter mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = mesh;
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = mat;
			mr.sortingLayerName = _sortingLayerName;
			mr.sortingOrder = order;
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = LightProbeUsage.Off;
			mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
			mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			mr.allowOcclusionWhenDynamic = false;
			go.SetActive(false);
			return go.transform;
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

			// 캐스팅 스킬: 시전 시작 즉시 범위+채움을 띄우고, 종료는 IsCasting 폴링으로 처리(hideTime 미사용)
			if (item.isCasting == true)
			{
				item.showTime = Time.time;
				item.castStartTime = Time.time;
				item.refreshPending = true;
				if (item.active == false)
				{
					item.active = true;
					_activeCount++;
				}

				return;
			}

			// 비캐스팅: 효과가 적용되는 시점(발동 + MotionEffectTime)에 맞춰 표시 예약 — 위치/회전은 표시 시점에 갱신
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

		// OnHitTarget 프록 — 피격자 위치/방향에 범위를 트랜션트로 잠시 표시 (여러 피격자면 각각 동시 표시)
		private void OnProcAtTarget(SkillProcAtTargetEvent evt)
		{
			if (evt.Caster != _owner)
			{
				return;
			}

			Item item;
			if (_byId.TryGetValue(evt.SkillId, out item) == false)
			{
				return;  // 메시 없음(미지원 ScanType 등) → 표시 대상 아님
			}

			Transient t = GetTransient();
			t.mf.sharedMesh = item.mesh;
			t.tr.position = new Vector3(evt.Position.x, evt.Position.y, 0f);
			if (item.needsFacing == true)
			{
				float angle = Mathf.Atan2(evt.Facing.y, evt.Facing.x) * Mathf.Rad2Deg;
				t.tr.rotation = Quaternion.Euler(0f, 0f, angle);
			}
			else
			{
				t.tr.rotation = Quaternion.identity;
			}

			if (t.active == false)
			{
				t.tr.gameObject.SetActive(true);
				t.active = true;
				_activeTransients++;
			}

			t.hideTime = Time.time + _displayDuration;
		}

		// 비활성 트랜션트 재사용, 없으면 신규 생성
		private Transient GetTransient()
		{
			for (int i = 0; i < _transients.Count; i++)
			{
				if (_transients[i].active == false)
				{
					return _transients[i];
				}
			}

			Transient t = new Transient();
			t.tr = CreateMeshChild("IndicatorProc_" + _transients.Count.ToString(), null, _material, _sortingOrder);
			t.mf = t.tr.GetComponent<MeshFilter>();
			t.active = false;
			_transients.Add(t);
			return t;
		}

		private void Update()
		{
			// 활성 항목이 없으면 매 프레임 비용 거의 0
			if (_activeCount == 0 && _activeTransients == 0)
			{
				return;
			}

			// 사망 시 표시 중인 인디케이터를 즉시 전부 숨김 (캐스팅 중 사망 시 IsCasting 잔류로 채움이 계속되는 것도 차단)
			if (_owner != null && _owner.IsDead == true)
			{
				HideAllActive();
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

				// 캐스팅 스킬은 별도 경로 — 시작 즉시 표시, 채움 스케일링, IsCasting 종료 시 숨김
				if (item.isCasting == true)
				{
					UpdateCastingItem(item, now);
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

			// 트랜션트(피격자 위치 표시) 만료 처리
			for (int i = 0; i < _transients.Count; i++)
			{
				Transient t = _transients[i];
				if (t.active == true && now >= t.hideTime)
				{
					t.tr.gameObject.SetActive(false);
					t.active = false;
					_activeTransients--;
				}
			}
		}

		// 표시 중인 모든 항목을 즉시 숨기고 활성 상태를 해제한다 (사망 등).
		private void HideAllActive()
		{
			for (int i = 0; i < _items.Count; i++)
			{
				Item item = _items[i];
				if (item.active == false)
				{
					continue;
				}

				if (item.visible == true)
				{
					item.tr.gameObject.SetActive(false);
					if (item.fillTr != null)
					{
						item.fillTr.gameObject.SetActive(false);
					}

					item.visible = false;
				}

				item.active = false;
				item.refreshPending = false;
			}

			_activeCount = 0;

			for (int i = 0; i < _transients.Count; i++)
			{
				Transient t = _transients[i];
				if (t.active == true)
				{
					t.tr.gameObject.SetActive(false);
					t.active = false;
				}
			}

			_activeTransients = 0;
		}

		// 캐스팅 항목 처리 — 표시 시작/채움 스케일링/종료(IsCasting 폴링)
		private void UpdateCastingItem(Item item, float now)
		{
			// 표시 시작 — 범위/채움을 현재 중심·방향으로 정렬 후 출력, 채움은 크기 0에서 시작
			if (item.refreshPending == true)
			{
				UpdateItemTransform(item);
				if (item.visible == false)
				{
					item.tr.gameObject.SetActive(true);
					item.fillTr.gameObject.SetActive(true);
					item.visible = true;
				}

				item.fillTr.localScale = Vector3.zero;
				item.refreshPending = false;
			}

			// 자기 스킬이 현재 캐스팅 중일 때만 표시 유지 — 다른 스킬 캐스팅 중 이전 항목이 잔류하는 것 방지
			var sc = _owner.SkillContainer;
			if (sc == null || sc.IsCasting == false || sc.CastingSkillId != item.id)
			{
				if (item.visible == true)
				{
					item.tr.gameObject.SetActive(false);
					item.fillTr.gameObject.SetActive(false);
					item.visible = false;
				}

				item.active = false;
				_activeCount--;
				return;
			}

			// 진행 중 — 채움을 0→풀 크기로 스케일링 (CastingParam 이 0이면 즉시 가득)
			float t = (item.castDuration > 0f) ? Mathf.Clamp01((now - item.castStartTime) / item.castDuration) : 1f;
			// 방향성 스킬(Line/Sector)은 폭 고정, 길이(X)만 성장 — 원형/도넛은 균등 성장
			if (item.needsFacing == true)
			{
				item.fillTr.localScale = new Vector3(t, 1f, 1f);
			}
			else
			{
				item.fillTr.localScale = new Vector3(t, t, 1f);
			}
		}

		// 인디케이터 표시 시작 시점의 캐스터 중심/방향으로 자식 위치·회전을 갱신
		private void UpdateItemTransform(Item item)
		{
			Vector2 center = _owner.HitCenter;
			Vector3 pos = new Vector3(center.x, center.y, 0f);
			item.tr.position = pos;
			if (item.fillTr != null)
			{
				item.fillTr.position = pos;
			}

			if (item.needsFacing == true)
			{
				Vector2 facing = GetFacing();
				float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
				Quaternion rot = Quaternion.Euler(0f, 0f, angle);
				item.tr.rotation = rot;
				if (item.fillTr != null)
				{
					item.fillTr.rotation = rot;
				}
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
	}
}
