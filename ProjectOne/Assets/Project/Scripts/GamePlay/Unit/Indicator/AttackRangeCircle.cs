using UnityEngine;
using UnityEngine.Rendering;
using ProjectOne.Settings;
using ProjectOne.Skill;

namespace ProjectOne.Unit
{
	// 내 캐릭터의 기본공격 사거리를 속이 빈 원(링)으로 상시 표시한다.
	//
	// SkillIndicator 와 다르다 — 저쪽은 여러 스킬을 발동 타이밍에 맞춰 잠깐 띄우는 물건이고,
	// 이건 평타 하나의 반지름만 계속 그린다. 발동 타이밍도 표시/숨김 연출도 없다.
	//
	// 평타의 ScanType 이 부채꼴이든 타겟이든 상관없이 원으로 그린다 — 플레이어에게 필요한 정보는
	// "얼마나 가까이 가야 때리는가" 하나뿐이다.
	public class AttackRangeCircle : MonoBehaviour
	{
		[SerializeField] private Color _color = new Color(1f, 1f, 1f, 0.25f);
		[SerializeField] private string _sortingLayerName = "Shadow";   // Floor 타일맵 위, 캐릭터 아래
		[SerializeField] private int _sortingOrder = -2;
		[SerializeField] private float _ringThickness = 0.05f;
		[SerializeField] private int _segments = 48;
		[Tooltip("사거리를 다시 조회하는 주기(초). 리졸브는 캐시되므로 짧아도 부담이 없다.")]
		[SerializeField] private float _refreshInterval = 0.25f;

		private UnitBase _owner;
		private Material _material;
		private Transform _circleTr;
		private MeshFilter _meshFilter;
		private MeshRenderer _meshRenderer;
		private Mesh _mesh;

		// 마지막으로 그린 반지름. 값이 바뀔 때만 메시를 다시 만든다.
		private float _drawnRadius = -1f;
		private float _refreshTimer;

		private void Awake()
		{
			_owner = this.GetComponent<UnitBase>();
			// 텍스처 없는 단색 렌더 (URP 2D에서 Sprites/Default 는 흰색 × color)
			_material = new Material(Shader.Find("Sprites/Default"));
			_material.color = _color;
			createCircle();
		}

		private void OnEnable()
		{
			SettingsManager.Instance.AttackRangeCircleChanged += onSettingChanged;
			// 켜질 때마다 즉시 한 번 갱신 — 대기 없이 올바른 크기로 보이게 한다.
			_refreshTimer = 0f;
			refresh();
		}

		private void OnDisable()
		{
			SettingsManager.Instance.AttackRangeCircleChanged -= onSettingChanged;
		}

		private void OnDestroy()
		{
			if (_material != null)
			{
				Destroy(_material);
			}

			if (_mesh != null)
			{
				Destroy(_mesh);
			}
		}

		private void Update()
		{
			_refreshTimer -= Time.deltaTime;
			if (_refreshTimer > 0f)
			{
				return;
			}

			_refreshTimer = _refreshInterval;
			refresh();
		}

		private void onSettingChanged(bool enabled)
		{
			refresh();
		}

		// 표시 여부와 반지름을 다시 판단한다. 반지름이 그대로면 메시는 건드리지 않는다.
		private void refresh()
		{
			if (_meshRenderer == null)
			{
				return;
			}

			if (SettingsManager.Instance.ShowAttackRangeCircle == false)
			{
				_meshRenderer.enabled = false;
				return;
			}

			float radius = getBasicAttackRange();
			if (radius <= 0f)
			{
				// 평타가 없다(무기 미장착 등) — 그릴 것이 없다.
				_meshRenderer.enabled = false;
				return;
			}

			_meshRenderer.enabled = true;

			if (Mathf.Approximately(radius, _drawnRadius) == true)
			{
				return;
			}

			rebuildMesh(radius);
		}

		// 리졸브를 거친 사거리 — "사거리 +20%" 같은 모디파이어가 표시에도 반영된다 (설계 11.2).
		// 테이블 원본(Table_Skill.Get)을 직접 읽으면 모디파이어가 빠진다.
		private float getBasicAttackRange()
		{
			if (_owner == null || _owner.SkillContainer == null)
			{
				return 0f;
			}

			EDT.Skill basic = _owner.SkillContainer.GetBasicAttack();
			if (basic == EDT.Skill.None)
			{
				return 0f;
			}

			ResolvedSkill resolved = _owner.Resolve(basic);
			if (resolved == null || resolved.IsValid == false)
			{
				return 0f;
			}

			return resolved.Row.ScanRange;
		}

		private void rebuildMesh(float radius)
		{
			if (_mesh != null)
			{
				Destroy(_mesh);
			}

			// 링 바깥 가장자리를 사거리에 맞춘다 — 원의 테두리가 곧 사거리라는 뜻이 된다.
			_mesh = IndicatorMeshBuilder.BuildRing(radius - _ringThickness, radius, _segments);
			_meshFilter.sharedMesh = _mesh;
			_drawnRadius = radius;

			// 스캔 판정 기준점(HitCenter)에 원 중심을 맞춘다
			Vector2 offset = _owner.HitCenter - (Vector2)this.transform.position;
			_circleTr.localPosition = new Vector3(offset.x, offset.y, 0f);
		}

		// 단색 메시 자식 생성 — 2D 에 불필요한 렌더 기능 차단 (SkillIndicator 와 동일 패턴)
		private void createCircle()
		{
			GameObject go = new GameObject("AttackRangeCircle");
			go.transform.SetParent(this.transform, false);
			_meshFilter = go.AddComponent<MeshFilter>();
			_meshRenderer = go.AddComponent<MeshRenderer>();
			_meshRenderer.sharedMaterial = _material;
			_meshRenderer.sortingLayerName = _sortingLayerName;
			_meshRenderer.sortingOrder = _sortingOrder;
			_meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			_meshRenderer.receiveShadows = false;
			_meshRenderer.lightProbeUsage = LightProbeUsage.Off;
			_meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			_meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			_meshRenderer.allowOcclusionWhenDynamic = false;
			_meshRenderer.enabled = false;
			_circleTr = go.transform;
		}
	}
}
