using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 발판 ①안 바닥 질감 (FreeStylized Cobblestone 01, 무료·상업 사용 가능, 4K 원본을 2K로 줄임)
    // 이어 붙이기(Repeat), 요철 그림은 노멀맵으로. 실행: -executeMethod Fantasia.Editor.GroundSetup.Apply
    public static class GroundSetup
    {
        private const string Dir = "Assets/_Project/Resources/BoardArt/";

        [MenuItem("Fantasia/Board/Ground Texture Import")]
        public static void Apply()
        {
            Set(Dir + "Ground_Albedo.png", TextureImporterType.Default, true);
            Set(Dir + "Ground_Normal.png", TextureImporterType.NormalMap, false);
            Set(Dir + "Ground_AO.png", TextureImporterType.Default, false);
            Debug.Log("[GroundSetup] 바닥 질감 가져오기 설정 완료");
        }

        private static void Set(string path, TextureImporterType type, bool srgb)
        {
            var im = AssetImporter.GetAtPath(path) as TextureImporter;
            if (im == null) { Debug.LogWarning("[GroundSetup] 없음: " + path); return; }
            im.textureType = type;
            im.sRGBTexture = srgb;
            im.wrapMode = TextureWrapMode.Repeat;
            im.mipmapEnabled = true;
            im.maxTextureSize = 2048;
            im.anisoLevel = 4; // 비스듬히 보는 보드에서 흐려지지 않게
            im.textureCompression = TextureImporterCompression.Compressed;
            im.SaveAndReimport();
        }
    }
}
