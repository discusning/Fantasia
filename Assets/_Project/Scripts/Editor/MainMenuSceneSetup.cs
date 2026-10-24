using System.Linq;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 메인 메뉴 씬 생성 + 빌드 첫 씬으로 등록. 화면은 MainMenuController가 코드로 만든다.
    public static class MainMenuSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/MainMenu/MainMenu.unity";

        [MenuItem("Fantasia/Setup Main Menu Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.045f, 0.06f);
            new GameObject("MainMenu").AddComponent<MainMenuController>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 빌드 첫 씬 = 메인 메뉴 (나머지 순서는 그대로)
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[MainMenuSceneSetup] {ScenePath} 생성 · 빌드 씬 순서: {string.Join(", ", scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)))}");
        }
    }
}
