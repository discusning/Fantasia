using Fantasia.Board;
using Fantasia.Core;
using Fantasia.Data;
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

            // D08 조우 판정 테스트에 쓸 명탐정 + 샘플 적을 보장해둔다 (idempotent, GameDataSeed 참고).
            GameDataSeed.SeedAll();
            var detectiveJob = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            var shadowHound = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_그림자사냥개.asset");

            var boardGO = new GameObject("BoardManager");
            var hexBoard = boardGO.AddComponent<HexBoard>();
            // 확정 월드맵 (Docs/Planning/WorldMap.md)
            hexBoard.MapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(WorldMapSelfTest.MapPath);
            hexBoard.RegionBosses = new[] { AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_검시관아벨모로.asset") };
            var reaper = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_인조그림자사신.asset");
            // 1막 소형 적 4종 (System.md 소형 적) — 인카운터 칸마다 이 중 하나.
            hexBoard.EncounterEnemyPool = System.Array.FindAll(new[]
            {
                shadowHound,
                AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_연성독거미유체.asset"),
                AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_역병쥐.asset"),
                AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_가고일유체.asset"),
            }, e => e != null);
            var boardTestController = boardGO.AddComponent<BoardTestController>();
            boardTestController.PlaceholderLandmarks = PlaceholderDataSetup.EnsureLandmarks();
            boardTestController.TestHeroJob = detectiveJob;
            boardTestController.ReaperEnemy = reaper;
            boardTestController.TestPartyJobs = new[]
            {
                detectiveJob,
                AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset"),
                AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_저격수.asset"),
            };

            new GameObject("DevSceneNav").AddComponent<DevSceneNav>();

            // Without this, uGUI Button clicks (인벤토리·조우 창 버튼) are
            // never dispatched at all — no EventSystem means no UI input.
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<StandaloneInputModule>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            BuildSettingsUtility.EnsureInBuildSettings(ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            // OpenScene reloads objects from disk, so the pre-save references above
            // are stale — look the camera up again in the now-active scene.
            return GameObject.Find("Main Camera").GetComponent<Camera>();
        }
    }
}
