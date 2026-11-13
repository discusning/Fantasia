using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 이펙트 고르기용 캡처: 프리팹마다 한 줄, 시간마다 한 칸 (사람 키 1.8m 기둥을 옆에 두고 크기 비교).
    // -executeMethod Fantasia.Editor.VfxPreviewShot.Capture -list "경로|경로" -times "0.1,0.3,0.6" -out x.png [-cell 320] [-scale 1]
    public static class VfxPreviewShot
    {
        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            var list = Arg("-list", "").Split('|').Where(s => s.Length > 0).ToArray();
            var times = Arg("-times", "0.1,0.3,0.6").Split(',').Select(float.Parse).ToArray();
            int cell = int.Parse(Arg("-cell", "320"));
            float scale = float.Parse(Arg("-scale", "1"));
            string outPath = Arg("-out", "vfx.png");

            var camGo = new GameObject("VfxCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.08f, 0.1f);
            cam.fieldOfView = 40f;
            cam.transform.position = new Vector3(0f, 1.3f, -5.2f);
            cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            var light = new GameObject("L").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.35f, 0.33f, 0.35f);

            // 키 비교용 기둥 (사람 1.8m)
            var post = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            post.transform.position = new Vector3(-1.1f, 0.9f, 0f);
            post.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.24f, 0.27f) };
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 2f;
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.16f, 0.15f, 0.17f) };

            var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var sheet = new Texture2D(cell * times.Length, cell * list.Length, TextureFormat.RGB24, false);
            var read = new Texture2D(cell, cell, TextureFormat.RGB24, false);

            for (int r = 0; r < list.Length; r++)
            {
                var parts = list[r].Split('@'); // 경로@높이
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(parts[0]);
                float height = parts.Length > 1 ? float.Parse(parts[1]) : 1.0f;
                if (prefab == null) { Debug.LogWarning($"[VfxShot] 없음 {parts[0]}"); continue; }
                for (int c = 0; c < times.Length; c++)
                {
                    var go = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, height, 0f), prefab.transform.rotation);
                    go.transform.localScale = prefab.transform.localScale * scale;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                        if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                            ps.Simulate(times[c], true, true, true);
                    cam.Render();
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
                    read.Apply();
                    sheet.SetPixels(c * cell, (list.Length - 1 - r) * cell, cell, cell, read.GetPixels());
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(go);
                }
                Debug.Log($"[VfxShot] {r + 1}. {Path.GetFileNameWithoutExtension(parts[0])}");
            }
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Debug.Log($"[VfxShot] 저장 {outPath}");
        }
    }
}
