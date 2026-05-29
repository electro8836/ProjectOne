using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace ProjectOne.Editor
{
  // 지정 폴더에 에셋이 추가/이동될 때 자동으로 Addressable 마킹
  // 규칙: (감시 폴더, 확장자) → (그룹, address 평탄화 여부)
  // 주소 형식: flattenAddress=true 면 확장자 제외 파일명만 사용
  //   예) Assets/Project/Prefabs/Units/Models/Prefab_Hero_01.prefab → "Prefab_Hero_01"
  //   예) Assets/Project/Prefabs/Units/Animations/Hero/AnimController_Hero.controller → "AnimController_Hero"
  // 정책 메모: .anim 은 마킹하지 않음 — Controller 가 Addressable 이면 의존성으로 번들 포함됨
  public class AddressableAutoMarker : AssetPostprocessor
  {
    private struct Rule
    {
      public string folder;          // 감시 폴더
      public string ext;             // 허용 확장자 (소문자, 점 포함)
      public string group;           // 마킹할 그룹 이름
      public bool flattenAddress;    // true 면 address = 파일명만, false 면 폴더 상대경로
    }

    private static readonly Rule[] Rules = new Rule[]
    {
      new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".prefab",     group = "Prefabs_Units",   flattenAddress = true },
      new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".controller", group = "Animators_Units", flattenAddress = true },
      new Rule { folder = "Assets/Project/Prefabs/Units",   ext = ".overrideController", group = "Animators_Units", flattenAddress = true },
      new Rule { folder = "Assets/Project/Prefabs/Effects", ext = ".prefab",     group = "Prefabs_Effects", flattenAddress = true },
      new Rule { folder = "Assets/Project/Prefabs/UI",      ext = ".prefab",     group = "Prefabs_UI",      flattenAddress = true },
      new Rule { folder = "Assets/Project/Prefabs/Maps",    ext = ".prefab",     group = "Prefabs_Maps",    flattenAddress = true },
    };

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
      if (!matchRule(assetPath, out string groupName, out string address))
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

      // 이미 같은 그룹 + 같은 주소면 스킵
      if (existing != null && existing.parentGroup == group && existing.address == address)
      {
        return false;
      }

      AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
      entry.address = address;

      Debug.Log($"[Addressables] 마킹: {address} → 그룹: {groupName}");
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
      if (!matchRule(assetPath, out _, out _))
      {
        return false;
      }

      settings.RemoveAssetEntry(guid, false);
      return true;
    }

    // assetPath가 감시 규칙에 해당하면 groupName, address를 채우고 true 반환
    private static bool matchRule(string assetPath, out string groupName, out string address)
    {
      groupName = null;
      address = null;
      string ext = Path.GetExtension(assetPath).ToLower();

      for (int i = 0; i < Rules.Length; i++)
      {
        Rule r = Rules[i];

        // 경로가 해당 폴더(하위 포함) 안에 있고 확장자가 일치해야 매칭
        if (!assetPath.StartsWith(r.folder + "/", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }
        // Rules 정의에 대문자 섞여도 매칭되도록 비교 시 소문자 정규화
        if (ext != r.ext.ToLower())
        {
          continue;
        }

        groupName = r.group;
        if (r.flattenAddress == true)
        {
          address = Path.GetFileNameWithoutExtension(assetPath);
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
  }
}
