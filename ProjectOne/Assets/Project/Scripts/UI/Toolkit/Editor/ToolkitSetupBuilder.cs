using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectOne.Editor
{
	// UI Toolkit 기반 에셋을 만든다 (UI구조_설계.md 2·3·4).
	//  1) 층별 PanelSettings 5종 — 없을 때만 만든다
	//  2) UIManager 프리펩에 층 UIDocument 5개를 붙이고 필드에 연결 — 없을 때만 붙인다
	//  3) UI Toolkit 화면·팝업 프리펩 — 매번 새로 만든다
	// 프리펩은 Prefabs/UI 아래라 AddressableAutoMarker 가 파일명 주소로 Prefabs_UI 그룹에 자동 등록한다.
	public static class ToolkitSetupBuilder
	{
		// 기본 런타임 테마 + Theme.uss — 층 루트에 직접 붙는 요소(툴팁 등)도 테마를 받는다.
		private const string ThemePath = "Assets/Project/UIToolkit/Common/Theme.tss";
		private const string CommonDir = "Assets/Project/UIToolkit/Common/";
		private const string UIManagerPrefabPath = "Assets/Project/Prefabs/UI/UIManager.prefab";
		private const string GradeColorsPath = "Assets/Project/Data/ScriptableObject/Definitions/ItemGradeColorTable.asset";

		// 층 이름 · 정렬 값 · UIManager 필드. UGUI Canvas 와 같은 정렬 값이다.
		private static readonly string[] LayerNames = { "HUD", "Window", "Navigation", "Popup", "System" };
		private static readonly int[] LayerOrders = { 90, 200, 300, 350, 400 };
		private static readonly string[] LayerFields = { "_hudLayer", "_windowLayer", "_navigationLayer", "_popupLayer", "_systemLayer" };

		[MenuItem("ProjectOne/UI Toolkit/층·프리펩 전체 만들기")]
		public static void BuildAll()
		{
			if (buildLayers() == false)
			{
				return;
			}

			buildMastery();
			buildViews();

			AssetDatabase.SaveAssets();
			Debug.Log("[ToolkitSetupBuilder] 층·프리펩 생성 완료");
		}

		// ── 층 ──────────────────────────────────────────────────────────

		private static bool buildLayers()
		{
			ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
			if (theme == null)
			{
				Debug.LogError($"[ToolkitSetupBuilder] 런타임 테마를 찾지 못했습니다: {ThemePath}");
				return false;
			}

			PanelSettings[] settings = new PanelSettings[LayerNames.Length];
			for (int i = 0; i < LayerNames.Length; i++)
			{
				settings[i] = getOrCreatePanelSettings(LayerNames[i], LayerOrders[i], theme);
			}

			GameObject root = PrefabUtility.LoadPrefabContents(UIManagerPrefabPath);
			UI.UIManager manager = root.GetComponent<UI.UIManager>();
			if (manager == null)
			{
				Debug.LogError("[ToolkitSetupBuilder] UIManager 프리펩에 UIManager 컴포넌트가 없습니다.");
				PrefabUtility.UnloadPrefabContents(root);
				return false;
			}

			SerializedObject so = new SerializedObject(manager);
			for (int i = 0; i < LayerNames.Length; i++)
			{
				string childName = "ToolkitLayer_" + LayerNames[i];
				Transform child = root.transform.Find(childName);
				if (child == null)
				{
					GameObject go = new GameObject(childName);
					go.transform.SetParent(root.transform, false);
					child = go.transform;
				}

				UIDocument document = child.GetComponent<UIDocument>();
				if (document == null)
				{
					document = child.gameObject.AddComponent<UIDocument>();
				}

				document.panelSettings = settings[i];
				so.FindProperty(LayerFields[i]).objectReferenceValue = document;
			}

			so.ApplyModifiedPropertiesWithoutUndo();
			PrefabUtility.SaveAsPrefabAsset(root, UIManagerPrefabPath);
			PrefabUtility.UnloadPrefabContents(root);
			return true;
		}

		// 기준 해상도는 UGUI CanvasScaler 와 맞춘다(세로 1080x1920, 높이 기준).
		private static PanelSettings getOrCreatePanelSettings(string layerName, int sortingOrder, ThemeStyleSheet theme)
		{
			string path = CommonDir + "PanelSettings_" + layerName + ".asset";
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
			if (settings != null)
			{
				if (settings.themeStyleSheet != theme || settings.sortingOrder != sortingOrder)
				{
					settings.themeStyleSheet = theme;
					settings.sortingOrder = sortingOrder;
					EditorUtility.SetDirty(settings);
				}

				return settings;
			}

			settings = ScriptableObject.CreateInstance<PanelSettings>();
			settings.themeStyleSheet = theme;
			settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
			settings.referenceResolution = new Vector2Int(1080, 1920);
			settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
			settings.match = 1f;
			settings.sortingOrder = sortingOrder;

			AssetDatabase.CreateAsset(settings, path);
			return settings;
		}

		// ── 화면 · 팝업 프리펩 ──────────────────────────────────────────

		private const string WindowDir = "Assets/Project/Prefabs/UI/Window/";
		private const string PopupDir = "Assets/Project/Prefabs/UI/Popup/";
		private const string ScreensDir = "Assets/Project/UIToolkit/Screens/";
		private const string PopupsDir = "Assets/Project/UIToolkit/Popups/";

		// 레이아웃 하나 + 선택 부품(등급 색상표·캐릭터 프리뷰)만 쓰는 화면들.
		// 기존 UGUI 프리펩과 같은 경로에 덮어써 GUID·Addressable 주소를 그대로 잇는다.
		private static void buildViews()
		{
			buildView(WindowDir + "UIPrefab_EquipmentToolkit.prefab", typeof(UI.EquipmentToolkitUI), "Assets/Project/UIToolkit/Equipment/EquipmentScreen.uxml", true, true);
			buildView(WindowDir + "UIPrefab_Shop.prefab", typeof(UI.ShopUI), ScreensDir + "Shop/ShopScreen.uxml", true, false);
			buildView(WindowDir + "UIPrefab_CraftInfo.prefab", typeof(UI.CraftUI), ScreensDir + "Craft/CraftScreen.uxml", true, false);
			buildView(WindowDir + "UIPrefab_CostumeInfo.prefab", typeof(UI.CostumeInfoUI), ScreensDir + "Costume/CostumeScreen.uxml", true, true);
			buildView(WindowDir + "UIPrefab_PetInfo.prefab", typeof(UI.PetInfoUI), ScreensDir + "Pet/PetScreen.uxml", true, false);

			buildView("Assets/Project/Prefabs/UI/NavigationBar/UIPrefab_NavigationBar.prefab", typeof(UI.NavigationBar), "Assets/Project/UIToolkit/HUD/Navigation/NavigationBar.uxml", false, false);

			buildNotices();
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_MainHUD.prefab", typeof(UI.MainHud), "Assets/Project/UIToolkit/HUD/MainHud/MainHud.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_Joystick.prefab", typeof(UI.JoystickController), "Assets/Project/UIToolkit/HUD/Joystick/Joystick.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_SystemLogInfo.prefab", typeof(UI.SystemLogInfo), "Assets/Project/UIToolkit/HUD/SystemLog/SystemLogInfo.uxml", true, false);
			buildLoading();

			buildView(PopupDir + "UIPrefab_CommonPopupToolkit.prefab", typeof(UI.CommonPopupToolkit), PopupsDir + "Common/CommonPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_MenuPopup.prefab", typeof(UI.MenuPopup), PopupsDir + "Menu/MenuPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_RewardPopup.prefab", typeof(UI.RewardPopup), PopupsDir + "Reward/RewardPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_StatPopup.prefab", typeof(UI.StatPopup), PopupsDir + "Stat/StatPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_MailBoxPopup.prefab", typeof(UI.MailBoxPopup), PopupsDir + "Mail/MailBoxPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_MailSlotPopup.prefab", typeof(UI.MailSlotPopup), PopupsDir + "Mail/MailSlotPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_AccountPopup.prefab", typeof(UI.AccountPopup), PopupsDir + "Account/AccountPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_NicknamePopup.prefab", typeof(UI.NicknamePopup), PopupsDir + "Account/NicknamePopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_RankingPopup.prefab", typeof(UI.RankingPopup), PopupsDir + "Ranking/RankingPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_PlayerInfoPopup.prefab", typeof(UI.PlayerInfoPopup), PopupsDir + "Ranking/PlayerInfoPopup.uxml", true, true);
			buildView(PopupDir + "UIPrefab_QuestListPopup.prefab", typeof(UI.QuestListPopup), PopupsDir + "Quest/QuestListPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_DailyBonusPopup.prefab", typeof(UI.DailyBonusPopup), PopupsDir + "DailyBonus/DailyBonusPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_PassPopup.prefab", typeof(UI.HeroPassPopup), PopupsDir + "HeroPass/HeroPassPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_BoxRewardPopup.prefab", typeof(UI.BoxRewardPopup), PopupsDir + "BoxReward/BoxRewardPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_ConsumablePopup.prefab", typeof(UI.ConsumableInfoPopup), PopupsDir + "ItemInfo/ConsumablePopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_DecompositionPopup.prefab", typeof(UI.DecompositionPopup), PopupsDir + "Decomposition/DecompositionPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_EquipmentPopup.prefab", typeof(UI.ItemInfoPopup), PopupsDir + "ItemInfo/EquipmentPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_CurrencyListPopup.prefab", typeof(UI.CurrencyListPopup), PopupsDir + "Currency/CurrencyListPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_MaterialListPopup.prefab", typeof(UI.MaterialListPopup), PopupsDir + "Material/MaterialListPopup.uxml", true, false);
			buildView(PopupDir + "UIPrefab_ActListPopup.prefab", typeof(UI.ActListPopup), PopupsDir + "ActList/ActListPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_GoldDungeonPopup.prefab", typeof(UI.GoldDungeonPopup), PopupsDir + "Dungeon/DungeonStagePopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_LabyrinthDungeonPopup.prefab", typeof(UI.LabyrinthDungeonPopup), PopupsDir + "Dungeon/DungeonStagePopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_RuinsDungeonPopup.prefab", typeof(UI.RuinsDungeonPopup), PopupsDir + "Dungeon/DungeonStagePopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_RiftDungeonPopup.prefab", typeof(UI.RiftDungeonPopup), PopupsDir + "Dungeon/RiftDungeonPopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_ContinuePopup.prefab", typeof(UI.ContinuePopup), PopupsDir + "Dungeon/ContinuePopup.uxml", false, false);
			buildView(PopupDir + "UIPrefab_DungeonListPopup.prefab", typeof(UI.DungeonListPopup), PopupsDir + "Dungeon/DungeonListPopup.uxml", false, false);
			buildView(WindowDir + "UIPrefab_DungeonResult.prefab", typeof(UI.DungeonResultUI), PopupsDir + "Dungeon/DungeonResult.uxml", true, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_GoldDungeon.prefab", typeof(UI.GoldDungeonUI), "Assets/Project/UIToolkit/HUD/Dungeon/GoldDungeonHud.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_RiftDungeon.prefab", typeof(UI.RiftDungeonUI), "Assets/Project/UIToolkit/HUD/Dungeon/RiftDungeonHud.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_LabyrinthDungeon.prefab", typeof(UI.LabyrinthDungeonUI), "Assets/Project/UIToolkit/HUD/Dungeon/LabyrinthDungeonHud.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_RuinsDungeon.prefab", typeof(UI.RuinsDungeonUI), "Assets/Project/UIToolkit/HUD/Dungeon/RuinsDungeonHud.uxml", false, false);
			buildView("Assets/Project/Prefabs/UI/HUD/UIPrefab_DungeonMessage.prefab", typeof(UI.DungeonMessage), "Assets/Project/UIToolkit/HUD/Dungeon/DungeonMessage.uxml", false, false);
		}

		// 로딩 화면 — 모든 UI 위(정렬 999)에 덮고 UIManager 와 따로 뜨므로 자기 UIDocument 를 가진다.
		private static void buildLoading()
		{
			ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
			PanelSettings settings = getOrCreatePanelSettings("Loading", 999, theme);

			GameObject go = new GameObject("LoadingUI");
			UIDocument document = go.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = loadUxml("Assets/Project/UIToolkit/HUD/Loading/LoadingScreen.uxml");
			go.AddComponent<UI.LoadingUI>();
			savePrefab(go, "Assets/Project/Prefabs/UI/Loading/LoadingUI.prefab");
		}

		// 알림 메시지 — 경고·시스템은 같은 클래스를 위치·유지 시간만 달리해 쓴다.
		private static void buildNotices()
		{
			const string noticeDir = "Assets/Project/Prefabs/UI/Notice/";
			const string uxmlDir = "Assets/Project/UIToolkit/HUD/Notice/";

			buildNotice(noticeDir + "UIPrefab_AlertMessage.prefab", uxmlDir + "NoticeMessage.uxml", "notice--alert", 3f);
			buildNotice(noticeDir + "UIPrefab_SystemMessage.prefab", uxmlDir + "NoticeMessage.uxml", "notice--system", 5f);
			buildView(noticeDir + "UIPrefab_WarningMessage.prefab", typeof(UI.WarningMessage), uxmlDir + "WarningMessage.uxml", false, false);
			buildView(noticeDir + "UIPrefab_LevelUpMessage.prefab", typeof(UI.LevelUpMessage), uxmlDir + "LevelUpMessage.uxml", false, false);
		}

		private static void buildNotice(string prefabPath, string uxmlPath, string positionClass, float holdSeconds)
		{
			GameObject go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
			UI.NoticeMessage view = go.AddComponent<UI.NoticeMessage>();
			SerializedObject so = new SerializedObject(view);
			so.FindProperty("_layout").objectReferenceValue = loadUxml(uxmlPath);
			so.FindProperty("_positionClass").stringValue = positionClass;
			so.FindProperty("_holdSeconds").floatValue = holdSeconds;
			so.ApplyModifiedPropertiesWithoutUndo();
			savePrefab(go, prefabPath);
		}

		private static void buildView(string prefabPath, System.Type viewType, string uxmlPath, bool gradeColors, bool characterPreview)
		{
			VisualTreeAsset layout = loadUxml(uxmlPath);
			if (layout == null)
			{
				return;
			}

			string prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
			GameObject go = new GameObject(prefabName);

			// 캐릭터 프리뷰 — View 가 RenderTexture 를 받아 배경으로 건다. View 보다 먼저 붙인다.
			UI.CharacterPreview preview = null;
			if (characterPreview == true)
			{
				preview = go.AddComponent<UI.CharacterPreview>();
			}

			Component view = go.AddComponent(viewType);
			SerializedObject so = new SerializedObject(view);
			so.FindProperty("_layout").objectReferenceValue = layout;

			if (gradeColors == true)
			{
				so.FindProperty("_gradeColors").objectReferenceValue = loadGradeColors();
			}

			if (preview != null)
			{
				so.FindProperty("_characterPreview").objectReferenceValue = preview;
			}

			so.ApplyModifiedPropertiesWithoutUndo();
			savePrefab(go, prefabPath);
		}

		private static void buildMastery()
		{
			GameObject go = new GameObject("UIPrefab_MasteryToolkit");
			UI.MasteryToolkitUI view = go.AddComponent<UI.MasteryToolkitUI>();
			SerializedObject so = new SerializedObject(view);
			so.FindProperty("_layout").objectReferenceValue = loadUxml("Assets/Project/UIToolkit/Mastery/MasteryScreen.uxml");
			so.FindProperty("_skillRowTemplate").objectReferenceValue = loadUxml("Assets/Project/UIToolkit/Mastery/MasterySkillRow.uxml");
			so.FindProperty("_masteryRowTemplate").objectReferenceValue = loadUxml("Assets/Project/UIToolkit/Mastery/MasteryListRow.uxml");
			so.ApplyModifiedPropertiesWithoutUndo();

			savePrefab(go, "Assets/Project/Prefabs/UI/Window/UIPrefab_MasteryToolkit.prefab");
		}

		private static UI.ItemGradeColorTable loadGradeColors()
		{
			UI.ItemGradeColorTable colors = AssetDatabase.LoadAssetAtPath<UI.ItemGradeColorTable>(GradeColorsPath);
			if (colors == null)
			{
				Debug.LogError($"[ToolkitSetupBuilder] 등급 색상표를 찾지 못했습니다: {GradeColorsPath}");
			}

			return colors;
		}

		private static VisualTreeAsset loadUxml(string path)
		{
			VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
			if (asset == null)
			{
				Debug.LogError($"[ToolkitSetupBuilder] UXML 을 찾지 못했습니다: {path}");
			}

			return asset;
		}

		private static void savePrefab(GameObject go, string path)
		{
			PrefabUtility.SaveAsPrefabAsset(go, path);
			Object.DestroyImmediate(go);
		}
	}
}
