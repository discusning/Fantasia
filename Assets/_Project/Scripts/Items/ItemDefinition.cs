using UnityEngine;

namespace Fantasia.Items
{
    // Right now this only decides whether right-click-to-equip applies
    // (StatusInventoryPanel.TryEquipSlot) — actual equip slots/stat effects
    // depend on the character/equipment system, which GDD 6.3 hasn't settled.
    public enum ItemCategory
    {
        Consumable,
        Equipment,
        Material,
    }

    // No icon art yet — IconTint is the placeholder swatch shown in inventory
    // slots until a Sprite is assigned.
    [CreateAssetMenu(fileName = "Item", menuName = "Fantasia/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        public string ItemName;
        public Sprite Icon;
        public Color IconTint = Color.white;
        [TextArea] public string Description;
        public ItemCategory Category = ItemCategory.Consumable;
    }
}
