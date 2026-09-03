using Fantasia.Combat;
using Fantasia.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // Combat needs its own camera treatment — a low, close battle-line view,
    // nothing like the overworld's 3/4 top-down board (see the reference in
    // Docs/Concept/Concept). Kept as a separate scene/scaffold so the two
    // viewpoints don't get tangled together.
    public static class CombatTestSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Combat/CombatTest.unity";
        private const float LineSpacing = 2f;
        private const float SideOffset = 4f;
        private const int CombatantsPerSide = 3;

        [MenuItem("Fantasia/Setup Combat Test Scene")]
        public static void CreateScene()
        {
            BuildScene();
            // Unlike the board (which regenerates tiles procedurally on Play),
            // combatants have no spawner yet — bake them into the saved scene.
            SpawnCombatantsAndManager();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            BuildSettingsUtility.EnsureInBuildSettings(ScenePath);
            Debug.Log("전투 테스트 씬 생성 완료: " + ScenePath + " — Play 후 화면 버튼으로 테스트하세요.");
        }

        // -executeMethod entry point for headless camera-framing verification.
        public static void CreateSceneAndCapture()
        {
            var cam = BuildScene();
            SpawnCombatantsAndManager();
            EditorScreenshotUtility.Capture(cam, "combat_test_screenshot.png");
        }

        private static Camera BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.85f, 0.7f);
            lightGO.transform.rotation = Quaternion.Euler(35f, -50f, 0f);

            var groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Arena Ground";
            groundGO.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.42f, 0.35f) };

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();
            cam.fieldOfView = 55f;

            // Low, close, over-the-shoulder angle from just behind the party
            // line, looking across the battle line at the enemies.
            cameraGO.transform.position = new Vector3(-SideOffset - 1.5f, 2f, -5.5f);
            cameraGO.transform.LookAt(new Vector3(SideOffset - 1.5f, 1.1f, 0.5f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);

            return GameObject.Find("Main Camera").GetComponent<Camera>();
        }

        private static void SpawnCombatantsAndManager()
        {
            SpawnLine(-SideOffset, new Color(0.25f, 0.4f, 0.85f), "Party", facesRight: true);
            SpawnLine(SideOffset, new Color(0.8f, 0.25f, 0.25f), "Enemy", facesRight: false);
            new GameObject("CombatManager").AddComponent<CombatTestController>();
            new GameObject("DevSceneNav").AddComponent<DevSceneNav>();
        }

        private static void SpawnLine(float x, Color color, string label, bool facesRight)
        {
            var positions = BattleFormation.Line(CombatantsPerSide, LineSpacing);
            var material = new Material(Shader.Find("Standard")) { color = color };

            for (int i = 0; i < CombatantsPerSide; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"{label} {i + 1}";
                go.transform.position = new Vector3(x, 1f, positions[i].z);
                go.transform.rotation = Quaternion.LookRotation(facesRight ? Vector3.right : Vector3.left);
                go.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
    }
}
