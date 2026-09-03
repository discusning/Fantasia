using Fantasia.Dice;

namespace Fantasia.Combat
{
    public readonly struct SlotResult
    {
        public readonly bool Success;
        public readonly bool ForcedByFocus;

        public SlotResult(bool success, bool forcedByFocus)
        {
            Success = success;
            ForcedByFocus = forcedByFocus;
        }
    }

    public readonly struct AttackResult
    {
        public readonly SlotResult[] Slots;
        public readonly int SuccessCount;
        public readonly bool IsPerfect;
        public readonly float Damage;

        public AttackResult(SlotResult[] slots, int successCount, bool isPerfect, float damage)
        {
            Slots = slots;
            SuccessCount = successCount;
            IsPerfect = isPerfect;
            Damage = damage;
        }
    }

    // Resolves one weapon attack per GDD 6.2: each slot is an independent
    // success check, Focus can force a slot to succeed, and an all-success
    // hit gets a bonus. Pure logic, deliberately independent of the combat
    // scene/camera (see CombatTestSceneSetup) so it can be wired into
    // whatever UI/animation ends up presenting it.
    public static class WeaponAttackResolver
    {
        private const float PerfectHitBonus = 0.25f; // placeholder, tune with design

        public static AttackResult Resolve(WeaponDefinition weapon, int focusSpent)
        {
            int slotCount = weapon.SlotCount;
            focusSpent = System.Math.Clamp(focusSpent, 0, slotCount);

            var slots = new SlotResult[slotCount];
            int successCount = 0;

            for (int i = 0; i < slotCount; i++)
            {
                bool forced = i < focusSpent;
                bool success = forced || DiceRoller.RollChance(weapon.SlotSuccessChance);
                slots[i] = new SlotResult(success, forced);
                if (success) successCount++;
            }

            bool isPerfect = successCount == slotCount;
            float damage = successCount * weapon.BaseDamagePerSlot * weapon.DurabilityMultiplier;
            if (isPerfect) damage *= 1f + PerfectHitBonus;

            return new AttackResult(slots, successCount, isPerfect, damage);
        }
    }
}
