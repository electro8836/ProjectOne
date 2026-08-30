using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using ProjectOne.Resources;

namespace ProjectOne.Editor
{
	// 지정 폴더에 에셋이 추가/이동될 때 자동으로 Addressable 마킹
	// 규칙: (감시 폴더, 확장자) → (그룹, address 평탄화 여부)
	// 주소 형식: flattenAddress=true 면 확장자 제외 파일명만 사용
	//   예) Assets/Project/Prefabs/Units/Models/Prefab_Hero_01.prefab → "Prefab_Hero_01"
	//   예) Assets/Project/Prefabs/Units/Animations/Hero/AnimController_Hero.controller → "AnimController_Hero"
	// 정책 메모: .anim 은 마킹하지 않음 — Controller 가 Addressable 이면 의존성으로 번들 포함됨
	// label 정책: 에셋 마킹 시 group명을 Addressables label로도 자동 부여
	//   → GetDownloadSizeAsync("Prefabs_Units") 등 label 기반 다운로드 가능
	public class AddressableAutoMarker : AssetPostprocessor
	{
		private struct Rule
		{
			public string folder;          // 감시 폴더
			public string ext;             // 허용 확장자 (소문자, 점 포함)
			public string group;           // 마킹할 그룹 이름 (groupFromFileName 이면 무시)
			public string label;           // 부여할 Addressables label (보통 group과 동일, groupFromFileName 이면 무시)
			public bool flattenAddress;    // true 면 address = 파일명만, false 면 폴더 상대경로
			public bool keepExtension;     // true 면 address 에 확장자 유지 (예: edt_xxx.bytes)
			public bool inDownloadLabels;  // true 면 PatchConfig._downloadLabels 에 포함

			// true 면 group/label 을 파일명에서 도출한다 — 에셋 1개당 그룹 1개.
			// 아틀라스가 이 방식이다: 아틀라스마다 번들을 나눠야 필요한 것만 받을 수 있는데,
			// 고정 group 으로는 한 폴더의 아틀라스가 전부 한 번들로 뭉쳐 그게 불가능하다.
			// 주소도 파일명이라 결과적으로 주소 = 그룹 = 라벨이 되어 대응표가 필요 없다.
			public bool groupFromFileName;
		}

		private static readonly Rule[] Rules = new Rule[]
		{
			new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".prefab",     group = "Prefabs_Units",   label = "Prefabs_Units",   flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".controller", group = "Animators_Units", label = "Animators_Units", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".overrideController", group = "Animators_Units", label = "Animators_Units", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/Effects", ext = ".prefab",     group = "Prefabs_Effects", label = "Prefabs_Effects", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/UI",      ext = ".prefab",     group = "Prefabs_UI",      label = "Prefabs_UI",      flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/Maps",    ext = ".prefab",     group = "Prefabs_Maps",    label = "Prefabs_Maps",    flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/Projectile", ext = ".prefab",  group = "Prefabs_Projectile", label = "Prefabs_Projectile", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			new Rule { folder = "Assets/Project/Prefabs/WorldObject", ext = ".prefab", group = "Prefabs_WorldObject", label = "Prefabs_WorldObject", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			// 씬 종속 카메라 리그 — 맵과 함께 코드가 띄우므로 맵 그룹에 같이 담는다 (GameplaySceneSetup).
			new Rule { folder = "Assets/Project/Prefabs/Camera",  ext = ".prefab",     group = "Prefabs_Maps",    label = "Prefabs_Maps",    flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			// SpriteAtlas(V2) — Art 하위 전체. UI 아이콘(Art/UI/Atlas)과 유닛 파츠(Art/Units/Parts)를 함께 덮는다.
			// 런타임은 아틀라스만 로드하고(AtlasManager) 스프라이트는 이름으로 꺼내므로, 낱장 png 는 마킹하지 않는다.
			new Rule { folder = "Assets/Project/Art", ext = ".spriteatlasv2", flattenAddress = true, keepExtension = false, inDownloadLabels = true, groupFromFileName = true },
			// 아바타 파츠 세트(ScriptableObject) — 무기/코스튬 외형. 주소 = 파일명, AvatarCatalog 가 프리로드.
			new Rule { folder = "Assets/Project/Data/ScriptableObject/Avatar", ext = ".asset", group = "Avatar_Sets", label = "Avatar_Sets", flattenAddress = true, keepExtension = false, inDownloadLabels = true },
			// EDT 테이블 — 그룹 Data_Tables / 라벨 Tables 로 자동 등록. 부트 로더가 라벨 "Tables" 로 로드.
			new Rule { folder = "Assets/Project/Data/Tables",     ext = ".bytes",      group = "Data_Tables",     label = "Tables",          flattenAddress = true, keepExtension = true,  inDownloadLabels = false },
		};

		// PatchConfig.asset 위치 — Addressables에 올리지 않음 (AssetBundleLoader가 인스펙터로 보유)
		private const string _patchConfigDir       = "Assets/Project/Data/ScriptableObject/Config";
		private const string _patchConfigAssetPath = "Assets/Project/Data/ScriptableObject/Config/PatchConfig.asset";

		// AtlasManifest.asset 위치 — 상주 로드할 아틀라스 주소 목록(AssetBundleLoader가 인스펙터로 보유)
		private const string _atlasManifestAssetPath = "Assets/Project/Data/ScriptableObject/Config/AtlasManifest.asset";

		// 아틀라스 수집 대상 폴더/확장자 — Rules 의 .spriteatlasv2 규칙과 동일하게 유지할 것
		private const string _atlasFolder = "Assets/Project/Art";
		private const string _atlasExt    = ".spriteatlasv2";

		// ── AssetPostprocessor 진입점 ─────────────────────────────────

		static void OnPostprocessAllAssets(
			string[] importedAssets,
			string[] deletedAssets,
			string[] movedAssets,
			string[] movedFromAssetPaths)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				return;
			}

			bool dirty = false;

			// 새로 임포트된 에셋
			for (int i = 0; i < importedAssets.Length; i++)
			{
				if (tryMark(settings, importedAssets[i]))
				{
					dirty = true;
				}
			}

			// 이동된 에셋 — 새 경로로 재마킹, 이전 경로 언마킹
			for (int i = 0; i < movedAssets.Length; i++)
			{
				unmark(settings, movedFromAssetPaths[i]);
				if (tryMark(settings, movedAssets[i]))
				{
					dirty = true;
				}
			}

			// 삭제된 에셋 언마킹 (이미 파일이 없으므로 GUID 정리만)
			for (int i = 0; i < deletedAssets.Length; i++)
			{
				if (unmark(settings, deletedAssets[i]))
				{
					dirty = true;
				}
			}

			if (dirty)
			{
				EditorUtility.SetDirty(settings);
				AssetDatabase.SaveAssets();
			}

			// 아틀라스(.spriteatlasv2)가 추가/이동/삭제됐으면 AtlasManifest 자동 갱신
			if (atlasChanged(importedAssets) || atlasChanged(movedAssets)
				|| atlasChanged(movedFromAssetPaths) || atlasChanged(deletedAssets))
			{
				refreshAtlasManifest();
			}
		}

		// 경로 배열에 감시 폴더의 .spriteatlasv2 가 하나라도 있으면 true
		private static bool atlasChanged(string[] paths)
		{
			for (int i = 0; i < paths.Length; i++)
			{
				if (isWatchedAtlas(paths[i]))
				{
					return true;
				}
			}

			return false;
		}

		private static bool isWatchedAtlas(string assetPath)
		{
			return assetPath.StartsWith(_atlasFolder + "/", System.StringComparison.OrdinalIgnoreCase)
				&& Path.GetExtension(assetPath).ToLower() == _atlasExt;
		}

		// ── 일괄 마킹 메뉴 ───────────────────────────────────────────

		[MenuItem("Tools/Addressables/Mark All Watched Folders")]
		public static void MarkAll()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				EditorUtility.DisplayDialog("오류", "Addressable Settings가 없습니다.\nWindow → Asset Management → Addressables → Groups에서 먼저 생성하세요.", "확인");
				return;
			}

			int count = 0;
			HashSet<string> visitedFolders = new HashSet<string>();
			for (int r = 0; r < Rules.Length; r++)
			{
				string folder = Rules[r].folder;
				if (visitedFolders.Contains(folder) == true)
				{
					continue;
				}

				visitedFolders.Add(folder);
				if (AssetDatabase.IsValidFolder(folder) == false)
				{
					continue;
				}

				string[] guids = AssetDatabase.FindAssets("", new[] { folder });
				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					if (tryMark(settings, path))
					{
						count++;
					}
				}
			}

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();

			// 마킹 완료 후 PatchConfig.asset / AtlasManifest.asset 자동 갱신
			refreshPatchConfig();
			refreshAtlasManifest();

			EditorUtility.DisplayDialog("완료", $"{count}개 에셋을 Addressable로 마킹했습니다.", "확인");
		}

		[MenuItem("Tools/Addressables/Clear All Auto-Marked")]
		public static void ClearAll()
		{
			if (!EditorUtility.DisplayDialog("경고", "자동 마킹된 모든 항목을 해제합니다. 계속하시겠습니까?", "확인", "취소"))
			{
				return;
			}

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				return;
			}

			int count = 0;
			HashSet<string> visitedFolders = new HashSet<string>();
			for (int r = 0; r < Rules.Length; r++)
			{
				string folder = Rules[r].folder;
				if (visitedFolders.Contains(folder) == true)
				{
					continue;
				}

				visitedFolders.Add(folder);
				if (AssetDatabase.IsValidFolder(folder) == false)
				{
					continue;
				}

				string[] guids = AssetDatabase.FindAssets("", new[] { folder });
				for (int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					if (unmark(settings, path))
					{
						count++;
					}
				}
			}

			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssets();
			EditorUtility.DisplayDialog("완료", $"{count}개 항목 마킹을 해제했습니다.", "확인");
		}

		// ── 내부 ──────────────────────────────────────────────────────

		private static bool tryMark(AddressableAssetSettings settings, string assetPath)
		{
			if (!matchRule(assetPath, out string groupName, out string address, out string label))
			{
				return false;
			}

			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			if (string.IsNullOrEmpty(guid))
			{
				return false;
			}

			AddressableAssetGroup group = getOrCreateGroup(settings, groupName);
			AddressableAssetEntry existing = settings.FindAssetEntry(guid);

			// 이미 같은 그룹 + 같은 주소 + label이 있으면 스킵
			if (existing != null && existing.parentGroup == group && existing.address == address
				&& existing.labels.Contains(label))
			{
				return false;
			}

			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
			entry.address = address;

			// label 자동 부여 (GetDownloadSizeAsync / 부트 로더에서 label로 사용)
			settings.AddLabel(label);
			entry.SetLabel(label, true);

			Debug.Log($"[Addressables] 마킹: {address} → 그룹: {groupName} / 라벨: {label}");
			return true;
		}

		private static bool unmark(AddressableAssetSettings settings, string assetPath)
		{
			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			if (string.IsNullOrEmpty(guid))
			{
				return false;
			}

			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null)
			{
				return false;
			}

			// 자동 마킹 규칙에 해당하는 경로만 제거 (수동 마킹 보호)
			if (!matchRule(assetPath, out _, out _, out string label))
			{
				return false;
			}

			entry.SetLabel(label, false);
			settings.RemoveAssetEntry(guid, false);
			return true;
		}

		// assetPath가 감시 규칙에 해당하면 groupName, address, label을 채우고 true 반환
		private static bool matchRule(string assetPath, out string groupName, out string address, out string label)
		{
			groupName = null;
			address = null;
			label = null;
			string ext = Path.GetExtension(assetPath).ToLower();

			for (int i = 0; i < Rules.Length; i++)
			{
				Rule r = Rules[i];

				// 경로가 해당 폴더(하위 포함) 안에 있고 확장자가 일치해야 매칭
				if (!assetPath.StartsWith(r.folder + "/", System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				// Rules 정의에 대문자 섞여도 매칭되도록 비교 시 소문자 정규화
				if (ext != r.ext.ToLower())
				{
					continue;
				}

				if (r.groupFromFileName == true)
				{
					groupName = Path.GetFileNameWithoutExtension(assetPath);
					label = groupName;
				}
				else
				{
					groupName = r.group;
					label = r.label;
				}

				if (r.flattenAddress == true)
				{
					address = r.keepExtension ? Path.GetFileName(assetPath) : Path.GetFileNameWithoutExtension(assetPath);
				}
				else
				{
					string relative = assetPath.Substring(r.folder.Length + 1);
					address = Path.ChangeExtension(relative, null).Replace('\\', '/');
				}

				return true;
			}

			return false;
		}

		// 그룹이 없으면 번들 스키마 포함해서 생성
		private static AddressableAssetGroup getOrCreateGroup(AddressableAssetSettings settings, string groupName)
		{
			AddressableAssetGroup group = settings.FindGroup(groupName);
			if (group != null)
			{
				return group;
			}

			group = settings.CreateGroup(groupName, false, false, false, null,
				typeof(ContentUpdateGroupSchema),
				typeof(BundledAssetGroupSchema));

			// 번들 압축 기본값 설정
			BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema != null)
			{
				schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
				schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
			}

			Debug.Log($"[Addressables] 그룹 생성: {groupName}");
			return group;
		}

		// Rules에서 distinct group명 목록을 추출해 PatchConfig.asset의 _downloadLabels만 갱신
		// MarkAll() 완료 후 자동 호출 → 수동 편집 불필요
		// PatchConfig는 Addressables에 올리지 않음 (AssetBundleLoader가 인스펙터로 보유)
		private static void refreshPatchConfig()
		{
			// Rules에서 중복 없는 label 목록 추출 (순서 유지) — 패치 다운로드 대상 규칙만
			var seen = new HashSet<string>();
			var labelList = new List<string>();
			for (int i = 0; i < Rules.Length; i++)
			{
				if (Rules[i].inDownloadLabels == false)
				{
					continue;
				}

				// 파일명이 곧 라벨인 규칙은 Rules 만 봐서는 알 수 없다 — 실제 파일을 훑어 채운다.
				if (Rules[i].groupFromFileName == true)
				{
					List<string> names = collectAtlasNames();
					for (int n = 0; n < names.Count; n++)
					{
						if (seen.Add(names[n]))
						{
							labelList.Add(names[n]);
						}
					}

					continue;
				}

				if (seen.Add(Rules[i].label))
				{
					labelList.Add(Rules[i].label);
				}
			}

			// Config 폴더 없으면 생성 (Data/ScriptableObject 까지는 존재한다고 가정)
			if (!AssetDatabase.IsValidFolder(_patchConfigDir))
			{
				AssetDatabase.CreateFolder("Assets/Project/Data/ScriptableObject", "Config");
			}

			// PatchConfig.asset 로드 또는 신규 생성
			PatchConfig config = AssetDatabase.LoadAssetAtPath<PatchConfig>(_patchConfigAssetPath);
			if (config == null)
			{
				config = ScriptableObject.CreateInstance<PatchConfig>();
				AssetDatabase.CreateAsset(config, _patchConfigAssetPath);
			}

			// SerializedObject로 _downloadLabels만 갱신 (다른 필드는 보존)
			var so = new SerializedObject(config);
			SerializedProperty labelsProp = so.FindProperty("_downloadLabels");
			labelsProp.arraySize = labelList.Count;
			for (int i = 0; i < labelList.Count; i++)
			{
				labelsProp.GetArrayElementAtIndex(i).stringValue = labelList[i];
			}
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(config);

			AssetDatabase.SaveAssets();
			Debug.Log($"[Addressables] PatchConfig 갱신 완료: [{string.Join(", ", labelList)}]");
		}

		// 감시 폴더의 .spriteatlasv2 이름(=주소=그룹=라벨) 목록.
		// AtlasManifest 갱신과 PatchConfig 라벨 수집이 같은 목록을 써야 하므로 한 곳에 둔다.
		private static List<string> collectAtlasNames()
		{
			var names = new List<string>();
			if (AssetDatabase.IsValidFolder(_atlasFolder) == false)
			{
				return names;
			}

			string[] guids = AssetDatabase.FindAssets("", new[] { _atlasFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (isWatchedAtlas(path))
				{
					names.Add(Path.GetFileNameWithoutExtension(path));
				}
			}

			names.Sort();	// 파일 순회 순서에 흔들리지 않게 고정 — 매니페스트/PatchConfig diff 를 줄인다
			return names;
		}

		// Art 하위의 .spriteatlasv2 주소를 수집해 AtlasManifest.asset의 _atlasAddresses를 갱신
		// MarkAll() 및 아틀라스 추가/삭제 시 자동 호출 → 부트가 이 목록으로 아틀라스를 로드
		private static void refreshAtlasManifest()
		{
			List<string> addressList = collectAtlasNames();

			// Config 폴더 없으면 생성 (Data/ScriptableObject 까지는 존재한다고 가정)
			if (!AssetDatabase.IsValidFolder(_patchConfigDir))
			{
				AssetDatabase.CreateFolder("Assets/Project/Data/ScriptableObject", "Config");
			}

			// AtlasManifest.asset 로드 또는 신규 생성
			AtlasManifest manifest = AssetDatabase.LoadAssetAtPath<AtlasManifest>(_atlasManifestAssetPath);
			if (manifest == null)
			{
				manifest = ScriptableObject.CreateInstance<AtlasManifest>();
				AssetDatabase.CreateAsset(manifest, _atlasManifestAssetPath);
			}

			// SerializedObject로 _atlasAddresses만 갱신
			var so = new SerializedObject(manifest);
			SerializedProperty addressesProp = so.FindProperty("_atlasAddresses");
			addressesProp.arraySize = addressList.Count;
			for (int i = 0; i < addressList.Count; i++)
			{
				addressesProp.GetArrayElementAtIndex(i).stringValue = addressList[i];
			}
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(manifest);

			AssetDatabase.SaveAssets();
			Debug.Log($"[Addressables] AtlasManifest 갱신 완료: [{string.Join(", ", addressList)}]");
		}
	}
}
