using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectOne.Editor
{
	// 선택한 스프라이트 시트(Multiple 로 잘린 Texture2D)로 SpriteRenderer 애니메이션 클립을 생성한다.
	// 규격은 기존 몬스터 클립과 동일 — 12fps 키 간격 / m_SampleRate 60 / 루트 경로 바인딩
	// 클립 파일은 원본 PNG 와 같은 폴더에 "<PNG 파일명>.anim" 으로 저장한다.
	public class SpriteSheetClipCreator
	{
		private const string _menuPath = "Assets/Animation/선택한 스프라이트 시트로 클립 생성";

		private const float _frameRate = 12f;   // 키 간격 (1/12초)
		private const float _sampleRate = 60f;  // 클립 샘플레이트 (기존 클립과 동일)

		// 파일명이 이 접미사로 끝나면 루프 클립으로 만든다
		private static readonly string[] _loopSuffixes = new string[] { "_Idle", "_Move", "_Buff", "_Debuff", "_Casting" };

		[MenuItem(_menuPath, true)]
		private static bool ValidateCreateClips()
		{
			return getSelectedTextures().Count > 0;
		}

		[MenuItem(_menuPath)]
		private static void CreateClips()
		{
			List<Texture2D> textures = getSelectedTextures();
			if (textures.Count == 0)
			{
				Debug.LogWarning("[SpriteSheetClipCreator] 스프라이트 시트(Texture2D)를 선택해 주세요.");
				return;
			}

			int created = 0;
			int updated = 0;

			for (int i = 0; i < textures.Count; i++)
			{
				string texturePath = AssetDatabase.GetAssetPath(textures[i]);
				List<Sprite> sprites = loadSortedSprites(texturePath);
				if (sprites.Count == 0)
				{
					Debug.LogWarning($"[SpriteSheetClipCreator] 스프라이트가 없어 건너뜁니다: {texturePath}");
					continue;
				}

				string clipName = Path.GetFileNameWithoutExtension(texturePath);
				string folder = texturePath.Substring(0, texturePath.LastIndexOf('/'));
				string clipPath = $"{folder}/{clipName}.anim";

				// 이미 있으면 에셋 guid 유지를 위해 기존 클립을 재사용한다
				AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
				bool isNew = clip == null;
				if (isNew)
				{
					clip = new AnimationClip();
				}

				clip.frameRate = _sampleRate;
				applySpriteCurve(clip, sprites);
				applyLoopSetting(clip, clipName);

				if (isNew)
				{
					AssetDatabase.CreateAsset(clip, clipPath);
					created++;
				}
				else
				{
					EditorUtility.SetDirty(clip);
					updated++;
				}

				Debug.Log($"[SpriteSheetClipCreator] {clipPath} — 프레임 {sprites.Count}개");
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[SpriteSheetClipCreator] 완료 — 생성 {created}개 / 갱신 {updated}개");
		}

		private static List<Texture2D> getSelectedTextures()
		{
			List<Texture2D> result = new List<Texture2D>();
			Object[] selected = Selection.GetFiltered(typeof(Texture2D), SelectionMode.DeepAssets);
			for (int i = 0; i < selected.Length; i++)
			{
				Texture2D texture = selected[i] as Texture2D;
				if (texture != null)
				{
					result.Add(texture);
				}
			}

			return result;
		}

		// 시트 안의 스프라이트를 행 우선(위→아래, 왼쪽→오른쪽) 순서로 정렬해 반환
		private static List<Sprite> loadSortedSprites(string texturePath)
		{
			List<Sprite> sprites = new List<Sprite>();
			Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
			for (int i = 0; i < assets.Length; i++)
			{
				Sprite sprite = assets[i] as Sprite;
				if (sprite != null)
				{
					sprites.Add(sprite);
				}
			}

			sprites.Sort(compareSpriteOrder);
			return sprites;
		}

		private static int compareSpriteOrder(Sprite a, Sprite b)
		{
			// 텍스처 좌표는 위로 갈수록 y 가 크므로 y 는 내림차순
			if (!Mathf.Approximately(a.rect.y, b.rect.y))
			{
				return b.rect.y.CompareTo(a.rect.y);
			}

			return a.rect.x.CompareTo(b.rect.x);
		}

		private static void applySpriteCurve(AnimationClip clip, List<Sprite> sprites)
		{
			EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
			ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[sprites.Count];

			for (int i = 0; i < sprites.Count; i++)
			{
				keys[i].time = i / _frameRate;
				keys[i].value = sprites[i];
			}

			AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
		}

		private static void applyLoopSetting(AnimationClip clip, string clipName)
		{
			AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
			settings.loopTime = isLoopClip(clipName);
			AnimationUtility.SetAnimationClipSettings(clip, settings);
		}

		private static bool isLoopClip(string clipName)
		{
			for (int i = 0; i < _loopSuffixes.Length; i++)
			{
				if (clipName.EndsWith(_loopSuffixes[i]))
				{
					return true;
				}
			}

			return false;
		}
	}
}
