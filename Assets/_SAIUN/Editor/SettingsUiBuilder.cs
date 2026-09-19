using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 세션 설정 패널과 시스템 설정 화면을 씬에 조립한다 (P4-04, 사양서 v1.1 12장).
    /// 이미 있으면 참조만 다시 연결하고, rebuild면 지우고 새로 만든다.
    /// 폰트·치수는 실측 대기값이라 임시값이다.
    /// </summary>
    public static class SettingsUiBuilder
    {
        private const string GearIconPath = "Assets/_SAIUN/Art/Textures/icon_gear.png";
        private const string RingSpritePath = "Assets/_SAIUN/Art/Textures/ui_ring.png";
        private const int RingSize = 48;
        private const float RingRadius = 12f;
        private const float RingThickness = 3f;
        private const int RingBorder = 16;
        private const int GearIconSize = 64;
        private const int GearTeeth = 8;
        private const int GearSupersample = 4;

        // ---- 임시 레이아웃값 (실측 대기) ----
        private const int Padding = 16;
        private const float Spacing = 8f;
        private const float RowHeight = 34f;
        private const float ListRowHeight = 30f;
        private const float LabelWidth = 112f;
        private const float NumberWidth = 52f;
        private const float SliderHeight = 20f;
        private const float InputHeight = 28f;
        private const float SmallButtonWidth = 64f;
        private const float IconButtonSize = 32f;
        private const float IconMargin = 8f;
        private const float HeaderHeight = 48f;
        private const float SessionPanelHeight = 332f;
        private const float CropButtonHeight = 52f;
        private const int CropColumns = 4;
        private const float ToggleBoxSize = 22f;
        private const float ScrollbarWidth = 6f;
        private const float ScrollSensitivity = 30f;

        private const float TitleFontSize = 18f;
        private const float SectionFontSize = 17f;
        private const float BodyFontSize = 15f;
        private const float SmallFontSize = 12f;
        private const float IconFontSize = 22f;

        private static readonly Vector2 TutorialCardSize = new Vector2(400f, 236f);
        private const float TutorialCardOffsetY = 40f;
        private const float TutorialBodyHeight = 92f;
        private const float TutorialButtonWidth = 120f;
        private static readonly Color TutorialDimColor = SaiunPalette.WithAlpha(SaiunPalette.DeepJungle, 0.6f);
        private static readonly Vector2 DialogCardSize = new Vector2(400f, 200f);
        private const float DialogBodyHeight = 70f;

        private static readonly Color PanelColor = SaiunPalette.BottomBarBackground;
        // 설정 글자 뒤로 시계·화단이 비치면 읽기 어려워 불투명하게 둔다.
        private static readonly Color ScreenColor = SaiunPalette.DeepJungle;
        private static readonly Color SubtleColor = SaiunPalette.WithAlpha(SaiunPalette.TeaGreen, 0.35f);
        private static readonly Color RowColor = SaiunPalette.WithAlpha(SaiunPalette.JungleTeal, 0.35f);

        private static TMP_FontAsset s_font;

        private const string ScenePath = "Assets/_SAIUN/Scenes/Main.unity";
        private const string FontAssetPath = "Assets/_SAIUN/Art/Fonts/MalgunGothic SDF.asset";

        // 레이아웃을 고친 뒤 기존 패널·설정 화면을 지우고 새로 만든다. 배치 모드에서도 쓴다.
        [MenuItem("SAIUN/Rebuild Settings UI")]
        public static void RebuildInMainScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject canvasRoot = GameObject.Find("UICanvas");
            // UI는 캔버스 아래 카드(480×680) 안에 있다.
            Transform canvas = canvasRoot != null ? canvasRoot.transform.Find("Card") ?? canvasRoot.transform : null;
            var gameManager = Object.FindFirstObjectByType<GameManager>();
            var bar = Object.FindFirstObjectByType<BottomBarView>();
            if (canvas == null || gameManager == null)
            {
                Debug.LogError("SettingsUiBuilder: 메인 씬에 UICanvas나 GameManager가 없습니다. Setup Main Scene을 먼저 실행하세요.");
                return;
            }

            Build(canvas, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath), gameManager, bar, rebuild: true);

            // 알림·유리 테두리·모서리 지우개는 설정 화면·튜토리얼보다 위에 있어야 한다.
            canvas.Find("ScreenAlert")?.SetAsLastSibling();
            canvas.Find("GlassRim")?.SetAsLastSibling();
            canvas.Find("CardCorners")?.SetAsLastSibling();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void Build(Transform canvas, TMP_FontAsset font, GameManager gameManager, BottomBarView bar, bool rebuild)
        {
            s_font = font;
            SessionPanelView panel = EnsureSessionPanel(canvas, gameManager, rebuild);
            EnsureSettingsScreen(canvas, gameManager, rebuild);
            EnsureTutorial(canvas, gameManager, bar, rebuild);
            canvas.Find("Dialog")?.SetAsLastSibling();

            if (bar != null)
            {
                var so = new SerializedObject(bar);
                so.FindProperty("sessionPanel").objectReferenceValue = panel;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---- 세션 설정 패널 (12-1) ----

        private static SessionPanelView EnsureSessionPanel(Transform canvas, GameManager gameManager, bool rebuild)
        {
            Transform existing = canvas.Find("SessionPanel");
            if (existing != null && rebuild)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing != null)
            {
                existing.SetAsLastSibling();
                var kept = existing.GetComponent<SessionPanelView>();
                SetReference(kept, "gameManager", gameManager);
                return kept;
            }

            RectTransform root = NewUi("SessionPanel", canvas);
            Stretch(root);
            var view = root.gameObject.AddComponent<SessionPanelView>();

            // 하단 바 바로 위에 붙는다.
            RectTransform panel = NewUi("Panel", root);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, SceneMetrics.BottomBarHeight);
            panel.sizeDelta = new Vector2(0f, SessionPanelHeight);
            AddImage(panel, PanelColor, raycast: true);
            VerticalStack(panel, new RectOffset(Padding, Padding, 12, 12), Spacing);

            Size(Text(panel, "Title", "세션 설정", TitleFontSize, SaiunPalette.HudText), height: 26f);
            Button close = IconButton(panel, "Close", "×");

            SliderField focus = SliderRow(panel, "Focus");
            SliderField shortBreak = SliderRow(panel, "ShortBreak");
            SliderField longBreak = SliderRow(panel, "LongBreak");
            SliderField sets = SliderRow(panel, "Sets");

            Size(Text(panel, "CropLabel", "작물", SmallFontSize + 1f, SaiunPalette.TeaGreen), height: 18f);
            RectTransform grid = NewUi("CropGrid", panel);
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            float cropWidth = (SceneMetrics.WindowWidth - Padding * 2 - Spacing * (CropColumns - 1)) / CropColumns;
            gridLayout.cellSize = new Vector2(cropWidth, CropButtonHeight);
            gridLayout.spacing = new Vector2(Spacing, Spacing);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = CropColumns;
            Size(grid, height: CropButtonHeight);
            Button cropTemplate = CropButtonTemplate(grid);

            TMP_Text hint = Size(Text(panel, "Hint", string.Empty, SmallFontSize, SaiunPalette.TeaGreen), height: 18f);

            var so = new SerializedObject(view);
            so.FindProperty("gameManager").objectReferenceValue = gameManager;
            so.FindProperty("panel").objectReferenceValue = panel.gameObject;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("focusField").objectReferenceValue = focus;
            so.FindProperty("shortBreakField").objectReferenceValue = shortBreak;
            so.FindProperty("longBreakField").objectReferenceValue = longBreak;
            so.FindProperty("setsField").objectReferenceValue = sets;
            so.FindProperty("cropGrid").objectReferenceValue = grid;
            so.FindProperty("cropTemplate").objectReferenceValue = cropTemplate;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.gameObject.SetActive(false);
            return view;
        }

        private static Button CropButtonTemplate(Transform grid)
        {
            Button button = ColoredButton(grid, "CropTemplate", string.Empty, BodyFontSize, RowColor, SaiunPalette.Eggshell);
            // 기본 버튼의 글자 하나를 이름·조건 두 줄로 바꾼다.
            Object.DestroyImmediate(button.GetComponentInChildren<TMP_Text>().gameObject);

            TMP_Text name = Text(button.transform, "Name", string.Empty, BodyFontSize, SaiunPalette.Eggshell, TextAlignmentOptions.Center);
            Anchor(name.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 1f));
            TMP_Text detail = Text(button.transform, "Detail", string.Empty, SmallFontSize - 1f, SaiunPalette.Eggshell, TextAlignmentOptions.Center);
            Anchor(detail.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.48f));

            button.gameObject.SetActive(false);
            return button;
        }

        // ---- 시스템 설정 화면 (12-2) ----

        private static void EnsureSettingsScreen(Transform canvas, GameManager gameManager, bool rebuild)
        {
            Transform existing = canvas.Find("Settings");
            if (existing != null && rebuild)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing != null)
            {
                existing.SetAsLastSibling();
                SetReference(existing.GetComponent<SettingsScreenView>(), "gameManager", gameManager);
                return;
            }

            RectTransform root = NewUi("Settings", canvas);
            Stretch(root);
            var view = root.gameObject.AddComponent<SettingsScreenView>();

            // 기어 아이콘: 창 오른쪽 위. 설정 화면을 열면 같은 자리에 닫기 버튼이 온다.
            RectTransform gear = NewUi("Gear", root);
            TopRight(gear);
            Image gearImage = AddImage(gear, SaiunPalette.WithAlpha(SaiunPalette.Eggshell, 0.85f), raycast: true);
            gearImage.sprite = EnsureGearIcon();
            gearImage.preserveAspect = true;
            var gearButton = gear.gameObject.AddComponent<Button>();
            gearButton.targetGraphic = gearImage;
            gearButton.colors = PaletteColors();

            // 하단 바는 가리지 않는다. 설정을 연 채로도 세션을 멈출 수 있게 한다.
            RectTransform screen = NewUi("Screen", root);
            Stretch(screen);
            screen.offsetMin = new Vector2(0f, SceneMetrics.BottomBarHeight);
            AddImage(screen, ScreenColor, raycast: true);

            TMP_Text title = Text(screen, "Title", "설정", TitleFontSize + 2f, SaiunPalette.HudText);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f));
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(Padding, -HeaderHeight);
            title.rectTransform.offsetMax = new Vector2(-Padding, 0f);
            Button close = IconButton(screen, "Close", "×");

            RectTransform content = ScrollArea(screen);

            // 사양서 12-2-1. 파일 선택은 Windows 대화상자로 연다.
            Section(content, "캐릭터");
            TMP_Text characterName = Size(Text(content, "CharacterName", string.Empty, BodyFontSize, SaiunPalette.HudText), height: 22f);
            Button importVrm = WideButton(content, "ImportVrm", "VRM 파일 불러오기 (200MB 이하)", SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Button resetCharacter = WideButton(content, "ResetCharacter", "기본 캐릭터로 초기화", RowColor, SaiunPalette.Eggshell);

            Section(content, "방해 앱");
            Sub(content, "블랙리스트");
            RectTransform blacklistRows = List(content, "BlacklistRows");
            (TMP_InputField blacklistInput, Button blacklistAdd) = InputWithButton(content, "BlacklistAdd", "프로세스 이름 (예: steam.exe)");
            (TMP_Dropdown running, Button runningAdd) = DropdownWithButton(content, "RunningAdd");
            Sub(content, "영구 화이트리스트");
            RectTransform whitelistRows = List(content, "WhitelistRows");
            (TMP_InputField whitelistInput, Button whitelistAdd) = InputWithButton(content, "WhitelistAdd", "항상 허용할 프로세스");
            SliderField grace = SliderRow(content, "Grace");

            // 사양서 12-2-3. 실제 날씨 연동은 API 키가 정해지면 붙인다.
            Section(content, "날씨");
            Toggle weatherRandom = ToggleRow(content, "WeatherRandom", "랜덤 날씨 (세트마다)");
            Toggle weatherFocus = ToggleRow(content, "WeatherFocus", "집중 상태 연동 (방해 앱이면 먹구름)");

            Section(content, "창");
            Toggle alwaysOnTop = ToggleRow(content, "AlwaysOnTop", "항상 위");
            Button resetPosition = WideButton(content, "ResetPosition", "창 위치 초기화", SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);

            Section(content, "사운드");
            Toggle sound = ToggleRow(content, "Sound", "효과음");
            SliderField volume = SliderRow(content, "Volume");

            Section(content, "데이터");
            TMP_Text focusTime = Size(Text(content, "FocusTime", string.Empty, BodyFontSize, SaiunPalette.HudText), height: 22f);
            TMP_Text harvestCount = Size(Text(content, "HarvestCount", string.Empty, BodyFontSize, SaiunPalette.HudText), height: 22f);
            TMP_Text inventory = Size(Text(content, "Inventory", string.Empty, BodyFontSize, SaiunPalette.HudText), height: 22f);
            Button tutorial = WideButton(content, "Tutorial", "튜토리얼 다시 보기", SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Button resetData = WideButton(content, "ResetData", "전체 데이터 초기화", SaiunPalette.Warning, SaiunPalette.OnMainPoint);

            Section(content, "앱");
            Button quit = WideButton(content, "Quit", "SAIUN 종료", RowColor, SaiunPalette.Eggshell);

            GameObject rowTemplate = ListRowTemplate(screen);
            MessageDialogView dialog = EnsureDialog(canvas, rebuild);

            var so = new SerializedObject(view);
            so.FindProperty("gameManager").objectReferenceValue = gameManager;
            so.FindProperty("screen").objectReferenceValue = screen.gameObject;
            so.FindProperty("openButton").objectReferenceValue = gearButton;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("characterText").objectReferenceValue = characterName;
            so.FindProperty("importVrmButton").objectReferenceValue = importVrm;
            so.FindProperty("resetCharacterButton").objectReferenceValue = resetCharacter;
            so.FindProperty("dialog").objectReferenceValue = dialog;
            so.FindProperty("blacklistRows").objectReferenceValue = blacklistRows;
            so.FindProperty("whitelistRows").objectReferenceValue = whitelistRows;
            so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
            so.FindProperty("blacklistInput").objectReferenceValue = blacklistInput;
            so.FindProperty("blacklistAddButton").objectReferenceValue = blacklistAdd;
            so.FindProperty("runningDropdown").objectReferenceValue = running;
            so.FindProperty("runningAddButton").objectReferenceValue = runningAdd;
            so.FindProperty("whitelistInput").objectReferenceValue = whitelistInput;
            so.FindProperty("whitelistAddButton").objectReferenceValue = whitelistAdd;
            so.FindProperty("graceField").objectReferenceValue = grace;
            so.FindProperty("weatherRandomToggle").objectReferenceValue = weatherRandom;
            so.FindProperty("weatherFocusToggle").objectReferenceValue = weatherFocus;
            so.FindProperty("alwaysOnTopToggle").objectReferenceValue = alwaysOnTop;
            so.FindProperty("resetPositionButton").objectReferenceValue = resetPosition;
            so.FindProperty("soundToggle").objectReferenceValue = sound;
            so.FindProperty("volumeField").objectReferenceValue = volume;
            so.FindProperty("focusTimeText").objectReferenceValue = focusTime;
            so.FindProperty("harvestCountText").objectReferenceValue = harvestCount;
            so.FindProperty("inventoryText").objectReferenceValue = inventory;
            so.FindProperty("tutorialButton").objectReferenceValue = tutorial;
            so.FindProperty("resetDataButton").objectReferenceValue = resetData;
            so.FindProperty("quitButton").objectReferenceValue = quit;
            so.FindProperty("resetDataLabel").objectReferenceValue = resetData.GetComponentInChildren<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();

            screen.gameObject.SetActive(false);
        }

        // ---- 확인·경고 다이얼로그 ----

        // 설정 화면·튜토리얼보다 위에 떠야 하므로 그 뒤(튜토리얼 다음)로 옮겨 둔다.
        private static MessageDialogView EnsureDialog(Transform canvas, bool rebuild)
        {
            Transform existing = canvas.Find("Dialog");
            if (existing != null && rebuild)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }
            if (existing != null) return existing.GetComponent<MessageDialogView>();

            RectTransform root = NewUi("Dialog", canvas);
            Stretch(root);
            var view = root.gameObject.AddComponent<MessageDialogView>();

            RectTransform overlay = NewUi("Overlay", root);
            Stretch(overlay);
            AddImage(overlay, TutorialDimColor, raycast: true);

            RectTransform card = NewUi("Card", overlay);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = DialogCardSize;
            Image cardImage = AddImage(card, SaiunPalette.DeepJungle, raycast: true);
            cardImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            cardImage.type = Image.Type.Sliced;
            VerticalStack(card, new RectOffset(20, 20, 16, 16), 8f);

            TMP_Text title = Size(Text(card, "Title", string.Empty, TitleFontSize, SaiunPalette.HudText), height: 28f);
            TMP_Text body = Size(Text(card, "Body", string.Empty, BodyFontSize, SaiunPalette.HudText, TextAlignmentOptions.TopLeft),
                height: DialogBodyHeight);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Overflow;

            RectTransform buttons = NewUi("Buttons", card);
            HorizontalStack(buttons, new RectOffset(0, 0, 0, 0));
            Size(buttons, height: RowHeight);
            Size(NewUi("Spacer", buttons), flexibleWidth: 1f);
            Button cancel = ColoredButton(buttons, "Cancel", "취소", BodyFontSize, Color.clear, SaiunPalette.TeaGreen);
            Size(cancel, width: TutorialButtonWidth - 20f, height: RowHeight);
            Button confirm = ColoredButton(buttons, "Confirm", "확인", BodyFontSize, SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Size(confirm, width: TutorialButtonWidth, height: RowHeight);

            var so = new SerializedObject(view);
            so.FindProperty("dialog").objectReferenceValue = overlay.gameObject;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("confirmLabel").objectReferenceValue = confirm.GetComponentInChildren<TMP_Text>();
            so.FindProperty("cancelButton").objectReferenceValue = cancel;
            so.FindProperty("cancelLabel").objectReferenceValue = cancel.GetComponentInChildren<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();

            overlay.gameObject.SetActive(false);
            return view;
        }

        // ---- 첫 실행 튜토리얼 (14장) ----

        private static void EnsureTutorial(Transform canvas, GameManager gameManager, BottomBarView bar, bool rebuild)
        {
            Transform existing = canvas.Find("Tutorial");
            if (existing != null && rebuild)
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            if (existing != null)
            {
                existing.SetAsLastSibling();
                SetReference(existing.GetComponent<TutorialView>(), "gameManager", gameManager);
                return;
            }

            RectTransform root = NewUi("Tutorial", canvas);
            Stretch(root);
            var view = root.gameObject.AddComponent<TutorialView>();

            RectTransform overlay = NewUi("Overlay", root);
            Stretch(overlay);
            // 뒤를 흐리게 덮고 클릭을 막는다.
            AddImage(overlay, TutorialDimColor, raycast: true);

            RectTransform highlight = NewUi("Highlight", overlay);
            Image ring = AddImage(highlight, SaiunPalette.Eggshell, raycast: false);
            ring.sprite = EnsureRingSprite();
            ring.type = Image.Type.Sliced;

            RectTransform card = NewUi("Card", overlay);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, TutorialCardOffsetY);
            card.sizeDelta = TutorialCardSize;
            Image cardImage = AddImage(card, SaiunPalette.DeepJungle, raycast: true);
            cardImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            cardImage.type = Image.Type.Sliced;
            VerticalStack(card, new RectOffset(20, 20, 16, 16), 6f);

            TMP_Text step = Size(Text(card, "Step", string.Empty, SmallFontSize, SaiunPalette.TeaGreen), height: 18f);
            TMP_Text title = Size(Text(card, "Title", string.Empty, TitleFontSize + 2f, SaiunPalette.HudText), height: 30f);
            TMP_Text body = Size(Text(card, "Body", string.Empty, BodyFontSize, SaiunPalette.HudText, TextAlignmentOptions.TopLeft),
                height: TutorialBodyHeight);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Overflow;

            RectTransform buttons = NewUi("Buttons", card);
            HorizontalStack(buttons, new RectOffset(0, 0, 0, 0));
            Size(buttons, height: RowHeight);
            Button skip = ColoredButton(buttons, "Skip", "건너뛰기", BodyFontSize, Color.clear, SaiunPalette.TeaGreen);
            Size(skip, width: TutorialButtonWidth - 20f, height: RowHeight);
            Size(NewUi("Spacer", buttons), flexibleWidth: 1f);
            Button next = ColoredButton(buttons, "Next", "다음", BodyFontSize, SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Size(next, width: TutorialButtonWidth, height: RowHeight);

            // 사양서 14장 세 단계. 캐릭터 항목은 P3에서 설정 화면에 붙는다.
            RectTransform gear = canvas.Find("Settings/Gear") as RectTransform;
            RectTransform start = bar != null && bar.PrimaryButton != null ? (RectTransform)bar.PrimaryButton.transform : null;
            var steps = new[]
            {
                new TutorialStep("캐릭터 설정",
                    "오른쪽 위 기어 아이콘에서 캐릭터를 바꿀 수 있어요.\n기본 캐릭터로 시작해도 괜찮아요.", gear),
                new TutorialStep("작물 심기",
                    "시작 버튼을 누르면 세션 설정이 열려요.\n작물 목록에서 심을 작물을 고르세요.", start),
                new TutorialStep("포모도로 시작",
                    "집중 시간과 세트 수를 정하고 시작하세요.\n집중 중에 방해 앱으로 넘어가 유예 시간 안에 돌아오지 않으면 작물이 시들어요.", start),
            };

            var so = new SerializedObject(view);
            so.FindProperty("gameManager").objectReferenceValue = gameManager;
            so.FindProperty("overlay").objectReferenceValue = overlay.gameObject;
            so.FindProperty("stepText").objectReferenceValue = step;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.FindProperty("nextButton").objectReferenceValue = next;
            so.FindProperty("nextLabel").objectReferenceValue = next.GetComponentInChildren<TMP_Text>();
            so.FindProperty("skipButton").objectReferenceValue = skip;
            so.FindProperty("highlight").objectReferenceValue = highlight;
            SerializedProperty stepsProperty = so.FindProperty("steps");
            stepsProperty.arraySize = steps.Length;
            for (int i = 0; i < steps.Length; i++)
            {
                SerializedProperty element = stepsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("title").stringValue = steps[i].Title;
                element.FindPropertyRelative("body").stringValue = steps[i].Body;
                element.FindPropertyRelative("target").objectReferenceValue = steps[i].Target;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            overlay.gameObject.SetActive(false);
        }

        // 둥근 모서리 테두리. 9-slice로 늘려 어떤 버튼에도 두른다.
        private static Sprite EnsureRingSprite()
        {
            if (!File.Exists(RingSpritePath))
            {
                var texture = new Texture2D(RingSize, RingSize, TextureFormat.RGBA32, false);
                var pixels = new Color32[RingSize * RingSize];
                float half = RingSize / 2f;
                for (int y = 0; y < RingSize; y++)
                {
                    for (int x = 0; x < RingSize; x++)
                    {
                        int inside = 0;
                        for (int sy = 0; sy < GearSupersample; sy++)
                        {
                            for (int sx = 0; sx < GearSupersample; sx++)
                            {
                                float px = x + (sx + 0.5f) / GearSupersample - half;
                                float py = y + (sy + 0.5f) / GearSupersample - half;
                                float inset = -RoundedBoxDistance(px, py, half, half, RingRadius);
                                if (inset >= 0f && inset <= RingThickness) inside++;
                            }
                        }
                        byte alpha = (byte)Mathf.RoundToInt(inside / (float)(GearSupersample * GearSupersample) * 255f);
                        pixels[y * RingSize + x] = new Color32(255, 255, 255, alpha);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(RingSpritePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(RingSpritePath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (AssetImporter.GetAtPath(RingSpritePath) is TextureImporter importer
                && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                    || importer.spriteBorder != Vector4.one * RingBorder))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = Vector4.one * RingBorder;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
        }

        /// <summary>둥근 사각형의 부호 있는 거리. 안쪽이 음수, 경계가 0이다.</summary>
        private static float RoundedBoxDistance(float px, float py, float halfWidth, float halfHeight, float radius)
        {
            float qx = Mathf.Abs(px) - (halfWidth - radius);
            float qy = Mathf.Abs(py) - (halfHeight - radius);
            float outsideX = Mathf.Max(qx, 0f);
            float outsideY = Mathf.Max(qy, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        // 설정 화면 본문. 세로로 길어지므로 스크롤한다.
        private static RectTransform ScrollArea(RectTransform screen)
        {
            GameObject scrollGo = DefaultControls.CreateScrollView(Ugui());
            scrollGo.name = "Scroll";
            scrollGo.transform.SetParent(screen, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            Stretch(scrollRt);
            scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);
            scrollGo.GetComponent<Image>().color = Color.clear;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ScrollSensitivity;
            if (scroll.horizontalScrollbar != null)
            {
                Object.DestroyImmediate(scroll.horizontalScrollbar.gameObject);
                scroll.horizontalScrollbar = null;
            }
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 0f;

            Scrollbar bar = scroll.verticalScrollbar;
            var barRt = (RectTransform)bar.transform;
            barRt.sizeDelta = new Vector2(ScrollbarWidth, barRt.sizeDelta.y);
            bar.GetComponent<Image>().color = Color.clear;
            bar.handleRect.GetComponent<Image>().color = SubtleColor;

            RectTransform content = scroll.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalStack(content, new RectOffset(Padding, Padding, 0, Padding), Spacing * 0.75f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        private static void Section(Transform parent, string text)
        {
            Size(Text(parent, $"Section_{text}", text, SectionFontSize, SaiunPalette.MainPoint), height: 32f);
        }

        private static void Sub(Transform parent, string text)
        {
            Size(Text(parent, $"Sub_{text}", text, SmallFontSize + 1f, SaiunPalette.TeaGreen), height: 20f);
        }

        private static RectTransform List(Transform parent, string name)
        {
            RectTransform list = NewUi(name, parent);
            VerticalStack(list, new RectOffset(0, 0, 0, 0), 4f);
            return list;
        }

        // 목록 한 줄: 프로세스 이름과 삭제 버튼. 화면 밖에 비활성으로 둔다.
        private static GameObject ListRowTemplate(Transform parent)
        {
            RectTransform row = NewUi("RowTemplate", parent);
            AddImage(row, RowColor, raycast: false);
            HorizontalStack(row, new RectOffset(10, 4, 2, 2));
            Size(row, height: ListRowHeight);

            TMP_Text name = Text(row, "Name", string.Empty, BodyFontSize, SaiunPalette.HudText);
            Size(name, flexibleWidth: 1f);
            Button remove = ColoredButton(row, "Remove", "삭제", SmallFontSize + 1f, Color.clear, SaiunPalette.TeaGreen);
            Size(remove, width: 52f, height: ListRowHeight - 4f);

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static (TMP_InputField, Button) InputWithButton(Transform parent, string name, string placeholder)
        {
            RectTransform row = NewUi(name, parent);
            HorizontalStack(row, new RectOffset(0, 0, 0, 0));
            Size(row, height: RowHeight);

            TMP_InputField input = Input(row, "Input", placeholder, TextAlignmentOptions.MidlineLeft);
            Size(input, flexibleWidth: 1f, height: InputHeight);
            Button add = ColoredButton(row, "Add", "추가", BodyFontSize, SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Size(add, width: SmallButtonWidth, height: InputHeight);
            return (input, add);
        }

        private static (TMP_Dropdown, Button) DropdownWithButton(Transform parent, string name)
        {
            RectTransform row = NewUi(name, parent);
            HorizontalStack(row, new RectOffset(0, 0, 0, 0));
            Size(row, height: RowHeight);

            GameObject dropdownGo = TMP_DefaultControls.CreateDropdown(Tmp());
            dropdownGo.name = "Dropdown";
            dropdownGo.transform.SetParent(row, false);
            var dropdown = dropdownGo.GetComponent<TMP_Dropdown>();
            foreach (TMP_Text text in dropdownGo.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = s_font;
                text.fontSize = BodyFontSize;
                text.color = SaiunPalette.InputText;
            }
            dropdownGo.GetComponent<Image>().color = SaiunPalette.InputBackground;
            dropdown.colors = PaletteColors();
            Size(dropdown, flexibleWidth: 1f, height: InputHeight);

            Button add = ColoredButton(row, "Add", "추가", BodyFontSize, SaiunPalette.MainPoint, SaiunPalette.OnMainPoint);
            Size(add, width: SmallButtonWidth, height: InputHeight);
            return (dropdown, add);
        }

        private static Toggle ToggleRow(Transform parent, string name, string label)
        {
            RectTransform row = NewUi(name, parent);
            HorizontalStack(row, new RectOffset(0, 0, 0, 0));
            Size(row, height: RowHeight);
            // 글자를 눌러도 켜지고 꺼지도록 줄 전체가 클릭을 받는다.
            AddImage(row, Color.clear, raycast: true);

            RectTransform box = NewUi("Box", row);
            Image boxImage = AddImage(box, SaiunPalette.InputBackground, raycast: true);
            boxImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            boxImage.type = Image.Type.Sliced;
            Size(box, width: ToggleBoxSize, height: ToggleBoxSize);

            RectTransform check = NewUi("Check", box);
            Stretch(check);
            Image checkImage = AddImage(check, SaiunPalette.DeepJungle, raycast: false);
            checkImage.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");

            TMP_Text text = Text(row, "Label", label, BodyFontSize, SaiunPalette.HudText);
            Size(text, flexibleWidth: 1f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.colors = PaletteColors();
            return toggle;
        }

        // ---- 공용 조각 ----

        private static SliderField SliderRow(Transform parent, string name)
        {
            RectTransform row = NewUi(name, parent);
            HorizontalStack(row, new RectOffset(0, 0, 0, 0));
            Size(row, height: RowHeight);

            TMP_Text label = Text(row, "Label", string.Empty, BodyFontSize, SaiunPalette.HudText);
            Size(label, width: LabelWidth);

            GameObject sliderGo = DefaultControls.CreateSlider(Ugui());
            sliderGo.transform.SetParent(row, false);
            var slider = sliderGo.GetComponent<Slider>();
            sliderGo.transform.Find("Background").GetComponent<Image>().color = SubtleColor;
            slider.fillRect.GetComponent<Image>().color = SaiunPalette.MainPoint;
            slider.handleRect.GetComponent<Image>().color = SaiunPalette.Eggshell;
            slider.colors = PaletteColors();
            Size(slider, flexibleWidth: 1f, height: SliderHeight);

            TMP_InputField input = Input(row, "Input", string.Empty, TextAlignmentOptions.Center);
            Size(input, width: NumberWidth, height: InputHeight);

            var field = row.gameObject.AddComponent<SliderField>();
            var so = new SerializedObject(field);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("slider").objectReferenceValue = slider;
            so.FindProperty("input").objectReferenceValue = input;
            so.ApplyModifiedPropertiesWithoutUndo();
            return field;
        }

        private static TMP_InputField Input(Transform parent, string name, string placeholder, TextAlignmentOptions alignment)
        {
            GameObject go = TMP_DefaultControls.CreateInputField(Tmp());
            go.name = name;
            go.transform.SetParent(parent, false);
            var input = go.GetComponent<TMP_InputField>();
            go.GetComponent<Image>().color = SaiunPalette.InputBackground;
            input.colors = PaletteColors();

            if (input.textComponent != null)
            {
                input.textComponent.font = s_font;
                input.textComponent.fontSize = BodyFontSize;
                input.textComponent.color = SaiunPalette.InputText;
                input.textComponent.alignment = alignment;
            }
            if (input.placeholder is TMP_Text hint)
            {
                hint.font = s_font;
                hint.fontSize = BodyFontSize;
                hint.color = SaiunPalette.WithAlpha(SaiunPalette.InputText, 0.5f);
                hint.alignment = alignment;
                hint.text = placeholder;
            }
            return input;
        }

        private static Button WideButton(Transform parent, string name, string label, Color background, Color foreground)
        {
            Button button = ColoredButton(parent, name, label, BodyFontSize, background, foreground);
            Size(button, height: RowHeight);
            return button;
        }

        private static Button ColoredButton(Transform parent, string name, string label, float fontSize, Color background, Color foreground)
        {
            GameObject go = TMP_DefaultControls.CreateButton(Tmp());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;

            var text = go.GetComponentInChildren<TMP_Text>();
            text.font = s_font;
            text.fontSize = fontSize;
            text.color = foreground;
            text.text = label;
            text.raycastTarget = false;

            var button = go.GetComponent<Button>();
            button.colors = PaletteColors();
            return button;
        }

        // 오른쪽 위 모서리의 글자 아이콘 버튼(닫기). 레이아웃에 끼지 않는다.
        private static Button IconButton(Transform parent, string name, string glyph)
        {
            Button button = ColoredButton(parent, name, glyph, IconFontSize, Color.clear, SaiunPalette.Eggshell);
            button.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            TopRight((RectTransform)button.transform);
            return button;
        }

        private static void TopRight(RectTransform rt)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-IconMargin, -IconMargin);
            rt.sizeDelta = new Vector2(IconButtonSize, IconButtonSize);
        }

        private static TMP_Text Text(Transform parent, string name, string text, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform rt = NewUi(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = s_font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.text = text;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static RectTransform NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image AddImage(RectTransform rt, Color color, bool raycast)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static void VerticalStack(RectTransform rt, RectOffset padding, float spacing)
        {
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void HorizontalStack(RectTransform rt, RectOffset padding)
        {
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        /// <summary>레이아웃 크기를 정한다. 음수는 그대로 둔다는 뜻이다.</summary>
        private static T Size<T>(T component, float width = -1f, float height = -1f, float flexibleWidth = -1f) where T : Component
        {
            // 에디터의 GetComponent는 없을 때 가짜 null을 돌려줘 ??로 거를 수 없다.
            if (!component.TryGetComponent(out LayoutElement element)) element = component.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = width;
            if (height >= 0f)
            {
                element.preferredHeight = height;
                element.minHeight = height;
            }
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            return component;
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rt)
        {
            Anchor(rt, Vector2.zero, Vector2.one);
        }

        private static void SetReference(Object target, string field, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Selectable 상태 틴트. 팔레트 색조를 지키려고 밝기·알파만 바꾼다(BottomBarView와 같다).
        private static ColorBlock PaletteColors()
        {
            ColorBlock block = ColorBlock.defaultColorBlock;
            block.normalColor = Color.white;
            block.highlightedColor = Color.white;
            block.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            block.selectedColor = Color.white;
            block.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            return block;
        }

        private static TMP_DefaultControls.Resources Tmp()
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

        private static DefaultControls.Resources Ugui()
        {
            return new DefaultControls.Resources
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

        // ---- 기어 아이콘 ----

        // 톱니 8개 달린 흰 기어를 직접 그린다. 외부 아이콘을 쓰지 않아 라이선스 걱정이 없다.
        private static Sprite EnsureGearIcon()
        {
            if (!File.Exists(GearIconPath))
            {
                string folder = Path.GetDirectoryName(GearIconPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var texture = new Texture2D(GearIconSize, GearIconSize, TextureFormat.RGBA32, false);
                var pixels = new Color32[GearIconSize * GearIconSize];
                for (int y = 0; y < GearIconSize; y++)
                {
                    for (int x = 0; x < GearIconSize; x++)
                    {
                        pixels[y * GearIconSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(GearCoverage(x, y) * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(GearIconPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(GearIconPath, ImportAssetOptions.ForceSynchronousImport);
            }

            // 스크립트로 Sprite로 바꾸면 Multiple 모드가 되어 스프라이트가 하나도 생기지 않는다. Single로 못 박는다.
            if (AssetImporter.GetAtPath(GearIconPath) is TextureImporter importer
                && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(GearIconPath);
        }

        // 한 픽셀을 여러 점으로 나눠 기어 안에 드는 비율을 구한다(가장자리 계단 방지).
        private static float GearCoverage(int x, int y)
        {
            const float toothRadius = 0.95f;
            const float bodyRadius = 0.72f;
            const float holeRadius = 0.3f;
            const float toothWidth = 0.42f;   // 톱니 한 칸 중 톱니가 차지하는 비율

            int inside = 0;
            for (int sy = 0; sy < GearSupersample; sy++)
            {
                for (int sx = 0; sx < GearSupersample; sx++)
                {
                    float px = (x + (sx + 0.5f) / GearSupersample) / GearIconSize * 2f - 1f;
                    float py = (y + (sy + 0.5f) / GearSupersample) / GearIconSize * 2f - 1f;
                    float radius = Mathf.Sqrt(px * px + py * py);
                    float turns = Mathf.Repeat(Mathf.Atan2(py, px) / (Mathf.PI * 2f) * GearTeeth, 1f);
                    bool onTooth = turns < toothWidth;
                    float outer = onTooth ? toothRadius : bodyRadius;
                    if (radius <= outer && radius >= holeRadius) inside++;
                }
            }
            return inside / (float)(GearSupersample * GearSupersample);
        }
    }
}
