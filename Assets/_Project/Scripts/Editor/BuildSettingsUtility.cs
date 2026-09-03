using System.Linq;
using UnityEditor;

namespace Fantasia.Editor
{
    // Runtime SceneManager.LoadScene requires a scene to be registered in
    // Build Settings even in-Editor, so scene-setup scripts call this to keep
    // themselves loadable without anyone touching Build Settings by hand.
    public static class BuildSettingsUtility
    {
        public static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath)) return;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
