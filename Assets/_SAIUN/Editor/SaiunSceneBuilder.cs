using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using _SAIUN.Scripts.Weather;
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
        private const string VaneMaterialPath = MaterialFolder + "/WeatherVane_Metal.mat";
        private const string VaneAccentMaterialPath = MaterialFolder + "/WeatherVane_Accent.mat";
        private const string ShadowCatcherMaterialPath = MaterialFolder + "/ShadowCatcher.mat";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string ShadowCatcherShaderName = "SAIUN/ShadowCatcher";
        private const string GlowMaterialPath = MaterialFolder + "/Particle_Glow.mat";
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

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
        // 화분은 둥근 테두리가 빛을 받아 반짝이도록 도자기처럼 조금 매끈하게, 흙은 거의 무광.
        private const float PlanterSmoothness = 0.42f;
        private const float SoilSmoothness = 0.12f;
        // 흙 알갱이(디테일 맵)의 칸 대비 반복 비율. 칸과 어긋나게 두어 칸마다 같은 무늬가 보이지 않는다.
        private const float SoilDetailTiling = 0.37f;
        private const float ShadowStrength = 0.55f;
        private const float ShadowFadeWidth = 0.8f;
        // 해가 22도까지 낮아지면 작물 그림자가 키의 2.5배로 늘어난다. 그만큼 받이를 넓게 깐다.
        private const float ShadowGroundMargin = 2.4f;

        // 유리 배경은 모든 투명 오브젝트보다 먼저 그려야 그림자·파티클을 덮지 않는다.
        private const int BackdropSortingOrder = -1000;

        // 톤매핑을 끄면 템플릿의 광원 세기 2에서는 밝은 면이 1을 넘어 하얗게 날아간다.
        private const float SunIntensity = 1f;

        // 가산 파티클 재질의 HDR 배율
        private const float GlowIntensity = 2.5f;

        // ---- 비 임시값 ----
        private const float RainDepth = 4f;          // 카메라에서 빗줄기까지 거리. 화단(약 8)보다 앞이다.
        private const float RainFallSpeed = 9f;      // RainEffect 기본값과 같게 둔다
        private const float RainDropSize = 0.016f;
        private const float RainStreakPerSpeed = 0.03f;
        private const float RainMargin = 0.6f;       // 화면 밖에서 생겨 화면 밖에서 사라지게 두르는 여유(유닛)
        private const int RainMaxParticles = 1500;

        // ---- 빗방울 튐 임시값 ----
        private const float SplashDropSize = 0.03f;
        private const float SplashSpeedMin = 0.45f;
        private const float SplashSpeedMax = 0.9f;
        private const float SplashLifetime = 0.3f;
        private const float SplashGravity = 0.6f;

        // ---- 바람 임시값 ----
        private const float WindDepth = 3.6f;             // 비보다 조금 앞. 화단(약 8)보다 앞이다.
        private const float WindStreakWidth = 0.035f;     // 바람결 굵기(유닛)
        private const float WindStreakAlpha = 0.65f;
        private const float WindStreakTrail = 0.45f;      // 꼬리 길이(알갱이 수명에 대한 비율). 빠를수록 길다.
        private const float WindStreakVertexSpacing = 0.04f;
        private const float WindStreakWave = 0.25f;       // 바람결이 굽이치는 난류 세기
        private const float WindStreakWaveFrequency = 0.9f;
        private const float WindStreakLifetimeMin = 0.9f;
        private const float WindStreakLifetimeMax = 1.6f;
        private const float WindLeafSizeMin = 0.13f;
        private const float WindLeafSizeMax = 0.2f;
        private const float WindLeafLifetime = 8f;        // 가장 느린 잎도 화면을 건널 만큼
        private const float WindLeafSpin = 3.5f;          // 잎이 도는 빠르기(라디안/초)
        private const float WindLeafFlutter = 0.35f;      // 잎이 오르내리는 난류 세기
        private const float WindLeafFlutterFrequency = 0.6f;
        private const float WindBandHeight = 0.7f;        // 카드 높이 중 바람이 지나는 띠의 비율
        private const float WindBandLift = 0.4f;          // 띠를 카드 가운데보다 올리는 거리(유닛). 시계 쪽에서 흐른다.

        // ---- 풍향계 임시값 (유닛, 화단 기준) ----
        private const float VanePoleHeight = 1.15f;
        private const float VanePoleThickness = 0.04f;
        private const float VaneShaftLength = 0.62f;
        private const float VaneBarThickness = 0.024f;
        private const float VaneHeadSize = 0.11f;
        private const float VaneTailHeight = 0.17f;
        private const float VaneTailLength = 0.22f;
        private const float VaneCompassLength = 0.34f;
        private const float VaneCompassHeight = 0.72f;    // 기둥 높이에 대한 동서남북 막대 위치
        private const float CupsLift = 0.18f;             // 화살표 위로 풍속계가 올라간 높이
        private const float CupArmLength = 0.2f;
        private const float CupSize = 0.08f;
        private const int CupCount = 3;
        private const float VaneSmoothness = 0.55f;

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

            // 캔버스: 창 픽셀이 고정이라 배율 없이 픽셀 그대로 쓴다.
            // 모든 UI는 480×680 카드(= 창 전체) 안에 둔다.
            GameObject canvasGo = GameObject.Find("UICanvas")
                                  ?? new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UsePixels(canvasGo.GetComponent<CanvasScaler>());
            Transform uiCard = EnsureCard(canvasGo.transform);

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
                orbitSo.FindProperty("intensity").floatValue = SunIntensity;
                orbitSo.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("SaiunSceneBuilder: Directional Light가 없어 SunOrbitController를 붙이지 않았습니다.");
            }

            Transform backdropCard = EnsureCard(EnsureBackdropCanvas(camera).transform);
            EnsureDesktopGlass(backdropCard, uiCard, windowController);
            Flowerbed bed = EnsureFlowerbed();
            EnsureCropGrowth(bed, stateMachine, timer, gameManager);
            EnsureWeather(camera, backdropCard, stateMachine, bed);
            EnsurePrefabInstance<TimerHudView>(uiCard, "TimerHud", TimerHudPrefabPath, gameManager);
            KeepCloudsOffHud(backdropCard, uiCard.Find("TimerHud"));
            EnsurePrefabInstance<BottomBarView>(uiCard, "BottomBar", BottomBarPrefabPath, gameManager);

            // 세션 설정 패널과 시스템 설정 화면(P4-04). 하단 바보다 위, 알림·테두리보다 아래에 둔다.
            Transform barTransform = uiCard.Find("BottomBar");
            SettingsUiBuilder.Build(uiCard, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath),
                gameManager, barTransform != null ? barTransform.GetComponent<BottomBarView>() : null, rebuild: false);

            EnsureScreenAlert(uiCard, gameManager);
            EnsureSound(gameManager);
            EnsureGlassRim(uiCard);

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

        // 시각 알림(P5-02). HUD·하단 바까지 덮어야 하므로 그 뒤에 두고, 유리 테두리보다는 아래에 둔다.
        private static void EnsureScreenAlert(Transform canvas, GameManager gameManager)
        {
            Transform existing = canvas.Find("ScreenAlert");
            GameObject root = existing != null
                ? existing.gameObject
                : new GameObject("ScreenAlert", typeof(RectTransform), typeof(ScreenAlertView));
            root.transform.SetParent(canvas, false);
            root.transform.SetAsLastSibling();
            Stretch(root.GetComponent<RectTransform>());

            Transform dimChild = root.transform.Find("Dim");
            GameObject dim = dimChild != null ? dimChild.gameObject : new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(root.transform, false);
            dim.transform.SetAsFirstSibling();
            Stretch(dim.GetComponent<RectTransform>());
            var dimImage = dim.GetComponent<Image>();
            dimImage.raycastTarget = false;
            dimImage.enabled = false;

            Transform glowChild = root.transform.Find("EdgeGlow");
            GameObject glow = glowChild != null ? glowChild.gameObject : new GameObject("EdgeGlow", typeof(RectTransform), typeof(RawImage));
            glow.transform.SetParent(root.transform, false);
            glow.transform.SetAsLastSibling();
            Stretch(glow.GetComponent<RectTransform>());
            var glowImage = glow.GetComponent<RawImage>();
            glowImage.raycastTarget = false;
            glowImage.enabled = false;

            var view = root.GetComponent<ScreenAlertView>();
            var so = new SerializedObject(view);
            so.FindProperty("gameManager").objectReferenceValue = gameManager;
            so.FindProperty("edgeGlow").objectReferenceValue = glowImage;
            so.FindProperty("dimOverlay").objectReferenceValue = dimImage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 효과음(P5-01). 임시 음원을 만들고 AudioSource 하나로 재생한다.
        private static void EnsureSound(GameManager gameManager)
        {
            System.Collections.Generic.Dictionary<string, AudioClip> clips = SoundPlaceholderBuilder.BuildMissing();

            GameObject go = GameObject.Find("Sound") ?? new GameObject("Sound", typeof(AudioSource), typeof(SoundView));
            var source = go.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            var so = new SerializedObject(go.GetComponent<SoundView>());
            so.FindProperty("gameManager").objectReferenceValue = gameManager;
            so.FindProperty("source").objectReferenceValue = source;
            so.FindProperty("completeClip").objectReferenceValue = clips[SoundPlaceholderBuilder.Complete];
            so.FindProperty("transitionClip").objectReferenceValue = clips[SoundPlaceholderBuilder.Transition];
            so.FindProperty("harvestClip").objectReferenceValue = clips[SoundPlaceholderBuilder.Harvest];
            so.FindProperty("warningClip").objectReferenceValue = clips[SoundPlaceholderBuilder.Warning];
            so.FindProperty("deathClip").objectReferenceValue = clips[SoundPlaceholderBuilder.Death];
            so.ApplyModifiedPropertiesWithoutUndo();
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
            UsePixels(go.GetComponent<CanvasScaler>());
            return go;
        }

        // 창 픽셀과 UI 단위를 1:1로. 창 크기가 고정이라 해상도에 맞춰 늘릴 이유가 없다.
        private static void UsePixels(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = SceneMetrics.PixelsPerUnit;
        }

        // 캔버스 아래 480×680 카드(= 창 전체). 모든 UI는 이 카드 기준으로 배치한다. 예전에 캔버스에 바로 있던 UI는 카드로 옮긴다.
        private static Transform EnsureCard(Transform canvas)
        {
            var card = canvas.Find("Card") as RectTransform;
            if (card == null)
            {
                card = (RectTransform)new GameObject("Card", typeof(RectTransform)).transform;
                card.SetParent(canvas, false);
            }
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(0f, SceneMetrics.WindowHeight);

            var others = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in canvas)
            {
                if (child != card) others.Add(child);
            }
            foreach (Transform child in others) child.SetParent(card, false);
            return card;
        }

        // ---- 화단 (P2-02) ----

        // 화단과 임시 외형, 그림자 받이를 만든다. 위치는 처음 만들 때만 정하고, 이후에는 인스펙터에서 조정한 값을 지킨다.
        private static Flowerbed EnsureFlowerbed()
        {
            Shader lit = Shader.Find(LitShaderName);
            Shader catcher = Shader.Find(ShadowCatcherShaderName);
            if (lit == null || catcher == null)
            {
                Debug.LogError("SaiunSceneBuilder: 화단 셰이더를 찾지 못했습니다.");
                return null;
            }

            Material planterMaterial = EnsureMaterial(PlanterMaterialPath, lit, m => m.SetColor("_BaseColor", SaiunPalette.Eggshell));
            planterMaterial.SetFloat("_Smoothness", PlanterSmoothness);

            // 흙 색은 텍스처가 들고 있어 기본색은 흰색으로 둔다(젖음은 기본색을 어둡게 해서 표현한다).
            // 기본 맵은 칸마다 한 장(고랑 그늘·둔덕 밝음), 디테일 맵은 칸과 어긋나게 반복되는 알갱이다.
            Material soilMaterial = EnsureMaterial(SoilMaterialPath, lit, _ => { });
            (Texture2D soilCell, Texture2D soilDetail, Texture2D soilNormal) = FlowerbedArtBuilder.EnsureSoilTextures();
            soilMaterial.SetColor("_BaseColor", Color.white);
            soilMaterial.SetTexture("_BaseMap", soilCell);
            soilMaterial.SetTexture("_BumpMap", null);
            soilMaterial.DisableKeyword("_NORMALMAP");
            soilMaterial.SetTexture("_DetailAlbedoMap", soilDetail);
            soilMaterial.SetTexture("_DetailNormalMap", soilNormal);
            soilMaterial.SetTextureScale("_DetailAlbedoMap", Vector2.one * SoilDetailTiling);
            soilMaterial.SetFloat("_DetailAlbedoMapScale", 1f);
            soilMaterial.SetFloat("_DetailNormalMapScale", 1f);
            soilMaterial.EnableKeyword("_DETAIL_MULX2");
            soilMaterial.SetFloat("_Smoothness", SoilSmoothness);
            EditorUtility.SetDirty(planterMaterial);
            EditorUtility.SetDirty(soilMaterial);
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

            // 화분·흙 메시는 Flowerbed가 실행할 때 만든다. 여기서는 그릴 자리(MeshFilter·MeshRenderer)만 둔다.
            Transform planter = EnsureMeshChild(bedGo.transform, "Planter", planterMaterial, ShadowCastingMode.On);
            Transform soilRoot = EnsureMeshChild(bedGo.transform, "Soil", soilMaterial, ShadowCastingMode.On);
            // 예전 흙 타일(칸마다 상자 하나)은 한 장짜리 흙 면으로 바뀌었다.
            for (int i = soilRoot.childCount - 1; i >= 0; i--)
            {
                if (soilRoot.GetChild(i).name.StartsWith("Soil_")) Object.DestroyImmediate(soilRoot.GetChild(i).gameObject);
            }

            Transform ground = EnsurePrimitiveChild(bedGo.transform, "ShadowGround", PrimitiveType.Quad,
                catcherMaterial, ShadowCastingMode.Off);

            var so = new SerializedObject(bed);
            so.FindProperty("planter").objectReferenceValue = planter;
            so.FindProperty("shadowMargin").floatValue = ShadowGroundMargin;
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
            return bed;
        }

        // ---- 작물 성장 (P2-03) ----

        private static void EnsureCropGrowth(Flowerbed bed, PomodoroStateMachine stateMachine, PomodoroTimer timer,
            GameManager gameManager)
        {
            if (bed == null) return;

            CropCatalog catalog = CropPlaceholderBuilder.Build(overwrite: false);

            // 수확 기록과 해금 판정, 시작 시 작물 확인에 쓴다.
            var gmSo = new SerializedObject(gameManager);
            gmSo.FindProperty("cropCatalog").objectReferenceValue = catalog;
            gmSo.ApplyModifiedPropertiesWithoutUndo();
            Material glow = EnsureGlowMaterial();

            ParticleSystem sprout = EnsureParticles(bed.transform, "SproutBurst", glow,
                ps => ConfigureBurst(ps, SaiunPalette.TeaGreen, minSpeed: 0.15f, maxSpeed: 0.4f));
            ParticleSystem harvest = EnsureParticles(bed.transform, "HarvestBurst", glow,
                ps => ConfigureBurst(ps, SaiunPalette.Eggshell, minSpeed: 0.3f, maxSpeed: 0.7f));
            ParticleSystem harvestGlow = EnsureParticles(bed.transform, "HarvestGlow", glow,
                ps => ConfigureGlow(ps, bed));

            var growth = EnsureComponent<CropGrowth>(bed.gameObject);
            var so = new SerializedObject(growth);
            so.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            so.FindProperty("timer").objectReferenceValue = timer;
            so.FindProperty("flowerbed").objectReferenceValue = bed;
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("sproutBurst").objectReferenceValue = sprout;
            so.FindProperty("harvestBurst").objectReferenceValue = harvest;
            so.FindProperty("harvestGlow").objectReferenceValue = harvestGlow;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- 자연 현상 (P2-05) ----

        private static void EnsureWeather(Camera camera, Transform backdropCanvas, PomodoroStateMachine stateMachine, Flowerbed bed)
        {
            GameObject weatherGo = GameObject.Find("Weather") ?? new GameObject("Weather");
            var weather = EnsureComponent<WeatherController>(weatherGo);
            var weatherSo = new SerializedObject(weather);
            weatherSo.FindProperty("stateMachine").objectReferenceValue = stateMachine;
            weatherSo.ApplyModifiedPropertiesWithoutUndo();

            EnsureClouds(backdropCanvas, weather);
            if (camera != null)
            {
                RainEffect rain = EnsureRain(camera, weather);
                if (bed != null) EnsureSplash(rain, bed);
                EnsureWind(camera, weather);
            }

            if (bed == null) return;

            // 작물은 바람을 따라 눕고 흔들린다.
            if (bed.TryGetComponent(out CropGrowth growth))
            {
                var growthSo = new SerializedObject(growth);
                growthSo.FindProperty("weather").objectReferenceValue = weather;
                growthSo.ApplyModifiedPropertiesWithoutUndo();
            }

            EnsureWeatherVane(bed, weather);
            EnsureSurfaceWetness(bed, weather);
        }

        // 뭉게구름: 유리 배경 바로 위, 3D 씬 뒤. Sky Layer에만 띄운다.
        private static void EnsureClouds(Transform backdropCanvas, WeatherController weather)
        {
            Transform existing = backdropCanvas.Find("Clouds");
            GameObject clouds = existing != null
                ? existing.gameObject
                : new GameObject("Clouds", typeof(RectTransform), typeof(CloudLayer));
            clouds.transform.SetParent(backdropCanvas, false);
            clouds.transform.SetSiblingIndex(1);   // DesktopGlass 바로 다음

            var rt = (RectTransform)clouds.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, SceneMetrics.SkyLayerHeight);

            // 예전 스프라이트 구름 틀(Image)은 셰이더 구름 틀(RawImage)로 바꾼다. 한 오브젝트에 Graphic은 하나뿐이다.
            Transform templateChild = clouds.transform.Find("CloudTemplate");
            if (templateChild != null && templateChild.GetComponent<RawImage>() == null)
            {
                Object.DestroyImmediate(templateChild.gameObject);
                templateChild = null;
            }
            GameObject template = templateChild != null
                ? templateChild.gameObject
                : new GameObject("CloudTemplate", typeof(RectTransform), typeof(RawImage));
            template.transform.SetParent(clouds.transform, false);
            var image = template.GetComponent<RawImage>();
            image.material = WeatherArtBuilder.EnsureCloudMaterial();
            image.raycastTarget = false;
            template.SetActive(false);

            var so = new SerializedObject(clouds.GetComponent<CloudLayer>());
            so.FindProperty("weather").objectReferenceValue = weather;
            so.FindProperty("sun").objectReferenceValue = Object.FindFirstObjectByType<SunOrbitController>();
            so.FindProperty("area").objectReferenceValue = rt;
            so.FindProperty("cloudTemplate").objectReferenceValue = image;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 구름이 시계 글자 뒤로 지나가면 옅어지게 한다. 시계는 구름보다 나중에 만들어져 따로 잇는다.
        private static void KeepCloudsOffHud(Transform backdropCanvas, Transform hud)
        {
            Transform clouds = backdropCanvas.Find("Clouds");
            if (clouds == null || hud == null) return;
            var texts = new System.Collections.Generic.List<TMP_Text>();
            foreach (string name in new[] { "PrimaryText", "SecondaryText" })
            {
                Transform child = hud.Find(name);
                if (child != null && child.TryGetComponent(out TMP_Text text)) texts.Add(text);
            }

            var so = new SerializedObject(clouds.GetComponent<CloudLayer>());
            SerializedProperty list = so.FindProperty("keepClear");
            list.arraySize = texts.Count;
            for (int i = 0; i < texts.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = texts[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 비: 카메라 자식으로 화면 위쪽 가장자리 밖에서 생겨 아래로 떨어진다.
        private static RainEffect EnsureRain(Camera camera, WeatherController weather)
        {
            Transform existing = camera.transform.Find("Rain");
            GameObject rainGo = existing != null ? existing.gameObject : new GameObject("Rain");
            rainGo.transform.SetParent(camera.transform, false);

            float halfHeight = SceneMetrics.PixelsToWorld(SceneMetrics.WindowHeight) / 2f;
            float halfWidth = SceneMetrics.PixelsToWorld(SceneMetrics.WindowWidth) / 2f;
            rainGo.transform.localPosition = new Vector3(0f, halfHeight + RainMargin / 2f, RainDepth);
            rainGo.transform.localRotation = Quaternion.identity;

            var rain = EnsureComponent<ParticleSystem>(rainGo);
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = rain.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 1f;
            // 창 아래 모서리에서 사라지게 한다.
            main.startLifetime = (halfHeight * 2f + RainMargin / 2f) / RainFallSpeed;
            main.startSpeed = 0f;
            main.startSize = RainDropSize;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = RainMaxParticles;

            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = 0f;

            // 바람으로 기울어도 화면 가장자리가 비지 않게 옆으로 넉넉히 뿌린다.
            ParticleSystem.ShapeModule shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3((halfWidth + RainMargin) * 2f, 0.05f, 0.5f);

            ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = 0f;
            velocity.y = -RainFallSpeed;
            velocity.z = 0f;

            var renderer = rainGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = RainStreakPerSpeed;
            renderer.lengthScale = 1f;
            renderer.cameraVelocityScale = 0f;
            renderer.sharedMaterial = WeatherArtBuilder.EnsureRainMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var effect = EnsureComponent<RainEffect>(rainGo);
            var so = new SerializedObject(effect);
            so.FindProperty("weather").objectReferenceValue = weather;
            so.FindProperty("rain").objectReferenceValue = rain;
            so.FindProperty("fallSpeed").floatValue = RainFallSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();
            return effect;
        }

        // 빗방울이 화분·흙에 부딪혀 튀는 물방울. RainEffect가 화단 위 아무 곳에 Emit한다.
        private static void EnsureSplash(RainEffect rain, Flowerbed bed)
        {
            ParticleSystem splash = EnsureParticles(bed.transform, "RainSplash", WeatherArtBuilder.EnsureRainMaterial(), ps =>
            {
                ParticleSystem.MainModule main = ps.main;
                main.duration = 1f;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(SplashLifetime * 0.7f, SplashLifetime);
                main.startSpeed = new ParticleSystem.MinMaxCurve(SplashSpeedMin, SplashSpeedMax);
                main.startSize = new ParticleSystem.MinMaxCurve(SplashDropSize * 0.6f, SplashDropSize);
                main.startColor = Color.white;
                main.gravityModifier = SplashGravity;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = RainMaxParticles / 4;

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTime = 0f;

                // 위로 벌어지는 작은 왕관 모양
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.01f;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                fade.enabled = true;
                fade.color = AlphaGradient(new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f));
            });

            Vector2 grid = bed.GridSize;
            float rim = bed.RimWidth * 2f;
            var so = new SerializedObject(rain);
            so.FindProperty("splash").objectReferenceValue = splash;
            so.FindProperty("splashArea").objectReferenceValue = bed.transform;
            so.FindProperty("splashSize").vector2Value = new Vector2(grid.x + rim, grid.y + rim);
            so.FindProperty("splashHeight").floatValue = bed.SurfaceHeight;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 눈에 보이는 바람: 카메라 자식이라 화면 기준으로 흐른다. 카드 안에서만 지나간다.
        private static void EnsureWind(Camera camera, WeatherController weather)
        {
            Transform existing = camera.transform.Find("Wind");
            GameObject windGo = existing != null ? existing.gameObject : new GameObject("Wind");
            windGo.transform.SetParent(camera.transform, false);
            windGo.transform.localPosition = new Vector3(0f, WindBandLift, WindDepth);
            windGo.transform.localRotation = Quaternion.identity;

            float cardWidth = SceneMetrics.PixelsToWorld(SceneMetrics.WindowWidth);
            float bandHeight = SceneMetrics.PixelsToWorld(SceneMetrics.WindowHeight) * WindBandHeight;

            Material windMaterial = WeatherArtBuilder.EnsureWindMaterial();
            ParticleSystem streaks = EnsureParticles(windGo.transform, "Streaks", windMaterial, ps =>
            {
                ParticleSystem.MainModule main = ps.main;
                main.duration = 1f;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(WindStreakLifetimeMin, WindStreakLifetimeMax);
                main.startSpeed = 0f;
                main.startSize = WindStreakWidth;
                main.startColor = new Color(1f, 1f, 1f, WindStreakAlpha);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                // 머리 알갱이는 그리지 않고 꼬리만 그린다. 가운데가 굵고 양 끝이 가늘다.
                ParticleSystem.TrailModule trails = ps.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.PerParticle;
                trails.ratio = 1f;
                trails.lifetime = WindStreakTrail;
                trails.minVertexDistance = WindStreakVertexSpacing;
                trails.worldSpace = false;
                trails.dieWithParticles = true;
                trails.inheritParticleColor = true;
                trails.sizeAffectsWidth = true;
                trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)));

                ParticleSystem.NoiseModule noise = ps.noise;
                noise.enabled = true;
                noise.strength = WindStreakWave;
                noise.frequency = WindStreakWaveFrequency;
                noise.scrollSpeed = WindStreakWaveFrequency;
                noise.damping = true;

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTime = 0f;

                // 카드 전체에서 생겨 잠깐 흐르다 사라진다.
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = Vector3.zero;
                shape.scale = new Vector3(cardWidth, bandHeight, 0.3f);

                ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = 0f;
                velocity.y = 0f;
                velocity.z = 0f;

                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                fade.enabled = true;
                fade.color = AlphaGradient(
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f));
            });
            var streakRenderer = streaks.GetComponent<ParticleSystemRenderer>();
            streakRenderer.renderMode = ParticleSystemRenderMode.None;
            streakRenderer.trailMaterial = windMaterial;

            ParticleSystem leaves = EnsureParticles(windGo.transform, "Leaves", WeatherArtBuilder.EnsureLeafMaterial(), ps =>
            {
                ParticleSystem.MainModule main = ps.main;
                main.duration = 1f;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = WindLeafLifetime;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(WindLeafSizeMin, WindLeafSizeMax);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(SaiunPalette.JungleTeal, SaiunPalette.TeaGreen);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTime = 0f;

                // 바람이 오는 쪽 가장자리의 세로 띠. 가로 위치는 WindEffect가 바람 방향에 맞춘다.
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = Vector3.zero;
                shape.scale = new Vector3(0.1f, bandHeight, 0.3f);

                ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = 0f;
                velocity.y = 0f;
                velocity.z = 0f;

                ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-WindLeafSpin, WindLeafSpin);

                // 난류로 오르내리며 팔랑인다.
                ParticleSystem.NoiseModule noise = ps.noise;
                noise.enabled = true;
                noise.strength = WindLeafFlutter;
                noise.frequency = WindLeafFlutterFrequency;
                noise.scrollSpeed = WindLeafFlutterFrequency;
                noise.damping = true;

                ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
                fade.enabled = true;
                fade.color = AlphaGradient(
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f),
                    new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f));
            });

            var effect = EnsureComponent<WindEffect>(windGo);
            var so = new SerializedObject(effect);
            so.FindProperty("weather").objectReferenceValue = weather;
            so.FindProperty("streaks").objectReferenceValue = streaks;
            so.FindProperty("leaves").objectReferenceValue = leaves;
            so.FindProperty("edgeOffset").floatValue = cardWidth / 2f + RainMargin / 2f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 풍향계와 풍속계: 화분 뒤 모서리(+X,+Z) 테두리 위에 선다. 기둥 그림자가 해를 따라 늘었다 줄었다 한다.
        private static void EnsureWeatherVane(Flowerbed bed, WeatherController weather)
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null) return;
            Material metal = EnsureMaterial(VaneMaterialPath, lit, m =>
            {
                m.SetColor("_BaseColor", SaiunPalette.DeepJungle);
                m.SetFloat("_Smoothness", VaneSmoothness);
            });
            Material accent = EnsureMaterial(VaneAccentMaterialPath, lit, m =>
            {
                m.SetColor("_BaseColor", SaiunPalette.TropicalTeal);
                m.SetFloat("_Smoothness", VaneSmoothness);
            });

            Transform root = EnsureChild(bed.transform, "WeatherVane");
            root.localPosition = bed.RimCorner;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            // Cylinder 기본 높이는 2다.
            Place(EnsurePrimitiveChild(root, "Pole", PrimitiveType.Cylinder, metal, ShadowCastingMode.On),
                new Vector3(0f, VanePoleHeight / 2f, 0f), Quaternion.identity,
                new Vector3(VanePoleThickness, VanePoleHeight / 2f, VanePoleThickness));

            // 동서남북 막대: 방향을 읽는 기준. 월드 축에 고정이다.
            float compassY = VanePoleHeight * VaneCompassHeight;
            Place(EnsurePrimitiveChild(root, "CompassX", PrimitiveType.Cube, metal, ShadowCastingMode.On),
                new Vector3(0f, compassY, 0f), Quaternion.identity,
                new Vector3(VaneCompassLength, VaneBarThickness / 2f, VaneBarThickness / 2f));
            Place(EnsurePrimitiveChild(root, "CompassZ", PrimitiveType.Cube, metal, ShadowCastingMode.On),
                new Vector3(0f, compassY, 0f), Quaternion.identity,
                new Vector3(VaneBarThickness / 2f, VaneBarThickness / 2f, VaneCompassLength));

            // 화살표: 앞(+Z)이 화살촉, 뒤가 바람을 받는 꼬리 날개다.
            Transform vane = EnsureChild(root, "Vane");
            vane.localPosition = new Vector3(0f, VanePoleHeight, 0f);
            vane.localScale = Vector3.one;
            Place(EnsurePrimitiveChild(vane, "Shaft", PrimitiveType.Cube, metal, ShadowCastingMode.On),
                Vector3.zero, Quaternion.identity,
                new Vector3(VaneBarThickness, VaneBarThickness, VaneShaftLength));
            Place(EnsurePrimitiveChild(vane, "Head", PrimitiveType.Cube, accent, ShadowCastingMode.On),
                new Vector3(0f, 0f, VaneShaftLength / 2f), Quaternion.Euler(0f, 45f, 0f),
                new Vector3(VaneHeadSize, VaneBarThickness, VaneHeadSize));
            Place(EnsurePrimitiveChild(vane, "Tail", PrimitiveType.Cube, accent, ShadowCastingMode.On),
                new Vector3(0f, VaneTailHeight / 4f, -VaneShaftLength / 2f + VaneTailLength / 2f), Quaternion.identity,
                new Vector3(VaneBarThickness / 2f, VaneTailHeight, VaneTailLength));

            // 풍속계: 화살표 위에서 컵 세 개가 돈다.
            Place(EnsurePrimitiveChild(root, "CupStem", PrimitiveType.Cylinder, metal, ShadowCastingMode.On),
                new Vector3(0f, VanePoleHeight + CupsLift / 2f, 0f), Quaternion.identity,
                new Vector3(VaneBarThickness, CupsLift / 2f, VaneBarThickness));
            Transform cups = EnsureChild(root, "Cups");
            cups.localPosition = new Vector3(0f, VanePoleHeight + CupsLift, 0f);
            cups.localScale = Vector3.one;
            for (int i = 0; i < CupCount; i++)
            {
                Transform arm = EnsureChild(cups, $"Arm_{i}");
                arm.localPosition = Vector3.zero;
                arm.localRotation = Quaternion.Euler(0f, 360f / CupCount * i, 0f);
                arm.localScale = Vector3.one;
                Place(EnsurePrimitiveChild(arm, "Rod", PrimitiveType.Cube, metal, ShadowCastingMode.On),
                    new Vector3(0f, 0f, CupArmLength / 2f), Quaternion.identity,
                    new Vector3(VaneBarThickness / 2f, VaneBarThickness / 2f, CupArmLength));
                // 접선 방향으로 납작한 컵. 바람을 받는 오목한 면이 한쪽을 향한다.
                Place(EnsurePrimitiveChild(arm, "Cup", PrimitiveType.Sphere, accent, ShadowCastingMode.On),
                    new Vector3(0f, 0f, CupArmLength), Quaternion.identity,
                    new Vector3(CupSize / 2f, CupSize, CupSize));
            }

            var component = EnsureComponent<WeatherVane>(root.gameObject);
            var so = new SerializedObject(component);
            so.FindProperty("weather").objectReferenceValue = weather;
            so.FindProperty("vane").objectReferenceValue = vane;
            so.FindProperty("cups").objectReferenceValue = cups;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 비에 젖는 흙·화분. 작물은 런타임에 생겨서 넣지 않는다.
        private static void EnsureSurfaceWetness(Flowerbed bed, WeatherController weather)
        {
            var surfaces = new System.Collections.Generic.List<Renderer>();
            Transform planter = bed.transform.Find("Planter");
            if (planter != null && planter.TryGetComponent(out Renderer planterRenderer)) surfaces.Add(planterRenderer);
            Transform soil = bed.transform.Find("Soil");
            if (soil != null) surfaces.AddRange(soil.GetComponentsInChildren<Renderer>(true));

            var wetness = EnsureComponent<SurfaceWetness>(bed.gameObject);
            var so = new SerializedObject(wetness);
            so.FindProperty("weather").objectReferenceValue = weather;
            SerializedProperty list = so.FindProperty("surfaces");
            list.arraySize = surfaces.Count;
            for (int i = 0; i < surfaces.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = surfaces[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Place(Transform target, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }

        // 빛 알갱이용 가산 재질. URP 머티리얼 인스펙터가 블렌드 모드를 고를 때 넣는 값을 직접 넣는다.
        private static Material EnsureGlowMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                Debug.LogError("SaiunSceneBuilder: URP 파티클 셰이더를 찾지 못했습니다.");
                return null;
            }

            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(GlowMaterialPath) };
            material.SetFloat("_Surface", 1f);   // Transparent
            material.SetFloat("_Blend", 2f);     // Additive
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            // 1을 넘겨 블룸(임계 1)에 걸리게 한다. 작은 알갱이가 빛나 보인다.
            material.SetColor("_BaseColor", Color.white * GlowIntensity);

            var texture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else Debug.LogWarning("SaiunSceneBuilder: 기본 파티클 텍스처가 없어 사각 알갱이로 그립니다.");

            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, GlowMaterialPath);
            return material;
        }

        private static ParticleSystem EnsureParticles(Transform parent, string name, Material material,
            System.Action<ParticleSystem> configure)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);

            var system = EnsureComponent<ParticleSystem>(go);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            configure(system);
            return system;
        }

        // 포기마다 한 번 튀었다 사라지는 알갱이. CropGrowth가 위치를 정해 Emit한다.
        private static void ConfigureBurst(ParticleSystem system, Color color, float minSpeed, float maxSpeed)
        {
            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = color;
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            // 반구가 위를 향하게 눕힌다.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.04f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = AlphaGradient(new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f));

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
        }

        // 수확 가능 상태에서 화단 위로 천천히 떠오르는 빛. 범위는 CropGrowth가 켤 때 화단에 맞춘다.
        private static void ConfigureGlow(ParticleSystem system, Flowerbed bed)
        {
            system.transform.localPosition = new Vector3(0f, bed.SurfaceHeight, 0f);

            ParticleSystem.MainModule main = system.main;
            main.duration = 2f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startColor = SaiunPalette.Eggshell;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 18f;

            Vector2 grid = bed.GridSize;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(grid.x, 0.02f, grid.y);

            // 세 축의 곡선 모드가 같아야 한다.
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = AlphaGradient(
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f));
        }

        private static ParticleSystem.MinMaxGradient AlphaGradient(params GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphas);
            return new ParticleSystem.MinMaxGradient(gradient);
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

        // 코드로 만든 메시를 그릴 자식. 기본 도형 메시나 콜라이더 없이 MeshFilter·MeshRenderer만 둔다.
        private static Transform EnsureMeshChild(Transform parent, string name, Material material, ShadowCastingMode shadows)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            EnsureComponent<MeshFilter>(go);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = EnsureComponent<MeshRenderer>(go);
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
