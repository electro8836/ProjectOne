using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ProjectOne.Editor
{
	// Project 창에서 선택한 FX 프리팹을 쿼터뷰처럼 기울이는 에디터 툴.
	// - 바닥에 눕혀 만든 3D 이펙트를 정면 직교 카메라에서 비스듬히 내려다본 모습으로 보이게 한다
	// - 루트는 VFXManager 가 재생 시 회전을 덮어쓰므로, "Tilt" 자식을 만들어 기존 내용 전체를 그 아래로 옮기고 거기에 X 회전을 준다
	// - 이미 "Tilt" 자식이 있으면 회전값만 갱신한다 (각도 재조정 가능)
	public class VFXTiltWindow : EditorWindow
	{
		private const string TiltName = "Tilt";
		private const string MainName = "Main";

		// 가상 카메라의 지면 대비 높이각(도). 90 = 완전 탑다운(바닥 원이 정원), 작을수록 납작한 타원
		private float _elevation = 35f;

		[MenuItem("Tools/VFX/Tilt Setter")]
		public static void Open()
		{
			VFXTiltWindow window = GetWindow<VFXTiltWindow>("VFX Tilt");
			window.minSize = new Vector2(320f, 160f);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("선택한 프리팹을 쿼터뷰 기울기로 변경", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			_elevation = EditorGUILayout.Slider("높이각 (도)", _elevation, 5f, 90f);

			int selectedCount = countSelectedPrefabs();
			EditorGUILayout.LabelField("선택된 프리팹", selectedCount + " 개");
			EditorGUILayout.Space();

			EditorGUI.BeginDisabledGroup(selectedCount == 0);
			if (GUILayout.Button("선택한 프리팹에 적용", GUILayout.Height(30f)))
			{
				apply();
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

		private void apply()
		{
			GameObject[] selected = Selection.gameObjects;
			Quaternion tiltRotation = Quaternion.Euler(-_elevation, 0f, 0f);
			int createdCount = 0;
			int updatedCount = 0;

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
					Transform tilt = root.transform.Find(TiltName);
					if (tilt == null)
					{
						wrapWithTilt(root);
						tilt = root.transform.Find(TiltName);
						createdCount++;
					}
					else
					{
						updatedCount++;
					}

					tilt.localRotation = tiltRotation;
					PrefabUtility.SaveAsPrefabAsset(root, path);
				}
				finally
				{
					PrefabUtility.UnloadPrefabContents(root);
				}
			}

			AssetDatabase.SaveAssets();
			EditorUtility.DisplayDialog("완료",
				$"새로 적용 {createdCount}개 / 각도 갱신 {updatedCount}개 (높이각 {_elevation}°)", "확인");
		}

		// 루트의 기존 자식과 루트 자체의 파티클을 "Tilt" 자식 아래로 옮긴다.
		private static void wrapWithTilt(GameObject root)
		{
			GameObject tilt = new GameObject(TiltName);
			tilt.transform.SetParent(root.transform, false);

			// 기존 자식 이동 — 로컬 값 유지 (Tilt 는 루트와 같은 위치/스케일이라 모습이 바뀌지 않는다)
			for (int i = root.transform.childCount - 1; i >= 0; i--)
			{
				Transform child = root.transform.GetChild(i);
				if (child == tilt.transform)
				{
					continue;
				}

				child.SetParent(tilt.transform, false);
			}

			// 루트 자신의 파티클은 컴포넌트를 옮길 수 없으므로 "Main" 자식으로 복사한 뒤 루트에서 제거한다.
			ParticleSystem rootPs = root.GetComponent<ParticleSystem>();
			if (rootPs != null)
			{
				moveRootParticleToChild(root, tilt.transform);
			}
		}

		private static void moveRootParticleToChild(GameObject root, Transform tiltTransform)
		{
			GameObject main = new GameObject(MainName);
			main.transform.SetParent(tiltTransform, false);

			ParticleSystem rootPs = root.GetComponent<ParticleSystem>();
			ParticleSystemRenderer rootRenderer = root.GetComponent<ParticleSystemRenderer>();

			ComponentUtility.CopyComponent(rootPs);
			ComponentUtility.PasteComponentAsNew(main);

			// 파티클 시스템을 붙이면 렌더러가 함께 생기므로 값만 덮어쓴다.
			ParticleSystemRenderer mainRenderer = main.GetComponent<ParticleSystemRenderer>();
			ComponentUtility.CopyComponent(rootRenderer);
			ComponentUtility.PasteComponentValues(mainRenderer);

			// 렌더러가 파티클 시스템을 요구하므로 렌더러를 먼저 지운다.
			Object.DestroyImmediate(rootRenderer);
			Object.DestroyImmediate(rootPs);
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
