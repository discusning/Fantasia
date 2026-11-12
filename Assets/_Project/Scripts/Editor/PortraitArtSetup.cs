using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/12 디자인 D9: 아군 3명 그린 초상(3:4) + 얼굴만 자른 정사각 그림 연결.
    //  Portrait = 3:4 전체 (대화 창), Face = 얼굴 (카드·파티장 칸·순서 띠·가방·파티 구성).
    //  아벨 모로: 대화는 그대로 상처완화 대체본, 적 칸·순서 띠는 그 얼굴을 자른 Face (팀장 결정).
    // 실행: 메뉴 Fantasia/Art/초상화 연결 · 배치 -executeMethod Fantasia.Editor.PortraitArtSetup.Run
    public static class PortraitArtSetup
    {
        private const string Dir = "Assets/_Project/UI/Sprites/Portraits";

        [MenuItem("Fantasia/Art/초상화 연결")]
        public static void Run()
        {
            Job("명탐정", "N23_초상_명탐정_3x4", "Face_명탐정");
            Job("역병의사", "N23_초상_역병의사_3x4", "Face_역병의사");
            Job("저격수", "N23_초상_저격수_3x4", "Face_저격수");

            var abel = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_검시관아벨모로.asset");
            var abelFace = Sprite($"{Dir}/Face_아벨모로.png", 512);
            if (abel != null && abelFace != null) { abel.Face = abelFace; EditorUtility.SetDirty(abel); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PortraitArtSetup] 끝 · 아벨 얼굴 {(abel != null && abel.Face != null)}");
        }

        private static void Job(string id, string portrait, string face)
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/Job_{id}.asset");
            if (job == null) { Debug.LogWarning($"[PortraitArtSetup] 직업 없음 {id}"); return; }
            job.Portrait = Sprite($"{Dir}/{portrait}.png", 1024);
            job.Face = Sprite($"{Dir}/{face}.png", 512);
            EditorUtility.SetDirty(job);
            Debug.Log($"[PortraitArtSetup] {id}: 초상 {job.Portrait != null} · 얼굴 {job.Face != null}");
        }

        private static Sprite Sprite(string path, int max)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = max;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
