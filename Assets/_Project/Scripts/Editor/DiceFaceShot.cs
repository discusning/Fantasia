using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 받은 주사위 모델(Art/Models/Dice)의 6면을 각 방향에서 찍어 어느 면이 몇인지 확인하는 도구.
    // 결과: Logs/dice_faces.png (+X −X +Y −Y +Z −Z 순서, 모델 기준 방향) · 크기는 콘솔에.
    public static class DiceFaceShot
    {
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Dice/Dice_01.prefab");
            var die = (GameObject)Object.Instantiate(src);
            die.transform.position = Vector3.zero;
            die.transform.rotation = Quaternion.identity;
            var rends = die.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            int tris = 0;
            foreach (var mf in die.GetComponentsInChildren<MeshFilter>()) tris += mf.sharedMesh.triangles.Length / 3;
            Debug.Log($"[DiceFaceShot] 크기 {b.size} 중심 {b.center} 면 {tris} 회전 {die.transform.localEulerAngles} 배율 {die.transform.localScale} 자식 {die.transform.childCount}");

            var light = new GameObject("L").AddComponent<Light>();
            light.type = LightType.Directional;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var cam = new GameObject("C").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            cam.orthographic = true;
            cam.orthographicSize = b.extents.magnitude * 0.9f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            const int S = 256;
            var sheet = new Texture2D(S * 6, S, TextureFormat.RGB24, false);
            var rt = new RenderTexture(S, S, 24) { antiAliasing = 4 };
            for (int i = 0; i < 6; i++)
            {
                cam.transform.position = b.center + dirs[i] * 10f;
                cam.transform.LookAt(b.center, Mathf.Abs(dirs[i].y) > 0.9f ? Vector3.forward : Vector3.up);
                light.transform.rotation = Quaternion.LookRotation(-dirs[i]);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, S, S), S * i, 0);
            }
            sheet.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../Logs/dice_faces.png"), sheet.EncodeToPNG());
        }
    }
}
