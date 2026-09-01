using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace ProjectOne.Editor
{
	// 몬스터 애니메이션 스프라이트 시트의 임포트 포맷 통일
	// 규칙: Sprite(Multiple) + GridByCellSize 256x256 (오프셋 0 / 패딩 0) + 커스텀 피벗(정규화) (0.5, 0.4)
	// 감시 폴더에 이미지가 추가/변경되면 자동으로 위 규칙을 적용한다.
	//   - 이미 256 그리드로 잘려 있으면 rect 는 그대로 두고 정렬/피벗만 갱신
	//     (rect 를 다시 만들면 스프라이트 이름과 internalID 가 바뀌어 .anim 참조가 끊긴다)
	//   - 그리드가 아니면 그리드로 다시 자른다 (스프라이트 이름은 "파일명_인덱스" 로 재생성)
	// 기존 에셋 일괄 정리는 메뉴 "Tools/몬스터 스프라이트 포맷 정리" 사용
	// 주의: OnPreprocessTexture 를 쓰면 프로젝트 전체 텍스처의 임포트 해시가 무효화되어
	//       스크립트를 고칠 때마다 전 텍스처가 재임포트된다 → OnPostprocessAllAssets 로 처리한다
	public class MonsterSpriteImporter : AssetPostprocessor
	{
		private const string _targetFolder = "Assets/Project/Art/Units/Animations/Monster/";
		private const string _targetExt = ".png";

		private static readonly Vector2 _cellSize = new Vector2(256f, 256f);
		private static readonly Vector2 _offset   = Vector2.zero;
		private static readonly Vector2 _padding  = Vector2.zero;
		private static readonly Vector2 _pivot    = new Vector2(0.5f, 0.4f);

		private const SpriteAlignment _alignment = SpriteAlignment.Custom;

		static void OnPostprocessAllAssets(
			string[] importedAssets,
			string[] deletedAssets,
			string[] movedAssets,
			string[] movedFromAssetPaths)
		{
			for (int i = 0; i < importedAssets.Length; i++)
			{
				processTexture(importedAssets[i]);
			}

			for (int i = 0; i < movedAssets.Length; i++)
			{
				processTexture(movedAssets[i]);
			}
		}

		[MenuItem("Tools/몬스터 스프라이트 포맷 정리")]
		private static void FixAllMonsterSprites()
		{
			string[] guids = AssetDatabase.FindAssets("t:Texture2D", new string[] { _targetFolder.TrimEnd('/') });
			int changedCount = 0;

			try
			{
				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					EditorUtility.DisplayProgressBar("몬스터 스프라이트 포맷 정리", path, (float)i / guids.Length);

					if (processTexture(path))
					{
						changedCount++;
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			Debug.Log($"[MonsterSpriteImporter] 정리 완료 — 수정 {changedCount}개 / 전체 {guids.Length}개");
		}

		// 규칙에 맞지 않는 텍스처만 설정 후 재임포트. 이미 규칙대로면 아무것도 하지 않는다(재귀 방지)
		private static bool processTexture(string assetPath)
		{
			if (!isTarget(assetPath))
			{
				return false;
			}

			TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
			if (importer == null)
			{
				return false;
			}

			ISpriteEditorDataProvider provider = getDataProvider(importer);
			if (provider == null)
			{
				return false;
			}

			SpriteRect[] rects = provider.GetSpriteRects();
			bool isGrid = isGridLayout(rects);

			if (isTypeReady(importer) && isGrid && isPivotReady(rects))
			{
				return false;
			}

			importer.textureType = TextureImporterType.Sprite;
			importer.spriteImportMode = SpriteImportMode.Multiple;

			if (isGrid)
			{
				// 이미 그리드 — 이름/ID 보존을 위해 정렬/피벗만 갱신
				for (int i = 0; i < rects.Length; i++)
				{
					rects[i].alignment = _alignment;
					rects[i].pivot = _pivot;
				}
			}
			else
			{
				Texture2D source = loadSourceTexture(assetPath);
				if (source == null)
				{
					Debug.LogWarning($"[MonsterSpriteImporter] 원본 이미지를 읽지 못해 슬라이스를 건너뜁니다: {assetPath}");
					return false;
				}

				rects = buildGridSprites(source, Path.GetFileNameWithoutExtension(assetPath));
				Object.DestroyImmediate(source);
			}

			provider.SetSpriteRects(rects);

			// 이름 ↔ fileID 매핑 갱신 (애니메이션이 참조하는 internalID 의 근거)
			ISpriteNameFileIdDataProvider nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
			if (nameProvider != null)
			{
				List<SpriteNameFileIdPair> pairs = new List<SpriteNameFileIdPair>();
				for (int i = 0; i < rects.Length; i++)
				{
					pairs.Add(new SpriteNameFileIdPair(rects[i].name, rects[i].spriteID));
				}

				nameProvider.SetNameFileIdPairs(pairs);
			}

			provider.Apply();
			importer.SaveAndReimport();
			return true;
		}

		private static bool isTarget(string path)
		{
			return path.StartsWith(_targetFolder) && path.ToLower().EndsWith(_targetExt);
		}

		private static bool isTypeReady(TextureImporter importer)
		{
			return importer.textureType == TextureImporterType.Sprite
				&& importer.spriteImportMode == SpriteImportMode.Multiple;
		}

		private static bool isPivotReady(SpriteRect[] rects)
		{
			for (int i = 0; i < rects.Length; i++)
			{
				if (rects[i].alignment != _alignment)
				{
					return false;
				}

				if (!Mathf.Approximately(rects[i].pivot.x, _pivot.x) || !Mathf.Approximately(rects[i].pivot.y, _pivot.y))
				{
					return false;
				}
			}

			return true;
		}

		// 모든 rect 가 셀 크기에 딱 맞고 격자 위치에 정렬되어 있으면 그리드로 간주
		private static bool isGridLayout(SpriteRect[] rects)
		{
			if (rects == null || rects.Length == 0)
			{
				return false;
			}

			for (int i = 0; i < rects.Length; i++)
			{
				Rect rect = rects[i].rect;
				if (!Mathf.Approximately(rect.width, _cellSize.x) || !Mathf.Approximately(rect.height, _cellSize.y))
				{
					return false;
				}

				if (Mathf.Repeat(rect.x, _cellSize.x) > 0.01f || Mathf.Repeat(rect.y, _cellSize.y) > 0.01f)
				{
					return false;
				}
			}

			return true;
		}

		private static ISpriteEditorDataProvider getDataProvider(TextureImporter importer)
		{
			SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
			factories.Init();

			ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
			if (provider == null)
			{
				return null;
			}

			provider.InitSpriteEditorDataProvider();
			return provider;
		}

		// GridByCellSize 슬라이스 생성 — 좌상단부터 왼쪽→오른쪽, 위→아래 순서. 완전 투명한 셀은 건너뛴다
		private static SpriteRect[] buildGridSprites(Texture2D source, string baseName)
		{
			int cellWidth = Mathf.RoundToInt(_cellSize.x);
			int cellHeight = Mathf.RoundToInt(_cellSize.y);
			int stepX = cellWidth + Mathf.RoundToInt(_padding.x);
			int stepY = cellHeight + Mathf.RoundToInt(_padding.y);
			int originX = Mathf.RoundToInt(_offset.x);
			int originY = Mathf.RoundToInt(_offset.y);

			List<SpriteRect> result = new List<SpriteRect>();
			int index = 0;

			for (int top = originY; top + cellHeight <= source.height; top += stepY)
			{
				// 텍스처 좌표는 아래에서 위로 증가 — 위쪽 행부터 잘리도록 y 를 뒤집는다
				int y = source.height - top - cellHeight;

				for (int x = originX; x + cellWidth <= source.width; x += stepX)
				{
					if (isEmptyCell(source, x, y, cellWidth, cellHeight))
					{
						continue;
					}

					SpriteRect spriteRect = new SpriteRect();
					spriteRect.name = $"{baseName}_{index}";
					spriteRect.rect = new Rect(x, y, cellWidth, cellHeight);
					spriteRect.alignment = _alignment;
					spriteRect.pivot = _pivot;
					spriteRect.border = Vector4.zero;
					spriteRect.spriteID = GUID.Generate();
					result.Add(spriteRect);
					index++;
				}
			}

			return result.ToArray();
		}

		private static bool isEmptyCell(Texture2D source, int x, int y, int width, int height)
		{
			Color[] pixels = source.GetPixels(x, y, width, height);
			for (int i = 0; i < pixels.Length; i++)
			{
				if (pixels[i].a > 0f)
				{
					return false;
				}
			}

			return true;
		}

		// 에셋의 Texture2D 는 읽기 불가라 원본 파일을 직접 디코드해 픽셀을 얻는다 (호출 측에서 DestroyImmediate 필요)
		private static Texture2D loadSourceTexture(string assetPath)
		{
			if (!File.Exists(assetPath))
			{
				return null;
			}

			byte[] bytes = File.ReadAllBytes(assetPath);
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!ImageConversion.LoadImage(texture, bytes, false))
			{
				Object.DestroyImmediate(texture);
				return null;
			}

			return texture;
		}
	}
}
