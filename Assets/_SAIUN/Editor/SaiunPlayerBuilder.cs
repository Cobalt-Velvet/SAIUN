using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// Windows 플레이어 빌드.
    /// 메뉴 SAIUN/Build Windows Player 또는 배치 모드 -executeMethod _SAIUN.Editor.SaiunPlayerBuilder.BuildWindows 로 실행한다.
    /// 출력 폴더 Build/ 는 gitignore 대상이다.
    /// </summary>
    public static class SaiunPlayerBuilder
    {
        private const string ScenePath = "Assets/_SAIUN/Scenes/Main.unity";
        private const string OutputDir = "Build/Windows";
        private const string ExeName = "SAIUN.exe";
        private const long BytesPerMegabyte = 1024 * 1024;

        [MenuItem("SAIUN/Build Windows Player")]
        public static void BuildWindows()
        {
            string outputPath = Path.Combine(OutputDir, ExeName);
            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"SaiunPlayerBuilder: 빌드 성공 {outputPath} ({summary.totalSize / BytesPerMegabyte} MB, {summary.totalTime.TotalSeconds:F0}s)");
                return;
            }

            Debug.LogError($"SaiunPlayerBuilder: 빌드 실패 - {summary.result}, 에러 {summary.totalErrors}건");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
