using UnityEditor;

namespace Fantasia.Editor
{
    // UI 그림 폴더(Assets/_Project/UI/Sprites)의 PNG는 UI 스프라이트로 가져온다 —
    // 아이콘 사용 설명서(Docs/Art/Fantasia_Icon_Usage_Guide.pdf) "Sprite (2D and UI), Single, Alpha Is Transparency".
    // 9-slice 경계는 그림마다 실제 장식 위치를 보고 따로 정한다 (여기서 일괄 지정하지 않음).
    public class UISpriteImport : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/UI/Sprites/";
        private const string NotebookFolder = "Assets/_Project/Resources/UI/"; // 11/14 탐정 수첩·U13 행동 아이콘 (Resources.Load)

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) && !assetPath.StartsWith(NotebookFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048; // 배너·버튼 원본 폭 2048 — 줄이면 9-slice 경계가 어긋남
        }
    }
}
