using Fantasia.Combat;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Statistical sanity check for WeaponAttackResolver — runs many trials and
    // logs measured vs. expected rates instead of eyeballing it in a scene.
    public static class CombatSlotRollSelfTest
    {
        private const int Trials = 20000;

        [MenuItem("Fantasia/Run Combat Slot-Roll Self-Test")]
        public static void Run()
        {
            RunFor(new WeaponDefinition { SlotCount = 2, BaseDamagePerSlot = 10f, Durability = DurabilityTier.Strong }, focusSpent: 0);
            RunFor(new WeaponDefinition { SlotCount = 3, BaseDamagePerSlot = 7f, Durability = DurabilityTier.Strong }, focusSpent: 0);
            RunFor(new WeaponDefinition { SlotCount = 4, BaseDamagePerSlot = 5f, Durability = DurabilityTier.Strong }, focusSpent: 0);
            RunFor(new WeaponDefinition { SlotCount = 4, BaseDamagePerSlot = 5f, Durability = DurabilityTier.Weak }, focusSpent: 2);
        }

        private static void RunFor(WeaponDefinition weapon, int focusSpent)
        {
            long successSum = 0;
            double damageSum = 0;
            int perfectCount = 0;

            for (int i = 0; i < Trials; i++)
            {
                var result = WeaponAttackResolver.Resolve(weapon, focusSpent);
                successSum += result.SuccessCount;
                damageSum += result.Damage;
                if (result.IsPerfect) perfectCount++;
            }

            float measuredSuccessPerSlot = (float)successSum / (Trials * weapon.SlotCount);

            Debug.Log($"[SlotRoll] {weapon.SlotCount}슬롯 {weapon.Durability} focus={focusSpent} — " +
                      $"기본 슬롯 확률 {weapon.SlotSuccessChance:P1} / 측정 평균 성공률(포커스 포함) {measuredSuccessPerSlot:P1}, " +
                      $"퍼펙트 비율 {(float)perfectCount / Trials:P2}, 평균 데미지 {damageSum / Trials:F2}");
        }
    }
}
