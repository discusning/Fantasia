using System.Linq;
using Fantasia.Data;
using Fantasia.UI;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 받은 아이콘 Z01~Z44(Assets/_Project/UI/Sprites/Icons)를 UI 테마·직업·소모품 칸에 연결한다.
    // 그림을 같은 파일 이름으로 바꿔 넣으면 다시 실행할 필요 없이 그대로 바뀐다 (연결은 파일 기준).
    public static class UIIconSetup
    {
        private const string Dir = "Assets/_Project/UI/Sprites/Icons/";

        [MenuItem("Fantasia/UI/Apply Icon Set (Z01-Z44)")]
        public static void Apply()
        {
            // 게이지는 좌우 둥근 끝이 찌그러지지 않게 9-slice 경계 (1024×128 그림 기준)
            SetBorder("Z38_공통_게이지_바탕.png", new Vector4(48, 40, 48, 40));
            SetBorder("Z39_공통_게이지_채움.png", new Vector4(48, 40, 48, 40));

            var theme = AssetDatabase.LoadAssetAtPath<UITheme>("Assets/_Project/Resources/UITheme.asset");
            if (theme == null) { Debug.LogError("[UIIconSetup] UITheme.asset 없음"); return; }
            theme.BloodTrailIcon = S("Z01"); theme.SoulIcon = S("Z02"); theme.CentIcon = S("Z03"); theme.LpIcon = S("Z04");
            theme.StrengthIcon = S("Z05"); theme.SpeedIcon = S("Z06"); theme.VitalityIcon = S("Z07");
            theme.IntelligenceIcon = S("Z08"); theme.LuckIcon = S("Z09"); theme.PerceptionIcon = S("Z10");
            theme.QuestIcon = S("Z15"); theme.EvidenceIcon = S("Z16"); theme.TreasureIcon = S("Z17");
            theme.HubMarker = S("Z23"); theme.HoundMarker = S("Z24"); theme.BossMarker = S("Z25");
            theme.RecoveryMarker = S("Z26"); theme.EventMarker = S("Z27"); theme.ExitMarker = S("Z28");
            theme.FocusPip = S("Z29"); theme.FocusPipEmpty = S("Z30");
            theme.ObjectiveOpen = S("Z33"); theme.ObjectiveDone = S("Z34");
            theme.QuickSlotFrame = S("Z35"); theme.QuickSlotSelected = S("Z36");
            theme.LevelBadge = S("Z37");
            theme.BarBack = S("Z38"); theme.BarFill = S("Z39");
            EditorUtility.SetDirty(theme);

            // 직업 (Z20~Z22)
            SetJob("Job_명탐정", "Z20"); SetJob("Job_역병의사", "Z21"); SetJob("Job_저격수", "Z22");

            // 소모품 (Z11~Z14)
            var catalog = ItemCatalog.Current;
            SetConsumable(catalog, ConsumableIds.Bandage, "Z11");
            SetConsumable(catalog, ConsumableIds.LifeElixir, "Z12");
            SetConsumable(catalog, ConsumableIds.ManaElixir, "Z13");
            SetConsumable(catalog, ConsumableIds.Antidote, "Z14");

            AssetDatabase.SaveAssets();
            Debug.Log("[UIIconSetup] 아이콘 연결 완료");
        }

        private static Sprite S(string code)
        {
            var path = AssetDatabase.FindAssets("t:Sprite", new[] { Dir.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileName(p).StartsWith(code + "_"));
            var sprite = path != null ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : null;
            if (sprite == null) Debug.LogWarning($"[UIIconSetup] {code} 그림 없음 — 그 자리는 글자로 보임");
            return sprite;
        }

        private static void SetBorder(string file, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(Dir + file) as TextureImporter;
            if (importer == null || importer.spriteBorder == border) return;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static void SetJob(string asset, string code)
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{asset}.asset");
            if (job == null) return;
            job.Icon = S(code);
            EditorUtility.SetDirty(job);
        }

        private static void SetConsumable(ItemCatalog catalog, string id, string code)
        {
            var c = catalog != null ? catalog.Consumable(id) : null;
            if (c == null) return;
            c.Icon = S(code);
            EditorUtility.SetDirty(c);
        }
    }
}
