using UnityEditor;
using UnityEditor.SceneManagement;

namespace Fantasia.Editor
{
    // 에디터에서 ▶ Play를 누르면 지금 열린 씬과 상관없이 보드(BoardTest)부터 시작한다.
    // 빈 씬(Untitled)에서 Play해서 아무것도 안 뜨는 일을 막는다. 끄려면 메뉴 Fantasia/Play Starts From Board.
    [InitializeOnLoad]
    public static class PlayModeStartScene
    {
        private const string ScenePath = "Assets/_Project/Scenes/Overworld/BoardTest.unity";
        private const string MenuPath = "Fantasia/Play Starts From Board";
        private const string PrefKey = "Fantasia.PlayStartsFromBoard";

        static PlayModeStartScene() => EditorApplication.delayCall += Apply;

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        private static void Apply()
        {
            EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) : null;
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
