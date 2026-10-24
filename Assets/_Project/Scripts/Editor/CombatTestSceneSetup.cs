using Fantasia.Combat;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.UI;
using Fantasia.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Fantasia.Editor
{
    // 전투 씬 — 보드의 3/4 탑다운과 다른 낮은 대치 구도 (참고: Fantasia_인터페이스_사진원본/
    // 01_포더킹1_실제_스크린샷/05_턴제_전투와_공격대상_선택_한국어.jpg). 참가자·UI는 BattleController가
    // Play 때 BoardSession.PendingBattle로 직접 만들어서, 씬에는 바닥·조명·카메라·관리자만 저장한다.
    public static class CombatTestSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Combat/CombatTest.unity";

        [MenuItem("Fantasia/Setup Combat Test Scene")]
        public static void CreateScene()
        {
            BuildScene();
            SpawnManager();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            BuildSettingsUtility.EnsureInBuildSettings(ScenePath);
            Debug.Log("전투 테스트 씬 생성 완료: " + ScenePath + " — 보드에서 조우하거나 이 씬을 바로 Play.");
        }

        // -executeMethod: 씬을 만들어 저장한 뒤, Play 첫 화면(기습 성공 → 아군 차례, 첫 적 조준 중)을 재현해 찍는다.
        public static void CreateSceneAndCapture()
        {
            GameDataSeed.SeedAll();
            CreateScene();

            BoardSession.EnsureExists();
            var session = BoardSession.Instance;
            session.Party = null; // BattleController 기본 3인
            session.PendingBattle = new BattleSetup { Opening = EncounterOutcome.AmbushSuccess };
            foreach (var name in new[] { "Enemy_그림자사냥개", "Enemy_역병쥐", "Enemy_가고일유체" })
                session.PendingBattle.Enemies.Add(AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset"));

            var controller = Object.FindFirstObjectByType<BattleController>();
            controller.Begin();
            controller.PreviewTargeting();

            var cam = Camera.main;
            PortraitStudio.RenderAllNow();
            ToCamera(Object.FindFirstObjectByType<PartyHudPanel>(), cam, 2f);
            ToCamera(Object.FindFirstObjectByType<BattleHud>(), cam, 1f);
            Canvas.ForceUpdateCanvases();
            EditorScreenshotUtility.Capture(cam, "combat_test_screenshot.png");
        }

        private static void ToCamera(Component owner, Camera cam, float distance)
        {
            if (owner == null) return;
            var canvas = owner.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = distance;
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.85f, 0.7f);
            lightGO.transform.rotation = Quaternion.Euler(40f, -40f, 0f);

            var groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Arena Ground";
            groundGO.transform.localScale = new Vector3(3f, 1f, 3f);
            groundGO.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.35f, 0.3f) };

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cam.fieldOfView = 36f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.16f, 0.2f);

            // 아군 줄 바로 뒤·위에서 적 줄을 비스듬히 내려다보는 구도. 아래 1/4은 파티 카드, 위는 타임라인·적 HP 바.
            cameraGO.transform.position = new Vector3(-9f, 7.5f, -12.5f);
            cameraGO.transform.LookAt(new Vector3(1.6f, -0.4f, 0.6f));

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void SpawnManager()
        {
            var controller = new GameObject("BattleManager").AddComponent<BattleController>();

            new GameObject("DevSceneNav").AddComponent<DevSceneNav>();
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}
