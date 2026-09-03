using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Core
{
    // Temporary cross-scene navigation for testing — jump between playable
    // scenes without leaving Play mode. Persists across scene loads via a
    // singleton + DontDestroyOnLoad. Remove once a real scene-flow/menu
    // system (title -> prologue -> overworld -> combat) exists.
    public class DevSceneNav : MonoBehaviour
    {
        private static DevSceneNav _instance;
        private static readonly string[] Scenes = { "BoardTest", "CombatTest" };

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 160, 10, 150, 90), GUI.skin.box);
            GUILayout.Label($"씬: {SceneManager.GetActiveScene().name}");

            foreach (var scene in Scenes)
            {
                if (scene == SceneManager.GetActiveScene().name) continue;
                if (GUILayout.Button(scene)) SceneManager.LoadScene(scene);
            }

            GUILayout.EndArea();
        }
    }
}
