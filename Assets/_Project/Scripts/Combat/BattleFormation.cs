using UnityEngine;

namespace Fantasia.Combat
{
    // Left-to-right placement order doubles as turn order (GDD 6.3 — party
    // formation determines the turn queue), so combatant layout lives here
    // rather than being scattered wherever a line of characters gets placed.
    public static class BattleFormation
    {
        public static Vector3[] Line(int count, float spacing)
        {
            var positions = new Vector3[count];
            float start = -(count - 1) * spacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(0f, 0f, start + i * spacing);
            }
            return positions;
        }
    }
}
