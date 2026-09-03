using UnityEngine;

namespace Fantasia.Dice
{
    // Minimal reusable dice primitive. Combat's slot-roll checks and the
    // overworld's movement roll both build on this rather than duplicating
    // Random.Range calls.
    public static class DiceRoller
    {
        public static int Roll(int sides)
        {
            return Random.Range(1, sides + 1);
        }

        public static bool RollChance(float chance01)
        {
            return Random.value < chance01;
        }
    }
}
