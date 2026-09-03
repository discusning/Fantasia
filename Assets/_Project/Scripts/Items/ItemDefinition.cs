using UnityEngine;

namespace Fantasia.Items
{
    // No icon art yet — IconTint is the placeholder swatch shown in inventory
    // slots until a Sprite is assigned.
    [CreateAssetMenu(fileName = "Item", menuName = "Fantasia/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        public string ItemName;
        public Sprite Icon;
        public Color IconTint = Color.white;
        [TextArea] public string Description;
    }
}
