using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Items
{
    [System.Serializable]
    public struct LootEntry
    {
        public ItemDefinition Item;
        public float Weight;

        public LootEntry(ItemDefinition item, float weight)
        {
            Item = item;
            Weight = weight;
        }
    }

    // Weighted drop table for a single pick (e.g. CombatTestController.PossibleLoot).
    // Not per-slot rolling like Battle Brothers (separate roll per equipment
    // slot) — one weighted pick across the whole table is enough until real
    // drop design (GDD 6.7) is settled. See Docs/References/GameDesignInspiration_2026-09-10.md.
    public static class LootTable
    {
        // Placeholder default so PlaceholderDataSetup has something reasonable
        // to seed with — equipment rarer than consumables/materials. Real
        // per-item weights are a design decision, not this table's job.
        public static float DefaultWeight(ItemCategory category) => category switch
        {
            ItemCategory.Equipment => 1f,
            ItemCategory.Material => 2f,
            _ => 3f, // Consumable
        };

        public static ItemDefinition PickWeighted(IReadOnlyList<LootEntry> entries)
        {
            float total = 0f;
            for (int i = 0; i < entries.Count; i++) total += Mathf.Max(0f, entries[i].Weight);
            if (total <= 0f) return entries.Count > 0 ? entries[0].Item : null;

            float roll = Random.Range(0f, total);
            for (int i = 0; i < entries.Count; i++)
            {
                float weight = Mathf.Max(0f, entries[i].Weight);
                if (roll < weight) return entries[i].Item;
                roll -= weight;
            }
            return entries[entries.Count - 1].Item;
        }
    }
}
