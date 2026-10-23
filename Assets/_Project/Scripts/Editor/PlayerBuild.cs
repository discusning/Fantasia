using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fantasia.Editor
{
    // D01 "실행 가능한 테스트 빌드" — Windows 실행 파일을 Builds/Windows/에 만든다.
    // 자동 플레이 점검: Builds/Windows/Fantasia.exe -autoplay -autoplay-out <폴더> (Core/AutoPlayBot).
    public static class PlayerBuild
    {
        private const string OutputPath = "Builds/Windows/Fantasia.exe";

        [MenuItem("Fantasia/Build/Windows Test Build")]
        public static void BuildWindows()
        {
            EnsureRuntimeShaders();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });

            var summary = report.summary;
            Debug.Log($"[PlayerBuild] {summary.result} · 씬 {scenes.Length}개 ({string.Join(", ", scenes)}) · {summary.totalSize / (1024 * 1024)}MB · 오류 {summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        // 런타임에 Shader.Find로 만드는 재질(임시 인형·타일 Standard, 대상 링 Sprites/Default)이
        // 빌드에서 빠져 분홍색이 되지 않게 "항상 포함" 목록에 넣는다.
        private static void EnsureRuntimeShaders()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var serialized = new SerializedObject(settings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "Standard", "Sprites/Default" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
