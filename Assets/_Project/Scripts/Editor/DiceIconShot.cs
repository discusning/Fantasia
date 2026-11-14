using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 팀장 B안: 보드 주사위 버튼 그림 — 게임 주사위 모델(Art/Models/Dice)을 비스듬히 찍어 투명 배경 PNG로.
    // 결과: Assets/_Project/Resources/UI/Board/DiceButton.png (UISpriteImport가 스프라이트로 가져옴).
    // 실행: -executeMethod Fantasia.Editor.DiceIconShot.Capture
    public static class DiceIconShot
    {
        private const string Output = "Assets/_Project/Resources/UI/Board/DiceButton.png";

        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Dice/Dice_01.prefab");
            var die = (GameObject)Object.Instantiate(src);
            die.transform.position = Vector3.zero;
            die.transform.rotation = Quaternion.Euler(-18f, 32f, 12f); // 세 면이 보이게
            var rends = die.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.93f, 0.82f); // 따뜻한 촛불빛 (보드 톤)
            key.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.5f, 0.45f);

            var cam = new GameObject("C").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = b.extents.magnitude * 0.82f;
            cam.transform.position = b.center + new Vector3(0f, 0.35f, -1f).normalized * 10f;
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 50f;

            const int S = 256;
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;

            int opaque = 0;
            foreach (var c in tex.GetPixels32()) if (c.a > 8) opaque++;
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllBytes(Output, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(Output);
            Debug.Log($"[DiceIconShot] {Output} 저장 · 보이는 픽셀 {opaque}/{S * S} (0이나 전부면 투명 배경 실패)");
        }
    }
}
