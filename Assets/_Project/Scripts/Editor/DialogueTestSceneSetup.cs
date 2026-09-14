using System.IO;
using Fantasia.Core;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Fantasia.Editor
{
    // A dedicated "dialogue space" scene — per feedback, the dialogue text
    // box alone (floating over whatever scene it was triggered from) wasn't
    // enough; the player should be moved somewhere a 3D character is
    // actually standing, matching the reference screenshot's framing. No
    // character models exist yet (GDD 6.6 note), so a tinted capsule stands
    // in.
    //
    // Default is ONE on-screen speaker — most conversations only ever show
    // one NPC at a time (the player's own lines, e.g. "개발자" in
    // DialoguePanel.SampleLines, don't get a capsule at all — no one is
    // highlighted during those, matching how VN portraits dim on the
    // player's own line). DialogueStageController still supports any number
    // of named capsules, so a specific scene that genuinely needs 2+
    // characters on screen at once can add more via SpawnSpeaker() — do
    // that only when asked for, not by default.
    public static class DialogueTestSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Dialogue/DialogueTest.unity";

        [MenuItem("Fantasia/Setup Dialogue Test Scene")]
        public static void CreateScene()
        {
            BuildScene();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            BuildSettingsUtility.EnsureInBuildSettings(ScenePath);
            Debug.Log("대화 테스트 씬 생성 완료: " + ScenePath);
        }

        // -executeMethod entry point for headless verification.
        public static void CreateSceneAndCapture()
        {
            var cam = BuildScene();
            EditorScreenshotUtility.Capture(cam, "dialogue_scene_screenshot.png");
        }

        private static Camera BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            var groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Dialogue Ground";
            groundGO.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.45f, 0.3f) };

            // Placeholder speaker — no 3D model yet, a tinted capsule stands
            // in. Name must match a DialoguePanel.SampleLines Speaker value
            // ("팀장 요정") so DialogueStageController can find and
            // highlight it.
            var speakersGO = new GameObject("Speakers");
            speakersGO.AddComponent<DialogueStageController>();
            SpawnSpeaker(speakersGO.transform, "팀장 요정", new Vector3(0f, 1f, 0f), new Color(0.5f, 0.7f, 0.9f));

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cam.fieldOfView = 45f;
            cameraGO.transform.position = new Vector3(0f, 1.6f, -3.5f);
            cameraGO.transform.LookAt(new Vector3(0f, 1.3f, 0f));

            new GameObject("DevSceneNav").AddComponent<DevSceneNav>();

            // DialoguePanel's "다음"/"닫기" button needs this to receive
            // clicks — each scene brings its own, same as BoardTest/CombatTest,
            // since it isn't a DontDestroyOnLoad object.
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<StandaloneInputModule>();

            // SaveScene doesn't create missing folders on its own — every
            // other test scene's folder (Scenes/Overworld, Scenes/Combat)
            // already existed from earlier commits, but Scenes/Dialogue is new.
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            return GameObject.Find("Main Camera").GetComponent<Camera>();
        }

        private static void SpawnSpeaker(Transform parent, string speakerName, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = speakerName;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
        }
    }
}
