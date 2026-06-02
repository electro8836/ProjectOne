using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectOne.Editor
{
	// Project 창에서 선택한 FX 프리팹들을 2D 탑뷰에 맞게 일괄 최적화하는 에디터 툴.
	// - 렌더러: 그림자/프로브/모션벡터/오클루전을 끔 (2D 에선 전부 불필요한 3D 부하)
	// - 파티클: maxParticles 상한을 적용해 과도한 버퍼 선점 방지
	// - 머티리얼: 레거시 빌트인 파티클 셰이더를 URP/Particles/Unlit 로 교체 (SRP 배칭 편입)
	public class VFXOptimizerWindow : EditorWindow
	{
		// 적용 항목 토글
		private bool _optimizeRenderers = true;
		private bool _clampMaxParticles = true;
		private bool _convertShaders = true;

		// maxParticles 상한 — 이보다 큰 값만 끌어내리고, 작은 값은 그대로 둠
		private int _maxParticlesCap = 50;

		[MenuItem("Tools/VFX/2D 최적화 적용")]
		public static void Open()
		{
			VFXOptimizerWindow window = GetWindow<VFXOptimizerWindow>("VFX 2D 최적화");
			window.minSize = new Vector2(340f, 220f);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("선택한 프리팹을 2D 탑뷰에 맞게 일괄 최적화", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			_optimizeRenderers = EditorGUILayout.ToggleLeft("렌더러 최적화 (그림자·프로브·모션벡터·오클루전 끄기)", _optimizeRenderers);
			_clampMaxParticles = EditorGUILayout.ToggleLeft("Max Particles 상한 적용", _clampMaxParticles);
			using (new EditorGUI.DisabledScope(_clampMaxParticles == false))
			{
				_maxParticlesCap = EditorGUILayout.IntField("Max Particles 상한", Mathf.Max(1, _maxParticlesCap));
			}

			_convertShaders = EditorGUILayout.ToggleLeft("레거시 파티클 셰이더 → URP/Particles/Unlit 교체", _convertShaders);

			EditorGUILayout.Space();
			int selectedCount = countSelectedPrefabs();
			EditorGUILayout.LabelField("선택된 프리팹", selectedCount + " 개");
			EditorGUILayout.Space();

			using (new EditorGUI.DisabledScope(selectedCount == 0))
			{
				if (GUILayout.Button("선택한 프리팹에 적용", GUILayout.Height(30f)))
				{
					apply();
				}
			}

			if (_convertShaders == true)
			{
				EditorGUILayout.HelpBox("셰이더 교체는 공유 머티리얼(ThirdParty 포함)을 직접 수정합니다. 적용 후 외형을 육안 확인하세요.", MessageType.Warning);
			}

			if (selectedCount == 0)
			{
				EditorGUILayout.HelpBox("Project 창에서 최적화할 FX 프리팹을 선택하세요.", MessageType.Info);
			}
		}

		// 선택 변경 시 개수 라벨이 즉시 갱신되도록 다시 그림
		private void OnSelectionChange()
		{
			Repaint();
		}

		// ── 적용 ──────────────────────────────────────────────────────

		private void apply()
		{
			GameObject[] selected = Selection.gameObjects;
			int prefabCount = 0;
			int rendererCount = 0;
			int psCount = 0;

			// 같은 공유 머티리얼을 여러 렌더러가 참조해도 한 번만 변환
			HashSet<Material> convertedMaterials = new HashSet<Material>();

			for (int i = 0; i < selected.Length; i++)
			{
				string path = AssetDatabase.GetAssetPath(selected[i]);
				if (isPrefabAssetPath(path) == false)
				{
					continue;
				}

				GameObject root = PrefabUtility.LoadPrefabContents(path);
				try
				{
					ParticleSystemRenderer[] renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
					for (int r = 0; r < renderers.Length; r++)
					{
						ParticleSystemRenderer renderer = renderers[r];
						if (_optimizeRenderers == true)
						{
							optimizeRenderer(renderer);
						}

						if (_convertShaders == true)
						{
							convertMaterials(renderer, convertedMaterials);
						}

						rendererCount++;
					}

					if (_clampMaxParticles == true)
					{
						ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
						for (int s = 0; s < systems.Length; s++)
						{
							clampMaxParticles(systems[s]);
							psCount++;
						}
					}

					PrefabUtility.SaveAsPrefabAsset(root, path);
					prefabCount++;
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			AssetDatabase.SaveAssets();
			EditorUtility.DisplayDialog("완료",
				$"프리팹 {prefabCount}개 / 렌더러 {rendererCount}개 / 파티클 {psCount}개 처리, 머티리얼 {convertedMaterials.Count}개 변환 완료.", "확인");
		}

		// ── 개별 처리 ──────────────────────────────────────────────────

		// 2D 에 불필요한 3D 렌더링 기능을 끈다.
		private static void optimizeRenderer(ParticleSystemRenderer renderer)
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = LightProbeUsage.Off;
			renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			renderer.allowOcclusionWhenDynamic = false;
		}

		// maxParticles 가 상한보다 크면 끌어내린다 (작은 값은 보존).
		private void clampMaxParticles(ParticleSystem ps)
		{
			ParticleSystem.MainModule main = ps.main;
			if (main.maxParticles > _maxParticlesCap)
			{
				main.maxParticles = _maxParticlesCap;
			}
		}

		// 렌더러가 참조하는 공유 머티리얼 중 레거시 파티클 셰이더를 URP/Particles/Unlit 로 교체.
		private static void convertMaterials(ParticleSystemRenderer renderer, HashSet<Material> converted)
		{
			Material[] materials = renderer.sharedMaterials;
			for (int m = 0; m < materials.Length; m++)
			{
				Material mat = materials[m];
				if (mat == null || converted.Contains(mat) == true)
				{
					continue;
				}

				if (isLegacyParticleShader(mat.shader) == false)
				{
					continue;
				}

				if (convertToUrpParticle(mat) == true)
				{
					converted.Add(mat);
				}
			}
		}

		// 레거시/빌트인 파티클 셰이더인지 판별 (Particles/Additive, Legacy Shaders/..., Mobile/Particles/... 등)
		private static bool isLegacyParticleShader(Shader shader)
		{
			if (shader == null)
			{
				return false;
			}

			string name = shader.name;
			if (name.StartsWith("Universal Render Pipeline/") == true)
			{
				return false;
			}

			return name.Contains("Particles/") == true || name.Contains("Particle/") == true;
		}

		// URP/Particles/Unlit 로 교체하고 가산/알파 블렌딩을 원본 셰이더 이름으로 추정해 설정.
		private static bool convertToUrpParticle(Material mat)
		{
			Shader urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
			if (urp == null)
			{
				Debug.LogWarning("[VFXOptimizer] URP/Particles/Unlit 셰이더를 찾을 수 없습니다. URP 패키지를 확인하세요.");
				return false;
			}

			// 교체 전 원본에서 텍스처/블렌딩 단서를 읽어 둔다
			bool additive = mat.shader.name.Contains("Additive") == true;
			Texture mainTex = mat.HasProperty("_MainTex") == true ? mat.GetTexture("_MainTex") : null;

			mat.shader = urp;

			if (mainTex != null && mat.HasProperty("_BaseMap") == true)
			{
				mat.SetTexture("_BaseMap", mainTex);
			}

			// Transparent 표면 + 가산(soft additive) 또는 일반 알파 블렌딩
			mat.SetFloat("_Surface", 1f);            // 0:Opaque 1:Transparent
			mat.SetFloat("_ZWrite", 0f);
			mat.SetFloat("_AlphaClip", 0f);
			mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

			if (additive == true)
			{
				mat.SetFloat("_Blend", 2f);          // 2:Additive
				mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
				mat.SetFloat("_DstBlend", (float)BlendMode.One);
			}
			else
			{
				mat.SetFloat("_Blend", 0f);          // 0:Alpha
				mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
				mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
			}

			mat.renderQueue = (int)RenderQueue.Transparent;
			EditorUtility.SetDirty(mat);
			return true;
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static int countSelectedPrefabs()
		{
			GameObject[] selected = Selection.gameObjects;
			int count = 0;
			for (int i = 0; i < selected.Length; i++)
			{
				if (isPrefabAssetPath(AssetDatabase.GetAssetPath(selected[i])) == true)
				{
					count++;
				}
			}

			return count;
		}

		private static bool isPrefabAssetPath(string path)
		{
			if (string.IsNullOrEmpty(path) == true)
			{
				return false;
			}

			return Path.GetExtension(path).ToLower() == ".prefab";
		}
	}
}
