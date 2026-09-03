using Fantasia.Board;
using Fantasia.Core;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Fantasia.Editor
{
    // One-click scaffold for a play-testable board scene: camera, light, and
    // a BoardManager object carrying HexBoard + BoardTestController. Rebuilding
    // via the menu is cheaper than hand-maintaining a .unity scene file as the
    // board scripts change.
    public static class BoardTestSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Overworld/BoardTest.unity";

        [MenuItem("Fantasia/Setup Board Test Scene")]
        public static void CreateScene()
        {
            BuildScene();
            Debug.Log("보드 테스트 씬 생성 완료: " + ScenePath +
                       " — Play 버튼을 눌러 테스트하세요 (Space=주사위 굴리기, 초록 타일 클릭=이동, I=상태창)");
        }

        // -executeMethod entry point for headless verification (no GUI interaction needed).
        // Generates tiles in memory only (Awake doesn't fire outside Play mode here) so the
        // saved scene on disk stays the lightweight, tiles-generate-on-Play version.
        public static void CreateSceneAndCapture()
        {
            var cam = BuildScene();
            var boardGO = GameObject.Find("BoardManager");
            boardGO.GetComponent<HexBoard>().Generate();
            boardGO.GetComponent<BoardTestController>().SpawnToken();
            EditorScreenshotUtility.Capture(cam, "board_test_screenshot.png");
        }

        // -executeMethod entry point for headless UI verification — force-opens
        // the status/inventory panel (normally hidden until the player presses I).
        public static void CreateSceneAndCaptureUI()
        {
            var cam = BuildScene();
            var panel = GameObject.Find("StatusInventoryUI").GetComponent<StatusInventoryPanel>();
            panel.Open();
            EditorScreenshotUtility.Capture(cam, "status_inventory_screenshot.png");
        }

        private static Camera BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cameraGO.transform.position = new Vector3(0f, 10f, -12f);
            cameraGO.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

            var boardGO = new GameObject("BoardManager");
            boardGO.AddComponent<HexBoard>();
            boardGO.AddComponent<BoardTestController>();

            new GameObject("DevSceneNav").AddComponent<DevSceneNav>();

            // Without this, uGUI Button clicks (StatusInventoryUI's tabs) are
            // never dispatched at all — no EventSystem means no UI input.
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<StandaloneInputModule>();

            var statusPanel = new GameObject("StatusInventoryUI").AddComponent<StatusInventoryPanel>();
            statusPanel.TargetCamera = cameraGO.GetComponent<Camera>();
            statusPanel.Characters = PlaceholderDataSetup.EnsureCharacters();
            statusPanel.InventoryItems = PlaceholderDataSetup.EnsureItems();

            EditorSceneManager.SaveScene(scene, ScenePath);
            BuildSettingsUtility.EnsureInBuildSettings(ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            // OpenScene reloads objects from disk, so the pre-save references above
            // are stale — look the camera up again in the now-active scene.
            return GameObject.Find("Main Camera").GetComponent<Camera>();
        }
    }
}
