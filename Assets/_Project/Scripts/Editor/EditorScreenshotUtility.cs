using System.IO;
using UnityEngine;

namespace Fantasia.Editor
{
    // Shared headless-verification helper: render a camera to a fixed-size
    // texture and save it, so scene-setup scripts don't each reimplement this.
    public static class EditorScreenshotUtility
    {
        public static void Capture(Camera cam, string fileName, int width = 1280, int height = 720)
        {
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;

            var path = Path.Combine(Application.dataPath, "../Logs/" + fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("스크린샷 저장: " + path);
        }
    }
}
