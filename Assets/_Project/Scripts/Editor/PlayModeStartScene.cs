using UnityEditor;
using UnityEditor.SceneManagement;

namespace Fantasia.Editor
{
    // 에디터에서 ▶ Play를 누르면 지금 열린 씬과 상관없이 메인 메뉴(없으면 보드 BoardTest)부터 시작한다.
    // 빈 씬(Untitled)에서 Play해서 아무것도 안 뜨는 일을 막는다. 끄면 지금 열린 씬에서 시작 — 메뉴 Fantasia/Play Starts From Main Menu.
    // 또 Play 중 코드 변경은 플레이가 끝난 뒤 컴파일하도록 맞춘다.
    [InitializeOnLoad]
    public static class PlayModeStartScene
    {
        private const string ScenePath = MainMenuSceneSetup.ScenePath;
        private const string FallbackPath = "Assets/_Project/Scenes/Overworld/BoardTest.unity";
        private const string MenuPath = "Fantasia/Play Starts From Main Menu";
        private const string PrefKey = "Fantasia.PlayStartsFromBoard";

        static PlayModeStartScene() => EditorApplication.delayCall += Apply;

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        private static void Apply()
        {
            // Play 중에 코드가 바뀌면 플레이를 끝낸 뒤 컴파일 (Preferences > General > Script Changes While Playing).
            // 플레이 도중 다시 불러오면 실행 중 상태가 비어 NullReference가 줄줄이 난다.
            EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1);
            EditorSceneManager.playModeStartScene = Enabled
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) ?? AssetDatabase.LoadAssetAtPath<SceneAsset>(FallbackPath)
                : null;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
