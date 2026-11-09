using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ProjectOne.Utils;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectOne.Editor
{
	// pixelfx 스프라이트 시트(PNG + Aseprite 형식 JSON)를 VFXManager 로 재생할 수 있는 프리펩으로 가져온다.
	// - 시트 생성: 저장소의 Tools/pixelfx-unity.mjs (Node) — 창에서 바로 실행하거나 터미널에서 직접 실행
	// - 텍스처: Point·무압축·밉맵 없음, JSON 프레임대로 스프라이트 슬라이스(피벗 = 이펙트 원점)
	// - 프리펩: 루트 VFXItem + 자식 "Sheet" 파티클 1장(Texture Sheet Animation 그리드) — VFXItem 의 파티클 생존 판정을 그대로 쓴다
	// - Prefabs/Effects 아래라 AddressableAutoMarker 가 주소 PixelFx_<name> 으로 자동 등록한다
	public class PixelFxImportWindow : EditorWindow
	{
		private const string SheetRoot = "Assets/Project/Art/VFX/PixelFx";
		private const string PrefabRoot = "Assets/Project/Prefabs/Effects/PixelFx";
		private const string PrefabPrefix = "PixelFx_";
		private const string SheetChildName = "Sheet";
		private const string SortingLayerName = "Effect";
		private const string ShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
		private const int MaxTextureSize = 8192;
		private const int NodeTimeoutMs = 120000;

		private string _effectId = "impacts/hit-spark";
		private string _assetName = "";
		private int _fps;
		private string _optsJson = "";
		private string _pivot = "";
		private bool _randomRotation;
		private float _pixelsPerUnit = 32f;

		[MenuItem("Tools/VFX/PixelFx 시트 가져오기")]
		public static void Open()
		{
			PixelFxImportWindow window = GetWindow<PixelFxImportWindow>("PixelFx");
			window.minSize = new Vector2(420f, 260f);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("pixelfx 렌더 + 가져오기", EditorStyles.boldLabel);
			_effectId = EditorGUILayout.TextField("이펙트 id", _effectId);
			_assetName = EditorGUILayout.TextField("에셋 이름 (비우면 id)", _assetName);
			_fps = EditorGUILayout.IntField("fps (0 = 이펙트 기본)", _fps);
			_optsJson = EditorGUILayout.TextField("옵션 JSON", _optsJson);
			_pivot = EditorGUILayout.TextField("피벗 x,y (비우면 원점)", _pivot);
			_randomRotation = EditorGUILayout.Toggle("재생마다 랜덤 회전", _randomRotation);
			_pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", _pixelsPerUnit);

			EditorGUILayout.Space();
			EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_effectId));
			if (GUILayout.Button("렌더 + 가져오기", GUILayout.Height(30f)))
			{
				renderAndImport();
			}

			EditorGUI.EndDisabledGroup();

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("이미 뽑은 시트", EditorStyles.boldLabel);
			List<string> selected = collectSelectedSheets();
			EditorGUILayout.LabelField("선택된 시트", selected.Count + " 개");
			EditorGUI.BeginDisabledGroup(selected.Count == 0);
			if (GUILayout.Button("선택한 시트 가져오기", GUILayout.Height(24f)))
			{
				for (int i = 0; i < selected.Count; i++)
				{
					ImportSheet(selected[i], _pixelsPerUnit);
				}
			}

			EditorGUI.EndDisabledGroup();
		}

		private void OnSelectionChange()
		{
			Repaint();
		}

		// ── 렌더 ─────────────────────────────────────────────────────

		private void renderAndImport()
		{
			string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
			StringBuilder args = new StringBuilder();
			args.Append(quote("Tools/pixelfx-unity.mjs")).Append(' ').Append(quote(_effectId.Trim()));
			if (!string.IsNullOrWhiteSpace(_assetName))
			{
				args.Append(" --name ").Append(quote(_assetName.Trim()));
			}

			if (_fps > 0)
			{
				args.Append(" --fps ").Append(_fps);
			}

			if (!string.IsNullOrWhiteSpace(_optsJson))
			{
				args.Append(" --opts ").Append(quote(_optsJson.Trim()));
			}

			if (!string.IsNullOrWhiteSpace(_pivot))
			{
				args.Append(" --pivot ").Append(quote(_pivot.Trim()));
			}

			if (_randomRotation)
			{
				args.Append(" --random-rotation");
			}

			ProcessStartInfo info = new ProcessStartInfo("node", args.ToString());
			info.WorkingDirectory = repoRoot;
			info.UseShellExecute = false;
			info.CreateNoWindow = true;
			info.RedirectStandardOutput = true;
			info.RedirectStandardError = true;
			info.StandardOutputEncoding = Encoding.UTF8;
			info.StandardErrorEncoding = Encoding.UTF8;

			string stdout;
			string stderr;
			int exitCode;
			EditorUtility.DisplayProgressBar("PixelFx", "렌더 중: " + _effectId, 0.5f);
			using (Process process = Process.Start(info))
			{
				// 출력 버퍼가 차서 멈추지 않도록 stderr 는 비동기로 받는다
				System.Threading.Tasks.Task<string> errTask = process.StandardError.ReadToEndAsync();
				stdout = process.StandardOutput.ReadToEnd();
				if (!process.WaitForExit(NodeTimeoutMs))
				{
					process.Kill();
				}

				stderr = errTask.Result;
				exitCode = process.HasExited ? process.ExitCode : -1;
			}

			EditorUtility.ClearProgressBar();

			if (exitCode != 0)
			{
				Debug.LogError($"[PixelFx] 렌더 실패 (exit {exitCode})\n{stderr}\n{stdout}");
				return;
			}

			Debug.Log("[PixelFx] " + stdout.Trim());

			string assetName = string.IsNullOrWhiteSpace(_assetName) ? lastSegment(_effectId.Trim()) : _assetName.Trim();
			string jsonPath = $"{SheetRoot}/{assetName}/{assetName}.json";
			AssetDatabase.Refresh();
			ImportSheet(jsonPath, _pixelsPerUnit);
		}

		// Windows 명령줄 규칙으로 인자 하나를 감싼다 — 따옴표는 \" 로, 그 앞의 역슬래시는 두 배로
		private static string quote(string value)
		{
			StringBuilder sb = new StringBuilder("\"");
			int slashes = 0;
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (c == '\\')
				{
					slashes++;
					continue;
				}

				if (c == '"')
				{
					sb.Append('\\', slashes * 2 + 1);
				}
				else
				{
					sb.Append('\\', slashes);
				}

				slashes = 0;
				sb.Append(c);
			}

			sb.Append('\\', slashes * 2);
			sb.Append('"');
			return sb.ToString();
		}

		private static string lastSegment(string id)
		{
			int slash = id.LastIndexOf('/');
			return slash < 0 ? id : id.Substring(slash + 1);
		}

		private static List<string> collectSelectedSheets()
		{
			List<string> result = new List<string>();
			string[] guids = Selection.assetGUIDs;
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (!path.StartsWith(SheetRoot + "/"))
				{
					continue;
				}

				string ext = Path.GetExtension(path).ToLowerInvariant();
				if (ext != ".json" && ext != ".png")
				{
					continue;
				}

				string jsonPath = Path.ChangeExtension(path, ".json").Replace('\\', '/');
				if (!result.Contains(jsonPath) && File.Exists(jsonPath))
				{
					result.Add(jsonPath);
				}
			}

			return result;
		}

		// ── 가져오기 ──────────────────────────────────────────────────

		// jsonPath: Assets/... 경로의 시트 JSON. 같은 이름으로 다시 가져오면 스프라이트 ID·프리펩 GUID 를 유지한 채 갱신한다
		public static void ImportSheet(string jsonPath, float pixelsPerUnit)
		{
			if (!File.Exists(jsonPath))
			{
				Debug.LogError("[PixelFx] 시트 JSON 이 없습니다: " + jsonPath);
				return;
			}

			SheetJson sheet = JsonUtility.FromJson<SheetJson>(File.ReadAllText(jsonPath));
			if (sheet == null || sheet.frames == null || sheet.frames.Length == 0 || sheet.meta == null || sheet.meta.pixelfx == null)
			{
				Debug.LogError("[PixelFx] pixelfx 시트 JSON 형식이 아닙니다: " + jsonPath);
				return;
			}

			string dir = Path.GetDirectoryName(jsonPath).Replace('\\', '/');
			string assetName = Path.GetFileNameWithoutExtension(jsonPath);
			string texturePath = dir + "/" + sheet.meta.image;
			int sheetMax = Mathf.Max(sheet.meta.size.w, sheet.meta.size.h);
			if (sheetMax > MaxTextureSize)
			{
				Debug.LogError($"[PixelFx] 시트가 {MaxTextureSize} 를 넘습니다({sheet.meta.size.w}x{sheet.meta.size.h}). --fps 를 낮추거나 더 작은 이펙트로 다시 뽑으세요: {jsonPath}");
				return;
			}

			AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
			if (!setupTexture(texturePath, assetName, sheet, pixelsPerUnit, sheetMax))
			{
				return;
			}

			Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
			Material material = setupMaterial(dir + "/" + assetName + ".mat", texture);
			string prefabPath = buildPrefab(assetName, sheet, material, pixelsPerUnit);

			Debug.Log($"[PixelFx] 가져오기 완료: {prefabPath} (주소 {PrefabPrefix}{assetName}, {sheet.frames.Length}프레임 @{sheet.meta.pixelfx.fps}fps)");
		}

		private static bool setupTexture(string texturePath, string assetName, SheetJson sheet, float pixelsPerUnit, int sheetMax)
		{
			TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
			if (importer == null)
			{
				Debug.LogError("[PixelFx] 시트 PNG 가 없습니다: " + texturePath);
				return false;
			}

			importer.textureType = TextureImporterType.Sprite;
			importer.spriteImportMode = SpriteImportMode.Multiple;
			importer.spritePixelsPerUnit = pixelsPerUnit;
			importer.filterMode = FilterMode.Point;
			importer.textureCompression = TextureImporterCompression.Uncompressed;
			importer.mipmapEnabled = false;
			importer.alphaIsTransparency = true;
			importer.wrapMode = TextureWrapMode.Clamp;
			importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(sheetMax));

			SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
			factories.Init();
			ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
			if (provider == null)
			{
				Debug.LogError("[PixelFx] 스프라이트 데이터 제공자를 얻지 못했습니다: " + texturePath);
				return false;
			}

			provider.InitSpriteEditorDataProvider();

			// 다시 가져올 때 같은 이름의 스프라이트 ID 를 유지 — 이 스프라이트를 참조하는 애니메이션이 끊기지 않게
			SpriteRect[] oldRects = provider.GetSpriteRects();
			Dictionary<string, GUID> oldIds = new Dictionary<string, GUID>();
			for (int i = 0; i < oldRects.Length; i++)
			{
				oldIds[oldRects[i].name] = oldRects[i].spriteID;
			}

			PixelFxMeta fx = sheet.meta.pixelfx;
			int textureHeight = sheet.meta.size.h;
			SpriteRect[] rects = new SpriteRect[sheet.frames.Length];
			List<SpriteNameFileIdPair> pairs = new List<SpriteNameFileIdPair>(rects.Length);
			for (int i = 0; i < rects.Length; i++)
			{
				FrameRect f = sheet.frames[i].frame;
				SpriteRect rect = new SpriteRect();
				rect.name = $"{assetName}_{i}";
				// JSON 은 좌상단 원점, 텍스처는 좌하단 원점
				rect.rect = new Rect(f.x, textureHeight - f.y - f.h, f.w, f.h);
				rect.alignment = SpriteAlignment.Custom;
				rect.pivot = new Vector2((float)fx.pivot[0] / f.w, 1f - (float)fx.pivot[1] / f.h);
				rect.border = Vector4.zero;
				GUID id;
				rect.spriteID = oldIds.TryGetValue(rect.name, out id) ? id : GUID.Generate();
				rects[i] = rect;
				pairs.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
			}

			provider.SetSpriteRects(rects);
			ISpriteNameFileIdDataProvider nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
			if (nameProvider != null)
			{
				nameProvider.SetNameFileIdPairs(pairs);
			}

			provider.Apply();
			importer.SaveAndReimport();
			return true;
		}

		private static Material setupMaterial(string materialPath, Texture2D texture)
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
			if (material == null)
			{
				material = new Material(Shader.Find(ShaderName));
				AssetDatabase.CreateAsset(material, materialPath);
			}

			material.mainTexture = texture;
			EditorUtility.SetDirty(material);
			AssetDatabase.SaveAssets();
			return material;
		}

		// 프리펩은 매번 새로 조립해 덮어쓴다 — 경로가 같으면 GUID(=주소 참조)는 유지된다
		private static string buildPrefab(string assetName, SheetJson sheet, Material material, float pixelsPerUnit)
		{
			PixelFxMeta fx = sheet.meta.pixelfx;
			int frameCount = sheet.frames.Length;
			int frameW = fx.frameSize[0];
			int frameH = fx.frameSize[1];
			int columns = Mathf.Max(1, fx.columns);
			int rows = Mathf.CeilToInt((float)frameCount / columns);
			float length = (float)frameCount / Mathf.Max(1, fx.fps);

			GameObject root = new GameObject(PrefabPrefix + assetName);
			root.AddComponent<VFXItem>();

			GameObject child = new GameObject(SheetChildName);
			child.transform.SetParent(root.transform, false);
			// 파티클 쿼드는 프레임 중심에 그려진다 — 이펙트 원점(pivot)이 루트 위치에 오도록 자식을 민다
			child.transform.localPosition = new Vector3(
				(frameW * 0.5f - fx.pivot[0]) / pixelsPerUnit,
				(fx.pivot[1] - frameH * 0.5f) / pixelsPerUnit,
				0f);

			ParticleSystem ps = child.AddComponent<ParticleSystem>();
			// AddComponent 직후 기본 재생 상태를 멈춰야 duration 을 바꿀 수 있다
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

			ParticleSystem.MainModule main = ps.main;
			main.duration = length;
			main.loop = fx.loop;
			main.playOnAwake = false;
			main.startLifetime = length;
			main.startSpeed = 0f;
			main.startSize3D = true;
			main.startSizeX = frameW / pixelsPerUnit;
			main.startSizeY = frameH / pixelsPerUnit;
			main.startSizeZ = 1f;
			// 랜덤 회전은 파티클이 생성될 때마다(재생마다) 새로 뽑힌다 — 렌더 스크립트가 기준점이 프레임 중심인 것만 허용한다
			if (fx.randomRotation)
			{
				main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
			}
			else
			{
				main.startRotation = 0f;
			}

			main.startColor = Color.white;
			main.gravityModifier = 0f;
			main.simulationSpace = ParticleSystemSimulationSpace.Local;
			main.scalingMode = ParticleSystemScalingMode.Hierarchy;
			main.maxParticles = 2;

			ParticleSystem.EmissionModule emission = ps.emission;
			emission.rateOverTime = 0f;
			emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 1) });

			ParticleSystem.ShapeModule shape = ps.shape;
			shape.enabled = false;

			// 그리드 타일은 좌상단부터 행 단위 — pixelfx 시트 배치와 같다.
			// 마지막 행이 덜 찬 시트도 있으므로 곡선 끝값을 실제 프레임 수 비율로 잘라 빈 칸을 건너뛴다
			ParticleSystem.TextureSheetAnimationModule sheetAnim = ps.textureSheetAnimation;
			sheetAnim.enabled = true;
			sheetAnim.mode = ParticleSystemAnimationMode.Grid;
			sheetAnim.numTilesX = columns;
			sheetAnim.numTilesY = rows;
			sheetAnim.animation = ParticleSystemAnimationType.WholeSheet;
			sheetAnim.cycleCount = 1;
			float end = (frameCount - 0.01f) / (columns * rows);
			sheetAnim.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, end));

			ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.sharedMaterial = material;
			renderer.sortingLayerName = SortingLayerName;
			renderer.sortingOrder = 0;

			if (!AssetDatabase.IsValidFolder(PrefabRoot))
			{
				AssetDatabase.CreateFolder(Path.GetDirectoryName(PrefabRoot).Replace('\\', '/'), Path.GetFileName(PrefabRoot));
			}

			string prefabPath = $"{PrefabRoot}/{PrefabPrefix}{assetName}.prefab";
			PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
			Object.DestroyImmediate(root);
			return prefabPath;
		}

		// ── JSON 형식 (tools/lib/fxlib.mjs spriteSheet 출력) ──────────

		[System.Serializable]
		private class SheetJson
		{
			public FrameEntry[] frames;
			public SheetMeta meta;
		}

		[System.Serializable]
		private class FrameEntry
		{
			public FrameRect frame;
		}

		[System.Serializable]
		private class FrameRect
		{
			public int x;
			public int y;
			public int w;
			public int h;
		}

		[System.Serializable]
		private class SheetMeta
		{
			public string image;
			public SheetSize size;
			public PixelFxMeta pixelfx;
		}

		[System.Serializable]
		private class SheetSize
		{
			public int w;
			public int h;
		}

		[System.Serializable]
		private class PixelFxMeta
		{
			public int fps;
			public bool loop;
			public int[] frameSize;
			public int[] pivot;
			public int columns;
			public bool randomRotation;
		}
	}
}
