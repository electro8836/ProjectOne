using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectOne.Editor
{
	// Project 창에서 선택한 FX 프리팹들의 Sorting Layer 를 일괄 변경하는 에디터 툴.
	// - 대상 렌더러: 프리팹 하위의 모든 Renderer(ParticleSystemRenderer 포함, 비활성 포함)
	// - Sorting Layer 목록은 SortingLayer.layers 에서 동적으로 가져옴
	public class VFXSortingLayerWindow : EditorWindow
	{
		// 드롭다운에서 현재 선택된 Sorting Layer 인덱스
		private int _layerIndex;

		[MenuItem("Tools/VFX/Sorting Layer Setter")]
		public static void Open()
		{
			VFXSortingLayerWindow window = GetWindow<VFXSortingLayerWindow>("VFX Sorting Layer");
			window.minSize = new Vector2(320f, 140f);
		}

		private void OnEnable()
		{
			// 기본값으로 "Effect" 레이어 선택 (없으면 0)
			_layerIndex = indexOfLayer("Effect");
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("선택한 프리팹의 Sorting Layer 일괄 변경", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			string[] layerNames = getLayerNames();
			if (layerNames.Length == 0)
			{
				EditorGUILayout.HelpBox("Sorting Layer 가 없습니다.", MessageType.Warning);
				return;
			}

			_layerIndex = EditorGUILayout.Popup("Sorting Layer", Mathf.Clamp(_layerIndex, 0, layerNames.Length - 1), layerNames);

			int selectedCount = countSelectedPrefabs();
			EditorGUILayout.LabelField("선택된 프리팹", selectedCount + " 개");
			EditorGUILayout.Space();

			EditorGUI.BeginDisabledGroup(selectedCount == 0);
			if (GUILayout.Button("선택한 프리팹에 적용", GUILayout.Height(30f)))
			{
				apply(layerNames[_layerIndex]);
			}

			EditorGUI.EndDisabledGroup();

			if (selectedCount == 0)
			{
				EditorGUILayout.HelpBox("Project 창에서 변경할 FX 프리팹을 선택하세요.", MessageType.Info);
			}
		}

		// 선택 변경 시 개수 라벨이 즉시 갱신되도록 다시 그림
		private void OnSelectionChange()
		{
			Repaint();
		}

		// ── 적용 ──────────────────────────────────────────────────────

		private void apply(string layerName)
		{
			GameObject[] selected = Selection.gameObjects;
			int prefabCount = 0;
			int rendererCount = 0;

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
					Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
					for (int r = 0; r < renderers.Length; r++)
					{
						renderers[r].sortingLayerName = layerName;
						rendererCount++;
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
				$"프리팹 {prefabCount}개 / 렌더러 {rendererCount}개의 Sorting Layer 를 \"{layerName}\" 로 변경했습니다.", "확인");
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

		private static string[] getLayerNames()
		{
			SortingLayer[] layers = SortingLayer.layers;
			string[] names = new string[layers.Length];
			for (int i = 0; i < layers.Length; i++)
			{
				names[i] = layers[i].name;
			}

			return names;
		}

		private static int indexOfLayer(string layerName)
		{
			SortingLayer[] layers = SortingLayer.layers;
			for (int i = 0; i < layers.Length; i++)
			{
				if (layers[i].name == layerName)
				{
					return i;
				}
			}

			return 0;
		}
	}
}
