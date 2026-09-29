using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 앱 글꼴 (2026-09-29, 사용자 선택 Sarasa Gothic K, SIL OFL 1.1).
    ///  - 글자: Tools/Fonts/subset_sarasa.py가 앱 고정 문구의 글자와 입력용 한글 완성형·가나·영문·기호만 남긴 TTF
    ///    (Art/Fonts/Sarasa, 굵기당 약 2 MB, 라이선스 OFL.txt 함께).
    ///  - 시계 큰 숫자는 Light, 나머지 글은 Regular. 둘 다 동적 아틀라스라 사용자가 친 글자도 같은 글꼴로 보인다.
    ///  - 여기 없는 글자(입력한 한자 등)는 Windows 글꼴이 대신 그린다: 맑은 고딕 → Yu Gothic.
    ///    시스템 글꼴은 앱에 넣지 않고 실행할 때 읽으므로 라이선스 문제가 없다.
    ///  - 빌드에는 편집기에서 채운 아틀라스를 싣지 않는다(빌드마다 비운다).
    /// </summary>
    public static class FontBuilder
    {
        public const string Folder = "Assets/_SAIUN/Art/Fonts";
        public const string UiFontPath = Folder + "/SarasaGothicK-Regular SDF.asset";
        public const string ClockFontPath = Folder + "/SarasaGothicK-Light SDF.asset";
        public const string LicensePath = Folder + "/Sarasa/OFL.txt";
        private const string UiSourcePath = Folder + "/Sarasa/SarasaGothicK-Regular.ttf";
        private const string ClockSourcePath = Folder + "/Sarasa/SarasaGothicK-Light.ttf";

        // 대체 글꼴: Windows에 깔린 글꼴을 실행할 때 읽는다.
        private const string KoreanFallbackPath = Folder + "/MalgunGothic SDF.asset";
        private const string KoreanFallbackFamily = "Malgun Gothic";
        private const string JapaneseFallbackPath = Folder + "/YuGothic SDF.asset";
        private const string JapaneseFallbackFamily = "Yu Gothic";
        private const string RegularStyle = "Regular";

        // 아틀라스: 글은 64 pt로 구워 2048 장에, 시계는 큰 글자라 96 pt로 구워 1024 장에(숫자 몇 개뿐이다).
        private const int UiSamplingSize = 64;
        private const int UiPadding = 6;
        private const int UiAtlasSize = 2048;
        private const int ClockSamplingSize = 96;
        private const int ClockPadding = 9;
        private const int ClockAtlasSize = 1024;

        // 시계 큰 숫자(TimerHud의 PrimaryText)
        public const string ClockTextName = "PrimaryText";
        public const string ClockParentName = "TimerHud";

        /// <summary>글 글꼴(Sarasa Regular).</summary>
        public static TMP_FontAsset EnsureUiFont() => Ensure(UiFontPath, UiSourcePath, UiSamplingSize, UiPadding, UiAtlasSize);

        /// <summary>시계 글꼴(Sarasa Light).</summary>
        public static TMP_FontAsset EnsureClockFont() => Ensure(ClockFontPath, ClockSourcePath, ClockSamplingSize, ClockPadding, ClockAtlasSize);

        /// <summary>이 글자에 입힐 글꼴. 시계 큰 숫자만 시계 글꼴이다.</summary>
        public static TMP_FontAsset FontFor(TMP_Text text, TMP_FontAsset ui, TMP_FontAsset clock)
        {
            Transform parent = text.transform.parent;
            bool isClock = text.name == ClockTextName && parent != null && parent.name == ClockParentName;
            return isClock ? clock : ui;
        }

        /// <summary>프리팹들과 열린 씬의 모든 글자에 앱 글꼴을 입히고, TMP 기본 글꼴도 바꾼다.</summary>
        public static void Apply(IEnumerable<string> prefabPaths)
        {
            TMP_FontAsset ui = EnsureUiFont();
            TMP_FontAsset clock = EnsureClockFont();
            if (ui == null || clock == null) return;

            foreach (string path in prefabPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (TMP_Text text in contents.GetComponentsInChildren<TMP_Text>(true)) changed |= Assign(text, ui, clock);
                    foreach (TMP_InputField input in contents.GetComponentsInChildren<TMP_InputField>(true)) changed |= AssignInput(input, ui);
                    if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (Assign(text, ui, clock)) EditorUtility.SetDirty(text);
            }
            foreach (TMP_InputField input in Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (AssignInput(input, ui)) EditorUtility.SetDirty(input);
            }

            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                SerializedProperty defaultFont = so.FindProperty("m_defaultFontAsset");
                if (defaultFont != null && defaultFont.objectReferenceValue != ui)
                {
                    defaultFont.objectReferenceValue = ui;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // 글꼴이 다르면 바꾸고 그 글꼴의 기본 재질을 입힌다(그림자 재질은 씬 조립기가 따로 입힌다).
        private static bool Assign(TMP_Text text, TMP_FontAsset ui, TMP_FontAsset clock)
        {
            TMP_FontAsset target = FontFor(text, ui, clock);
            if (text.font == target) return false;
            text.font = target;
            text.fontSharedMaterial = target.material;
            return true;
        }

        // 입력창이 새 글자 칸을 만들 때 쓰는 글꼴도 맞춘다(글자 칸 자체는 Assign이 맞춘다).
        private static bool AssignInput(TMP_InputField input, TMP_FontAsset ui)
        {
            var so = new SerializedObject(input);
            SerializedProperty global = so.FindProperty("m_GlobalFontAsset");
            if (global == null || global.objectReferenceValue == ui) return false;
            global.objectReferenceValue = ui;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static TMP_FontAsset Ensure(string path, string sourcePath, int sampling, int padding, int atlas)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
                if (source == null)
                {
                    Debug.LogError($"FontBuilder: 글꼴 파일 {sourcePath}이(가) 없습니다. Tools/Fonts/subset_sarasa.py로 만드세요.");
                    return null;
                }
                asset = TMP_FontAsset.CreateFontAsset(source, sampling, padding, GlyphRenderMode.SDFAA, atlas, atlas,
                    AtlasPopulationMode.Dynamic, true);
                if (asset == null) return null;
                SaveNew(asset, path);
            }

            // 대체 글꼴과 빌드 설정은 매번 맞춘다.
            var fallbacks = new List<TMP_FontAsset>();
            TMP_FontAsset korean = EnsureOsFont(KoreanFallbackPath, KoreanFallbackFamily);
            TMP_FontAsset japanese = EnsureOsFont(JapaneseFallbackPath, JapaneseFallbackFamily);
            if (korean != null) fallbacks.Add(korean);
            if (japanese != null) fallbacks.Add(japanese);
            asset.fallbackFontAssetTable = fallbacks;

            var so = new SerializedObject(asset);
            SerializedProperty clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null) clear.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // Windows 글꼴을 이름으로 참조하는 글꼴 에셋(글꼴 파일은 저장소에 넣지 않는다). 없는 시스템이면 null.
        private static TMP_FontAsset EnsureOsFont(string path, string family)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(family, RegularStyle);
            if (asset == null)
            {
                Debug.LogWarning($"FontBuilder: 시스템 글꼴 '{family}'를 찾지 못해 대체 글꼴에서 뺍니다.");
                return null;
            }
            SaveNew(asset, path);
            return asset;
        }

        private static void SaveNew(TMP_FontAsset asset, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);

            Texture2D atlasTexture = asset.atlasTextures[0];
            atlasTexture.name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlasTexture, asset);

            Material material = asset.material;
            material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(material, asset);
            AssetDatabase.SaveAssets();
        }
    }
}
