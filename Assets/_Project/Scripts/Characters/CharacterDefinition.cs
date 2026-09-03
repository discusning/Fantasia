using UnityEngine;

namespace Fantasia.Characters
{
    // Base character data for the status UI / eventual combat roster (GDD 6.1
    // stat table). Portrait art doesn't exist yet — PortraitTint is the
    // placeholder swatch shown until a Sprite is assigned.
    [CreateAssetMenu(fileName = "Character", menuName = "Fantasia/Character Definition")]
    public class CharacterDefinition : ScriptableObject
    {
        public string CharacterName;
        public Sprite Portrait;
        public Color PortraitTint = Color.gray;

        public int MaxHP = 30;
        public int PhysicalAttack;
        public int MagicAttack;
        public int PhysicalDefense;
        public int MagicDefense;
        public int Speed;
    }
}
