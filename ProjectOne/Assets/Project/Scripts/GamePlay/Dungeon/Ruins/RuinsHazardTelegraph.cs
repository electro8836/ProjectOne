using UnityEngine;
using UnityEngine.Rendering;
using ProjectOne.Unit;

namespace ProjectOne.Dungeon
{
	// 유적 방어 시스템 공격의 예고 장판 — 원·도넛·가로 사각형 단색 메시.
	//
	// 발동까지 남은 시간은 채움 대신 알파로 보여준다 — 도넛은 크기를 키우는 채움이 모양과 맞지 않는다.
	// 메시는 SkillIndicator 와 같은 IndicatorMeshBuilder 를 쓴다. RuinsCoreSequencer 가 풀로 재사용한다.
	public sealed class RuinsHazardTelegraph : MonoBehaviour
	{
		// SkillIndicator 와 같은 층 — Floor 타일맵 위, 캐릭터·벽(GamePlay) 아래
		private const string SortingLayerName = "Shadow";
		private const int SortingOrder = -1;
		private const int Segments = 40;

		private static readonly Color StartColor = new Color(1f, 0.25f, 0.2f, 0.15f);
		private static readonly Color EndColor = new Color(1f, 0.25f, 0.2f, 0.6f);

		private MeshFilter _filter;
		private MeshRenderer _renderer;
		private Material _material;

		public static RuinsHazardTelegraph Create(Transform parent)
		{
			GameObject go = new GameObject("RuinsHazardTelegraph");
			go.transform.SetParent(parent, false);
			return go.AddComponent<RuinsHazardTelegraph>();
		}

		private void Awake()
		{
			_filter = this.gameObject.AddComponent<MeshFilter>();
			_renderer = this.gameObject.AddComponent<MeshRenderer>();

			// 텍스처 없는 단색 렌더 (URP 2D 에서 Sprites/Default 는 흰색 × color)
			_material = new Material(Shader.Find("Sprites/Default"));
			_material.color = StartColor;

			_renderer.sharedMaterial = _material;
			_renderer.sortingLayerName = SortingLayerName;
			_renderer.sortingOrder = SortingOrder;
			_renderer.shadowCastingMode = ShadowCastingMode.Off;
			_renderer.receiveShadows = false;
			_renderer.lightProbeUsage = LightProbeUsage.Off;
			_renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			_renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			_renderer.allowOcclusionWhenDynamic = false;
		}

		private void OnDestroy()
		{
			releaseMesh();

			if (_material != null)
			{
				Destroy(_material);
			}
		}

		public void ShowCircle(Vector2 center, float radius)
		{
			show(center, IndicatorMeshBuilder.Get(EDT.SkillScanTypes.Circle).Build(radius, 0f, Segments, 0f));
		}

		public void ShowRing(Vector2 center, float innerRadius, float outerRadius)
		{
			show(center, IndicatorMeshBuilder.BuildRing(innerRadius, outerRadius, Segments));
		}

		// 직선 메시는 원점이 왼쪽 끝이다 — 사각형의 왼쪽 가운데에 놓는다.
		public void ShowRect(Vector2 min, Vector2 max)
		{
			Mesh mesh = IndicatorMeshBuilder.Get(EDT.SkillScanTypes.Line).Build(max.x - min.x, max.y - min.y, Segments, 0f);
			show(new Vector2(min.x, (min.y + max.y) * 0.5f), mesh);
		}

		// 0 = 막 예고됨, 1 = 발동 직전
		public void SetProgress(float t)
		{
			_material.color = Color.Lerp(StartColor, EndColor, Mathf.Clamp01(t));
		}

		public void Hide()
		{
			releaseMesh();
			this.gameObject.SetActive(false);
		}

		private void show(Vector2 pos, Mesh mesh)
		{
			releaseMesh();
			_filter.sharedMesh = mesh;
			this.transform.position = new Vector3(pos.x, pos.y, 0f);
			SetProgress(0f);
			this.gameObject.SetActive(true);
		}

		// 모양·크기가 매번 달라 메시를 그때그때 만든다 — 버리는 메시는 직접 파괴한다.
		private void releaseMesh()
		{
			if (_filter != null && _filter.sharedMesh != null)
			{
				Destroy(_filter.sharedMesh);
				_filter.sharedMesh = null;
			}
		}
	}
}
