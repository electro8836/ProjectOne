using UnityEngine;
using UnityEditor;
using System.IO;

public class TextureFlipper
{
	[MenuItem("Assets/Texture/Flip Selected PNGs Horizontally")]
	private static void FlipSelectedTexturesHorizontal()
	{
		// Project 창에서 현재 선택된 모든 에셋/오브젝트 가져오기
		Object[] selectedObjects = Selection.objects;

		if (selectedObjects == null || selectedObjects.Length == 0)
		{
			Debug.LogWarning("좌우 반전할 PNG/Texture2D 파일을 선택해 주세요.");
			return;
		}

		int successCount = 0;
		int failCount = 0;

		// 에셋 변경 사항 묶음 처리 시작
		AssetDatabase.StartAssetEditing();

		try
		{
			foreach (Object obj in selectedObjects)
			{
				// Texture2D 에셋인 경우만 처리
				if (obj is Texture2D selectedTexture)
				{
					string path = AssetDatabase.GetAssetPath(selectedTexture);

					// Read/Write Enabled 옵션 확인
					if (!selectedTexture.isReadable)
					{
						Debug.LogError($"[반전 실패] '{selectedTexture.name}' - Inspector에서 Read/Write Enabled를 체크하고 Apply를 눌러주세요.");
						failCount++;
						continue;
					}

					int width = selectedTexture.width;
					int height = selectedTexture.height;

					Texture2D flippedTexture = new Texture2D(width, height, selectedTexture.format, false);

					// 픽셀 좌우 반전
					for (int y = 0; y < height; y++)
					{
						for (int x = 0; x < width; x++)
						{
							Color pixel = selectedTexture.GetPixel(x, y);
							flippedTexture.SetPixel(width - 1 - x, y, pixel);
						}
					}

					flippedTexture.Apply();

					// PNG 인코딩 및 파일 덮어쓰기
					byte[] bytes = flippedTexture.EncodeToPNG();
					File.WriteAllBytes(path, bytes);

					Object.DestroyImmediate(flippedTexture);
					successCount++;
				}
			}
		}
		finally
		{
			// 에셋 DB 갱신 및 완료
			AssetDatabase.StopAssetEditing();
			AssetDatabase.Refresh();
		}

		Debug.Log($"<b>텍스처 좌우 반전 작업 완료!</b> (성공: {successCount}개 / 실패: {failCount}개)");
	}

	// 메뉴 활성화/비활성화 조건 (Texture2D 선택 시에만 클릭 가능)
	[MenuItem("Assets/Texture/Flip Selected PNGs Horizontally", true)]
	private static bool ValidateFlipSelectedTexturesHorizontal()
	{
		return Selection.activeObject is Texture2D;
	}
}