using System.IO;
using Fantasia.Board;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Creates/updates placeholder landmark assets until real landmark content
    // is designed. Idempotent — reruns just refresh values.
    public static class PlaceholderDataSetup
    {
        private const string LandmarkDir = "Assets/_Project/Data/Landmarks";

        // Three example landmark types (see LandmarkInfoPanel + Ctrl+Right-click
        // on a board tile) — category is free text, not an enum, since the
        // real landmark list is a design decision (GDD TBD).
        public static LandmarkDefinition[] EnsureLandmarks()
        {
            var specs = new (string name, string category, string badge, Color accent, string desc)[]
            {
                ("고요의 성소", "성소", "성", new Color(0.55f, 0.75f, 0.95f), "이곳에서 기도하면 마음이 편안해진다."),
                ("버려진 감옥", "감옥", "감", new Color(0.55f, 0.15f, 0.15f), "쇠사슬 소리가 아직도 들리는 것 같다."),
                ("잊혀진 유적", "유적", "유", new Color(0.45f, 0.6f, 0.35f), "오래된 문양이 벽에 새겨져 있다."),
            };

            Directory.CreateDirectory(LandmarkDir);
            var result = new LandmarkDefinition[specs.Length];

            for (int i = 0; i < specs.Length; i++)
            {
                var (name, category, badge, accent, desc) = specs[i];
                string path = $"{LandmarkDir}/{name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<LandmarkDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<LandmarkDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.LandmarkName = name;
                asset.Category = category;
                asset.Badge = badge;
                asset.AccentColor = accent;
                asset.Description = desc;
                EditorUtility.SetDirty(asset);

                result[i] = asset;
            }

            AssetDatabase.SaveAssets();
            return result;
        }
    }
}
