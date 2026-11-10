using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 보드 배치물 방향 확인용 사진 (2026-11-11, 나무 90도 엎어짐 점검).
    // 위 줄: 게임용 프리팹(Resources/BoardArt, ArtSetup이 돌린 것) · 아래 줄: 받은 원본 FBX 그대로 — 둘 다 옆(정면 −Z)에서 15도 내려다봄.
    // 실행: -executeMethod Fantasia.Editor.BoardPropShot.Capture -out <png>
    public static class BoardPropShot
    {
        private const int Cell = 320;
        private static readonly string[] Roles = { "Block_Trees", "Block_LowTrees", "Block_LowStone", "Block_A", "Block_B", "Treasure", "Recovery", "Event_FortuneTeller", "Event_Fountain", "Exit" };
        private static readonly string[] Raw =
        {
            "Props/Prop_Trees_A/Prop_Trees_A.fbx", "Props/Prop_Trees_A/Prop_Trees_A.fbx", "Props/Prop_StoneWall/Prop_StoneWall.fbx", "Buildings/FBX/Prop_House_A.fbx", "Buildings/FBX/Prop_House_B.fbx",
            "Props/Prop_Treasure/Prop_Treasure.fbx", "Props/Prop_Well/Prop_Well.fbx", "Props/Prop_FortuneTent/Prop_FortuneTent.fbx", "Props/Prop_Fountain/Prop_Fountain.fbx", "Props/Prop_ExitGate/Prop_ExitGate.fbx",
        };

        public static void Capture()
        {
            string outPath = Arg("-out") ?? "Logs/board_props.png";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGO = new GameObject("Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGO.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var camGO = new GameObject("Cam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.15f, 0.2f);
            cam.fieldOfView = 30f;
            var rt = new RenderTexture(Cell, Cell, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var sheet = new Texture2D(Cell * Roles.Length, Cell * 2, TextureFormat.RGB24, false);

            for (int i = 0; i < Roles.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Resources/BoardArt/{Roles[i]}.prefab");
                var raw = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Models/{Raw[i]}");
                Shoot(prefab, cam, rt, sheet, i, 1, Roles[i]);
                Shoot(raw, cam, rt, sheet, i, 0, Raw[i]);
            }
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            RenderTexture.active = null;
            Debug.Log($"[BoardPropShot] 저장 {outPath}");
        }

        private static void Shoot(GameObject src, Camera cam, RenderTexture rt, Texture2D sheet, int col, int row, string label)
        {
            if (src == null) { Debug.Log($"[BoardPropShot] 없음 {label}"); return; }
            var go = Object.Instantiate(src);
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Object.DestroyImmediate(go); return; }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float radius = b.extents.magnitude;
            cam.transform.position = b.center + (Quaternion.Euler(15f, 0f, 0f) * Vector3.back) * radius * 4f;
            cam.transform.LookAt(b.center);
            cam.Render();
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, Cell, Cell), Cell * col, Cell * row);
            Debug.Log($"[BoardPropShot] {label}: 크기 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2} (가로×높이×깊이)");
            Object.DestroyImmediate(go);
        }

        private static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
