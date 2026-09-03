using System.IO;
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
            var specs = new (string name, Color tint, string desc)[]
            {
                ("치유 물약", new Color(0.9f, 0.3f, 0.3f), "HP를 회복한다."),
                ("빵", new Color(0.8f, 0.65f, 0.3f), "캠프에서 소비하는 식량."),
                ("녹슨 검", new Color(0.6f, 0.6f, 0.6f), "기본 무기."),
            };

            Directory.CreateDirectory(ItemDir);
            var result = new ItemDefinition[specs.Length];

            for (int i = 0; i < specs.Length; i++)
            {
                var (name, tint, desc) = specs[i];
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
                EditorUtility.SetDirty(asset);

                result[i] = asset;
            }

            AssetDatabase.SaveAssets();
            return result;
        }
    }
}
