using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace _SAIUN.Editor
{
    /// <summary>
    /// UI 프리팹과 메인 씬을 코드로 만든다. 여러 번 실행해도 결과가 같다.
    /// 메뉴 SAIUN/Build All 또는 배치 모드 -executeMethod _SAIUN.Editor.SaiunSceneBuilder.BuildAll 로 실행한다.
    /// 레이아웃 치수 중 확정값은 사양서 8장을, 폰트·색은 실측 대기값이라 임시값을 쓴다.
    /// </summary>
    public static class SaiunSceneBuilder
    {
        // ---- 경로 ----
        private const string ScenePath = "Assets/_SAIUN/Scenes/Main.unity";
        private const string LegacyScenePath = "Assets/Scenes/SampleScene.unity";
        private const string LegacySceneFolder = "Assets/Scenes";
        private const string FontFolder = "Assets/_SAIUN/Art/Fonts";
        private const string FontAssetPath = FontFolder + "/MalgunGothic SDF.asset";
        private const string PrefabFolder = "Assets/_SAIUN/Prefabs/UI";
        private const string BottomBarPrefabPath = PrefabFolder + "/BottomBar.prefab";
        private const string TimerHudPrefabPath = PrefabFolder + "/TimerHud.prefab";
        private const string MaterialFolder = "Assets/_SAIUN/Art/Materials";
        private const string PlanterMaterialPath = MaterialFolder + "/Flowerbed_Planter.mat";
        private const string SoilMaterialPath = MaterialFolder + "/Flowerbed_Soil.mat";
        private const string ShadowCatcherMaterialPath = MaterialFolder + "/ShadowCatcher.mat";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string ShadowCatcherShaderName = "SAIUN/ShadowCatcher";

        // 확정값은 SceneMetrics가 단일 출처다.
        private const int WindowWidth = SceneMetrics.WindowWidth;
        private const int WindowHeight = SceneMetrics.WindowHeight;

        // ---- 임시 레이아웃값 (실측 대기) ----
        private const string PlaceholderFontFamily = "Malgun Gothic";
        private const int BarPadding = 16;
        private const int BarButtonWidth = 96;
        private const int BarControlHeight = 44;
        private const int BarGap = 12;
        private const float BarFontSize = 18f;
        private const float HudPrimaryFontSize = 96f;
        private const float HudSecondaryFontSize = 28f;
        private const float HudLabelFontSize = 20f;
        private const int DotSize = 12;
        private const int DotSpacing = 10;

        // ---- 화단 임시값 (실측 대기) ----
        // 화단 흙 윗면 중심을 둘 창 픽셀. 사양서 2-3 "우측 고정 / 중앙 하단"에 따라
        // Scene Layer(340~612px)의 오른쪽 절반에 들어가고 하단 바와 16px 떨어지게 잡았다.
        private static readonly Vector2 FlowerbedWindowPixel = new Vector2(352f, 490f);
        private const float PlaceholderSmoothness = 0.15f;
        private const float ShadowStrength = 0.55f;
        private const float ShadowFadeWidth = 0.8f;

        // 유리 배경은 모든 투명 오브젝트보다 먼저 그려야 그림자·파티클을 덮지 않는다.
        private const int BackdropSortingOrder = -1000;

        // 톤매핑을 끄면 템플릿의 광원 세기 2에서는 밝은 면이 1을 넘어 하얗게 날아간다.
        private const float SunIntensity = 1f;

        [MenuItem("SAIUN/Build All (TMP·Font·Prefabs·Scene)")]
        public static void BuildAll()
        {
            if (!EnsureTmpResources())
            {
                Debug.LogError("SaiunSceneBuilder: TMP 필수 리소스가 없어 중단합니다. 임포트 후 다시 실행하세요.");
                return;
            }

            TMP_FontAsset font = EnsureFontAsset();
            BuildPrefabsIfMissing(font);
            SetupMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("SaiunSceneBuilder: 완료");
        }

        [MenuItem("SAIUN/Build UI Prefabs (missing only)")]
        public static void BuildPrefabs()
        {
            if (!EnsureTmpResources()) return;
            BuildPrefabsIfMissing(EnsureFontAsset());
            AssetDatabase.SaveAssets();
        }

        // 프리팹을 다시 만들면 내부 fileID가 바뀌어 씬 인스턴스의 오버라이드가 끊긴다.
        // 그래서 기본 경로는 없는 프리팹만 만들고, 덮어쓰기는 별도 메뉴로만 한다.
        [MenuItem("SAIUN/Rebuild UI Prefabs (overwrite)")]
        public static void RebuildPrefabs()
        {
            if (!EnsureTmpResources()) return;
            TMP_FontAsset font = EnsureFontAsset();
            BuildBottomBarPrefab(font);
            BuildTimerHudPrefab(font);
            AssetDatabase.SaveAssets();
            Debug.LogWarning("SaiunSceneBuilder: 프리팹을 덮어썼습니다. 씬의 인스턴스를 지우고 Setup Main Scene을 다시 실행하세요.");
        }

        private static void BuildPrefabsIfMissing(TMP_FontAsset font)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BottomBarPrefabPath) == null) BuildBottomBarPrefab(font);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TimerHudPrefabPath) == null) BuildTimerHudPrefab(font);
        }

        // 프리팹은 직렬화된 값을 들고 있어서 C# 기본값을 바꿔도 반영되지 않는다.
        // LoadPrefabContents로 제자리 편집해야 fileID가 유지돼 씬 인스턴스가 끊기지 않는다.
        [MenuItem("SAIUN/Apply Palette to Prefabs")]
        public static void ApplyPalette()
        {
            SetColors(BottomBarPrefabPath, typeof(BottomBarView),
                ("backgroundColor", SaiunPalette.BottomBarBackground),
                ("inputBackgroundColor", SaiunPalette.InputBackground),
                ("inputTextColor", SaiunPalette.InputText),
                ("buttonColor", SaiunPalette.MainPoint),
                ("buttonTextColor", SaiunPalette.OnMainPoint));

            SetColors(TimerHudPrefabPath, typeof(TimerHudView),
                ("textColor", SaiunPalette.HudText),
                ("dotCompletedColor", SaiunPalette.SetDotCompleted),
                ("dotPendingColor", SaiunPalette.SetDotPending));

            AssetDatabase.SaveAssets();
            Debug.Log("SaiunSceneBuilder: 팔레트를 프리팹에 적용했습니다.");
        }

        private static void SetColors(string prefabPath, System.Type componentType, params (string Field, Color Color)[] values)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Component component = contents.GetComponent(componentType);
                if (component == null)
                {
                    Debug.LogError($"SaiunSceneBuilder: {prefabPath}에 {componentType.Name}이(가) 없습니다.");
                    return;
                }

                var so = new SerializedObject(component);
                foreach ((string field, Color color) in values)
                {
                    SerializedProperty property = so.FindProperty(field);
                    if (property == null)
                    {
                        Debug.LogWarning($"SaiunSceneBuilder: {componentType.Name}에 {field} 필드가 없습니다.");
                        continue;
                    }
                    property.colorValue = color;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [MenuItem("SAIUN/Setup Main Scene")]
        public static void SetupScene()
        {
            SetupMainScene();
            AssetDatabase.SaveAssets();
        }

        // ---- TMP 리소스 ----

        private static bool EnsureTmpResources()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;

            Debug.Log("SaiunSceneBuilder: TMP Essential Resources를 임포트합니다.");
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return Resources.Load<TMP_Settings>("TMP Settings") != null;
        }

        // 한글 표시용 임시 폰트. OS 폰트를 DynamicOS 모드로 참조하므로 폰트 파일을 저장소에 넣지 않는다.
        private static TMP_FontAsset EnsureFontAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) return existing;

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(PlaceholderFontFamily, "Regular");
            if (fontAsset == null)
            {
                Debug.LogWarning($"SaiunSceneBuilder: OS 폰트 '{PlaceholderFontFamily}'를 찾지 못해 TMP 기본 폰트를 씁니다. 한글이 표시되지 않을 수 있습니다.");
                return TMP_Settings.defaultFontAsset;
            }

            EnsureFolder(FontFolder);
            fontAsset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            Texture2D atlas = fontAsset.atlasTextures[0];
            atlas.name = fontAsset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);

            Material material = fontAsset.material;
            material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(material, fontAsset);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }

        // ---- Bottom Bar 프리팹 ----

        private static void BuildBottomBarPrefab(TMP_FontAsset font)
        {
            EnsureFolder(PrefabFolder);

            var root = new GameObject("BottomBar", typeof(RectTransform), typeof(Image), typeof(BottomBarView));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, SceneMetrics.BottomBarHeight);

            Image background = root.GetComponent<Image>();
            background.raycastTarget = false;   // 바 배경 위에서는 창 드래그가 되게 둔다

            // 태스크 입력
            GameObject inputGo = TMP_DefaultControls.CreateInputField(DefaultResources());
            inputGo.name = "TaskInput";
            inputGo.transform.SetParent(root.transform, false);
            RectTransform inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 0.5f);
            inputRt.anchorMax = new Vector2(1f, 0.5f);
            inputRt.pivot = new Vector2(0.5f, 0.5f);
            inputRt.offsetMin = new Vector2(BarPadding, -BarControlHeight / 2f);
            inputRt.offsetMax = new Vector2(-(BarPadding + BarButtonWidth + BarGap), BarControlHeight / 2f);

            var input = inputGo.GetComponent<TMP_InputField>();
            input.characterLimit = SessionConfig.MaxTaskTextLength;
            var inputText = input.textComponent as TMP_Text;
            if (inputText != null)
            {
                inputText.font = font;
                inputText.fontSize = BarFontSize;
            }
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.font = font;
                placeholder.fontSize = BarFontSize;
                placeholder.text = "이번 세션에 할 일";
            }

            // 시작·정지 버튼
            GameObject buttonGo = TMP_DefaultControls.CreateButton(DefaultResources());
            buttonGo.name = "PrimaryButton";
            buttonGo.transform.SetParent(root.transform, false);
            RectTransform buttonRt = buttonGo.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(1f, 0.5f);
            buttonRt.anchorMax = new Vector2(1f, 0.5f);
            buttonRt.pivot = new Vector2(1f, 0.5f);
            buttonRt.anchoredPosition = new Vector2(-BarPadding, 0f);
            buttonRt.sizeDelta = new Vector2(BarButtonWidth, BarControlHeight);

            var label = buttonGo.GetComponentInChildren<TMP_Text>();
            label.font = font;
            label.fontSize = BarFontSize;
            label.text = "시작";
            label.raycastTarget = false;

            var view = root.GetComponent<BottomBarView>();
            var so = new SerializedObject(view);
            so.FindProperty("primaryButton").objectReferenceValue = buttonGo.GetComponent<Button>();
            so.FindProperty("primaryButtonLabel").objectReferenceValue = label;
            so.FindProperty("taskInput").objectReferenceValue = input;
            so.FindProperty("background").objectReferenceValue = background;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, BottomBarPrefabPath);
        }

        // ---- Timer HUD 프리팹 ----

        private static void BuildTimerHudPrefab(TMP_FontAsset font)
        {
            EnsureFolder(PrefabFolder);

            var root = new GameObject("TimerHud", typeof(RectTransform), typeof(TimerHudView));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, SceneMetrics.SkyLayerHeight);

            TMP_Text phase = CreateText(root.transform, "PhaseLabel", font, HudLabelFontSize, "POMODORO");
            SetTopAnchored(phase.rectTransform, y: -36f, height: 30f);

            TMP_Text primary = CreateText(root.transform, "PrimaryText", font, HudPrimaryFontSize, "00:00");
            SetMiddleAnchored(primary.rectTransform, y: 20f, height: 120f);

            TMP_Text secondary = CreateText(root.transform, "SecondaryText", font, HudSecondaryFontSize, "00:00");
            SetMiddleAnchored(secondary.rectTransform, y: -60f, height: 40f);

            // 세트 진행 도트
            var dots = new GameObject("SetDots", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            dots.transform.SetParent(root.transform, false);
            RectTransform dotsRt = dots.GetComponent<RectTransform>();
            dotsRt.anchorMin = new Vector2(0.5f, 0f);
            dotsRt.anchorMax = new Vector2(0.5f, 0f);
            dotsRt.pivot = new Vector2(0.5f, 0f);
            dotsRt.anchoredPosition = new Vector2(0f, 24f);
            dotsRt.sizeDelta = new Vector2(WindowWidth / 2f, DotSize);

            var layout = dots.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = DotSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var dotGo = new GameObject("DotTemplate", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(dots.transform, false);
            dotGo.GetComponent<RectTransform>().sizeDelta = new Vector2(DotSize, DotSize);
            var dotImage = dotGo.GetComponent<Image>();
            dotImage.sprite = BuiltinSprite("UI/Skin/Knob.psd");
            dotImage.raycastTarget = false;
            dotGo.SetActive(false);

            var view = root.GetComponent<TimerHudView>();
            var so = new SerializedObject(view);
            so.FindProperty("primaryText").objectReferenceValue = primary;
            so.FindProperty("secondaryText").objectReferenceValue = secondary;
            so.FindProperty("phaseLabel").objectReferenceValue = phase;
            so.FindProperty("dotsContainer").objectReferenceValue = dotsRt;
            so.FindProperty("dotTemplate").objectReferenceValue = dotImage;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, TimerHudPrefabPath);
        }

        // ---- 메인 씬 ----

        private static void SetupMainScene()
        {
            MoveLegacySceneIfNeeded();

            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"SaiunSceneBuilder: 씬이 없습니다: {ScenePath}");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // GameManager 오브젝트 하나에 시스템 컴포넌트를 모두 붙인다.
            GameObject gmGo = GameObject.Find("GameManager") ?? new GameObject("GameManager");
            var stateMachine = EnsureComponent<PomodoroStateMachine>(gmGo);
            var timer = EnsureComponent<PomodoroTimer>(gmGo);
            var database = EnsureComponent<SaiunDatabase>(gmGo);
            var gameManager = EnsureComponent<GameManager>(gmGo);
            var watcher = EnsureComponent<ForegroundWatcher>(gmGo);
            var windowController = Object.FindFirstObjectByType<WindowController>();

            var timerSo = new SerializedObject(timer);
            timerSo.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            timerSo.ApplyModifiedPropertiesWithoutUndo();

            var watcherSo = new SerializedObject(watcher);
            watcherSo.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            watcherSo.ApplyModifiedPropertiesWithoutUndo();

            var gmSo = new SerializedObject(gameManager);
            gmSo.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            gmSo.FindProperty("timer").objectReferenceValue = timer;
            gmSo.FindProperty("database").objectReferenceValue = database;
            gmSo.FindProperty("windowController").objectReferenceValue = windowController;
            gmSo.FindProperty("watcher").objectReferenceValue = watcher;
            gmSo.ApplyModifiedPropertiesWithoutUndo();

            // EventSystem은 새 Input System 모듈만 쓴다.
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
            GameObject esGo = eventSystem != null ? eventSystem.gameObject : new GameObject("EventSystem", typeof(EventSystem));
            var legacyModule = esGo.GetComponent<StandaloneInputModule>();
            if (legacyModule != null) Object.DestroyImmediate(legacyModule);
            EnsureComponent<InputSystemUIInputModule>(esGo);

            // 캔버스: 창 크기가 고정이므로 기준 해상도를 창 크기로 둔다.
            GameObject canvasGo = GameObject.Find("UICanvas")
                                  ?? new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(WindowWidth, WindowHeight);
            scaler.matchWidthOrHeight = 0.5f;

            SetupWindowController(windowController);

            Camera camera = SetupCamera();

            // 광원 공전: 씬의 Directional Light에 붙인다.
            Light sun = FindDirectionalLight();
            if (sun != null)
            {
                sun.intensity = SunIntensity;
                sun.shadows = LightShadows.Soft;
                var orbit = EnsureComponent<SunOrbitController>(sun.gameObject);
                var orbitSo = new SerializedObject(orbit);
                orbitSo.FindProperty("sun").objectReferenceValue = sun;
                orbitSo.FindProperty("timer").objectReferenceValue = timer;
                orbitSo.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("SaiunSceneBuilder: Directional Light가 없어 SunOrbitController를 붙이지 않았습니다.");
            }

            GameObject backdropCanvas = EnsureBackdropCanvas(camera);
            EnsureDesktopGlass(backdropCanvas.transform, canvasGo.transform, windowController);
            EnsureFlowerbed();
            EnsurePrefabInstance<TimerHudView>(canvasGo.transform, "TimerHud", TimerHudPrefabPath, gameManager);
            EnsurePrefabInstance<BottomBarView>(canvasGo.transform, "BottomBar", BottomBarPrefabPath, gameManager);
            EnsureGlassRim(canvasGo.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"SaiunSceneBuilder: 씬 저장 {ScenePath}");
        }

        // 사양서 3장: 씬은 Assets/_SAIUN/Scenes/ 에 둔다. GUID를 유지한 채 옮긴다.
        private static void MoveLegacySceneIfNeeded()
        {
            if (File.Exists(ScenePath) || !File.Exists(LegacyScenePath)) return;

            EnsureFolder(Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(LegacyScenePath, ScenePath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"SaiunSceneBuilder: 씬 이동 실패 - {error}");
                return;
            }

            // 비어 있으면 옛 폴더를 지운다.
            if (Directory.Exists(LegacySceneFolder)
                && Directory.GetFileSystemEntries(LegacySceneFolder).Length == 0)
            {
                AssetDatabase.DeleteAsset(LegacySceneFolder);
            }

            Debug.Log($"SaiunSceneBuilder: 씬 이동 {LegacyScenePath} → {ScenePath}");
        }

        private static void EnsurePrefabInstance<TView>(Transform parent, string name, string prefabPath, GameManager gameManager)
            where TView : MonoBehaviour
        {
            Transform existing = parent.Find(name);
            if (existing != null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"SaiunSceneBuilder: 프리팹이 없습니다: {prefabPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;

            // 뷰가 GameManager를 직접 알도록 씬 인스턴스에서만 참조를 넣는다.
            TView view = instance.GetComponent<TView>();
            if (view == null)
            {
                Debug.LogError($"SaiunSceneBuilder: {prefabPath}에 {typeof(TView).Name}이(가) 없습니다.");
                return;
            }

            var so = new SerializedObject(view);
            SerializedProperty prop = so.FindProperty("gameManager");
            if (prop != null)
            {
                prop.objectReferenceValue = gameManager;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---- 공용 ----

        private static TMP_Text CreateText(Transform parent, string name, TMP_FontAsset font, float fontSize, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void SetTopAnchored(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static void SetMiddleAnchored(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static TMP_DefaultControls.Resources DefaultResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = BuiltinSprite("UI/Skin/UISprite.psd"),
                background = BuiltinSprite("UI/Skin/Background.psd"),
                inputField = BuiltinSprite("UI/Skin/InputFieldBackground.psd"),
                knob = BuiltinSprite("UI/Skin/Knob.psd"),
                checkmark = BuiltinSprite("UI/Skin/Checkmark.psd"),
                dropdown = BuiltinSprite("UI/Skin/DropdownArrow.psd"),
                mask = BuiltinSprite("UI/Skin/UIMask.psd"),
            };
        }

        private static Sprite BuiltinSprite(string path)
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        }

        // 유리 설정은 씬에 직렬화돼 있어 C# 기본값을 바꿔도 반영되지 않는다. 여기서 맞춘다.
        private static void SetupWindowController(WindowController controller)
        {
            if (controller == null)
            {
                Debug.LogWarning("SaiunSceneBuilder: WindowController가 없어 유리 설정을 건너뜁니다.");
                return;
            }

            var so = new SerializedObject(controller);
            so.FindProperty("glass").enumValueIndex = (int)WindowController.GlassMode.DesktopBlur;
            // Acrylic으로 바꿔 쓸 때를 대비해 조율해 둔 농도를 씬에도 남긴다.
            so.FindProperty("glassTintStrength").floatValue = 0.15f;
            so.FindProperty("extendFrame").boolValue = false;
            so.FindProperty("excludeFromCapture").boolValue = true;
            so.FindProperty("roundedCorners").boolValue = true;
            so.FindProperty("customBorder").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("SaiunSceneBuilder: 유리 배경을 DesktopBlur로 설정했습니다.");
        }

        // 창 뒤 화면을 흐리게 깔아 주는 층. 3D 씬보다 뒤에 있어야 하므로 카메라 공간 캔버스에 둔다.
        // 예전에는 오버레이 캔버스에 있었으므로, 거기 남아 있으면 옮겨 온다(참조와 fileID는 유지된다).
        private static void EnsureDesktopGlass(Transform canvas, Transform legacyCanvas, WindowController windowController)
        {
            Transform existing = canvas.Find("DesktopGlass") ?? legacyCanvas.Find("DesktopGlass");
            GameObject glass = existing != null
                ? existing.gameObject
                : new GameObject("DesktopGlass", typeof(RectTransform), typeof(RawImage), typeof(DesktopGlassView));

            glass.transform.SetParent(canvas, false);
            glass.transform.SetAsFirstSibling();
            Stretch(glass.GetComponent<RectTransform>());
            glass.GetComponent<RawImage>().raycastTarget = false;

            // 틴트는 흐린 화면 위에 얹는 별도 층이다.
            // 예전에는 단색 Image였고 지금은 기울기 텍스처를 쓰므로, 남아 있으면 갈아 끼운다.
            Transform tintChild = glass.transform.Find("Tint");
            if (tintChild != null && tintChild.GetComponent<RawImage>() == null)
            {
                Object.DestroyImmediate(tintChild.gameObject);
                tintChild = null;
            }

            GameObject tint = tintChild != null
                ? tintChild.gameObject
                : new GameObject("Tint", typeof(RectTransform), typeof(RawImage));
            tint.transform.SetParent(glass.transform, false);
            Stretch(tint.GetComponent<RectTransform>());
            tint.GetComponent<RawImage>().raycastTarget = false;

            var view = glass.GetComponent<DesktopGlassView>();
            var so = new SerializedObject(view);
            so.FindProperty("backdrop").objectReferenceValue = glass.GetComponent<RawImage>();
            so.FindProperty("tintOverlay").objectReferenceValue = tint.GetComponent<RawImage>();
            so.FindProperty("windowController").objectReferenceValue = windowController;
            // 창 추적은 자주, 바탕화면 새로 받기는 드물게.
            so.FindProperty("refreshInterval").floatValue = 0.06f;
            so.FindProperty("idleInterval").floatValue = 0.6f;
            so.FindProperty("changeThreshold").floatValue = 0.004f;
            so.FindProperty("wallpaperInterval").floatValue = 0.3f;
            so.FindProperty("downscale").intValue = 3;
            so.FindProperty("blurPasses").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 유리 테두리 하이라이트. 창 가장자리를 따라 그리므로 캔버스에서 가장 위에 둔다.
        private static void EnsureGlassRim(Transform canvas)
        {
            Transform existing = canvas.Find("GlassRim");
            GameObject rim = existing != null
                ? existing.gameObject
                : new GameObject("GlassRim", typeof(RectTransform), typeof(RawImage), typeof(GlassRimView));

            rim.transform.SetParent(canvas, false);
            rim.transform.SetAsLastSibling();   // 하단 바보다 위에 그려야 테두리가 끊기지 않는다

            var rt = rim.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            rim.GetComponent<RawImage>().raycastTarget = false;
        }

        // 사양서 2-4: 고정 Orthographic 아이소메트릭 카메라.
        // 배경 알파 0과 Solid Color 설정은 투명 창에 필요하므로 건드리지 않는다.
        private static Camera SetupCamera()
        {
            Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogWarning("SaiunSceneBuilder: 카메라가 없어 설정을 건너뜁니다.");
                return null;
            }

            camera.orthographic = true;
            camera.orthographicSize = SceneMetrics.CameraOrthographicSize;
            camera.nearClipPlane = SceneMetrics.CameraNearClip;
            camera.farClipPlane = SceneMetrics.CameraFarClip;

            Transform t = camera.transform;
            t.rotation = Quaternion.Euler(SceneMetrics.CameraPitchDegrees, SceneMetrics.CameraYawDegrees, 0f);
            t.position = -t.forward * SceneMetrics.CameraDistance;   // 씬 원점을 바라보게 물린다

            Debug.Log($"SaiunSceneBuilder: 카메라 Orthographic Size {SceneMetrics.CameraOrthographicSize} " +
                      $"(pixelsPerUnit {SceneMetrics.PixelsPerUnit})");
            return camera;
        }

        // 유리 배경 전용 캔버스. 카메라 Far 클립 바로 앞에 펼쳐서 3D 오브젝트가 깊이 테스트로 그 앞에 그려지게 한다.
        private static GameObject EnsureBackdropCanvas(Camera camera)
        {
            GameObject go = GameObject.Find("BackdropCanvas")
                            ?? new GameObject("BackdropCanvas", typeof(Canvas), typeof(CanvasScaler));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = SceneMetrics.BackdropPlaneDistance;
            canvas.sortingOrder = BackdropSortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(WindowWidth, WindowHeight);
            scaler.matchWidthOrHeight = 0.5f;
            return go;
        }

        // ---- 화단 (P2-02) ----

        // 화단과 임시 외형, 그림자 받이를 만든다. 위치는 처음 만들 때만 정하고, 이후에는 인스펙터에서 조정한 값을 지킨다.
        private static void EnsureFlowerbed()
        {
            Shader lit = Shader.Find(LitShaderName);
            Shader catcher = Shader.Find(ShadowCatcherShaderName);
            if (lit == null || catcher == null)
            {
                Debug.LogError("SaiunSceneBuilder: 화단 셰이더를 찾지 못했습니다.");
                return;
            }

            Material planterMaterial = EnsureMaterial(PlanterMaterialPath, lit, m =>
            {
                m.SetColor("_BaseColor", SaiunPalette.Eggshell);
                m.SetFloat("_Smoothness", PlaceholderSmoothness);
            });
            Material soilMaterial = EnsureMaterial(SoilMaterialPath, lit, m =>
            {
                m.SetColor("_BaseColor", SaiunPalette.DeepJungle);
                m.SetFloat("_Smoothness", PlaceholderSmoothness);
            });
            Material catcherMaterial = EnsureMaterial(ShadowCatcherMaterialPath, catcher, m =>
            {
                m.SetColor("_ShadowColor", SaiunPalette.DeepJungle);
                m.SetFloat("_ShadowStrength", ShadowStrength);
                m.SetFloat("_FadeWidth", ShadowFadeWidth);
            });

            GameObject bedGo = GameObject.Find("Flowerbed");
            bool created = bedGo == null;
            if (created) bedGo = new GameObject("Flowerbed");
            var bed = EnsureComponent<Flowerbed>(bedGo);

            Transform planter = EnsurePrimitiveChild(bedGo.transform, "Planter", PrimitiveType.Cube,
                planterMaterial, ShadowCastingMode.On);

            Transform soilRoot = bedGo.transform.Find("Soil");
            if (soilRoot == null)
            {
                soilRoot = new GameObject("Soil").transform;
                soilRoot.SetParent(bedGo.transform, false);
            }
            for (int i = 0; i < bed.CellCount; i++)
            {
                EnsurePrimitiveChild(soilRoot, $"Soil_{i / bed.Columns}_{i % bed.Columns}", PrimitiveType.Cube,
                    soilMaterial, ShadowCastingMode.On);
            }

            Transform ground = EnsurePrimitiveChild(bedGo.transform, "ShadowGround", PrimitiveType.Quad,
                catcherMaterial, ShadowCastingMode.Off);

            var so = new SerializedObject(bed);
            so.FindProperty("planter").objectReferenceValue = planter;
            so.FindProperty("soilRoot").objectReferenceValue = soilRoot;
            so.FindProperty("shadowGround").objectReferenceValue = ground;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (created)
            {
                // 흙 윗면 중심이 지정한 창 픽셀에 보이도록 바닥 원점을 역산한다.
                Vector3 surface = SceneMetrics.WindowPixelsToGround(FlowerbedWindowPixel, bed.SurfaceHeight);
                bedGo.transform.SetPositionAndRotation(new Vector3(surface.x, 0f, surface.z), Quaternion.identity);
            }

            bed.FitVisuals();
            Debug.Log($"SaiunSceneBuilder: 화단 원점 {bedGo.transform.position}, 칸 간격 {bed.CellSize}");
        }

        // 처음 만들 때만 설정한다. 이후 색·농도 조정은 머티리얼 에셋에서 한다.
        private static Material EnsureMaterial(string path, Shader shader, System.Action<Material> configure)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            EnsureFolder(MaterialFolder);
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            configure(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform EnsurePrimitiveChild(Transform parent, string name, PrimitiveType type,
            Material material, ShadowCastingMode shadows)
        {
            Transform existing = parent.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(parent, false);
                // 클릭 판정이 필요 없는 장식이다.
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = true;
            return go.transform;
        }

        private static Light FindDirectionalLight()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) return light;
            }
            return null;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (success) Debug.Log($"SaiunSceneBuilder: 프리팹 저장 {path}");
            else Debug.LogError($"SaiunSceneBuilder: 프리팹 저장 실패 {path}");
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }
    }
}
