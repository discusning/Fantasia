using System.IO;
using Fantasia.Board;
using Fantasia.Characters;
using Fantasia.Items;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Creates/updates a handful of placeholder Character/Item data assets so
    // UI work (StatusInventoryPanel) has something real to display before
    // actual game content is designed. Idempotent — reruns just refresh values.
    public static class PlaceholderDataSetup
    {
        private const string CharacterDir = "Assets/_Project/Data/Characters";
        private const string ItemDir = "Assets/_Project/Data/Items";
        private const string LandmarkDir = "Assets/_Project/Data/Landmarks";

        public static CharacterDefinition[] EnsureCharacters()
        {
            var specs = new (string name, Color tint, int hp, int patk, int matk, int pdef, int mdef, int speed)[]
            {
                ("캐릭1", new Color(0.3f, 0.5f, 0.9f), 32, 9, 2, 6, 3, 8),
                ("캐릭2", new Color(0.9f, 0.4f, 0.3f), 28, 4, 10, 3, 6, 6),
                ("캐릭3", new Color(0.4f, 0.8f, 0.4f), 36, 6, 4, 9, 5, 10),
            };

            Directory.CreateDirectory(CharacterDir);
            var result = new CharacterDefinition[specs.Length];

            for (int i = 0; i < specs.Length; i++)
            {
                var (name, tint, hp, patk, matk, pdef, mdef, speed) = specs[i];
                string path = $"{CharacterDir}/{name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<CharacterDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.CharacterName = name;
                asset.PortraitTint = tint;
                asset.MaxHP = hp;
                asset.PhysicalAttack = patk;
                asset.MagicAttack = matk;
                asset.PhysicalDefense = pdef;
                asset.MagicDefense = mdef;
                asset.Speed = speed;
                EditorUtility.SetDirty(asset);

                result[i] = asset;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        public static ItemDefinition[] EnsureItems()
        {
            var specs = new (string name, Color tint, string desc, ItemCategory category)[]
            {
                ("치유 물약", new Color(0.9f, 0.3f, 0.3f), "HP를 회복한다.", ItemCategory.Consumable),
                ("빵", new Color(0.8f, 0.65f, 0.3f), "캠프에서 소비하는 식량.", ItemCategory.Consumable),
                ("녹슨 검", new Color(0.6f, 0.6f, 0.6f), "기본 무기.", ItemCategory.Equipment),
            };

            Directory.CreateDirectory(ItemDir);
            var result = new ItemDefinition[specs.Length];

            for (int i = 0; i < specs.Length; i++)
            {
                var (name, tint, desc, category) = specs[i];
                string path = $"{ItemDir}/{name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.ItemName = name;
                asset.IconTint = tint;
                asset.Description = desc;
                asset.Category = category;
                EditorUtility.SetDirty(asset);

                result[i] = asset;
            }

            AssetDatabase.SaveAssets();
            return result;
        }

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
