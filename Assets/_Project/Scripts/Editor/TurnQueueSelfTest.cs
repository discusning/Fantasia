using System.Linq;
using Fantasia.Combat;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Runs full auto-battles through Combatant + TurnQueue + WeaponAttackResolver
    // (the same pieces CombatTestController drives from buttons) to catch
    // infinite loops / crashes before testing it by hand in Play mode.
    public static class TurnQueueSelfTest
    {
        private const int Battles = 200;
        private const int MaxTurns = 500;

        [MenuItem("Fantasia/Run Turn Queue Self-Test")]
        public static void Run()
        {
            int partyWins = 0;
            long turnSum = 0;

            for (int b = 0; b < Battles; b++)
            {
                var party = BuildSide(true, new[] { 8, 6, 10 }, 3, 6f, DurabilityTier.Strong);
                var enemies = BuildSide(false, new[] { 7, 9, 5 }, 2, 8f, DurabilityTier.Normal);
                var queue = new TurnQueue(party.Concat(enemies));

                int turns = 0;
                var actor = queue.Advance();
                while (party.Any(c => c.IsAlive) && enemies.Any(c => c.IsAlive) && turns < MaxTurns)
                {
                    var target = (actor.IsPlayerSide ? enemies : party).First(c => c.IsAlive);
                    var result = WeaponAttackResolver.Resolve(actor.Weapon, focusSpent: 0);
                    target.TakeDamage(Mathf.RoundToInt(result.Damage));

                    turns++;
                    actor = queue.Advance();
                }

                if (turns >= MaxTurns)
                {
                    Debug.LogWarning($"[TurnQueue] 배틀 {b}: {MaxTurns}턴 넘게 안 끝남 — 확인 필요");
                    continue;
                }

                if (party.Any(c => c.IsAlive)) partyWins++;
                turnSum += turns;
            }

            Debug.Log($"[TurnQueue] {Battles}회 자동 전투 — 파티 승률 {(float)partyWins / Battles:P1}, 평균 {turnSum / (float)Battles:F1}턴");
        }

        private static System.Collections.Generic.List<Combatant> BuildSide(
            bool isPlayerSide, int[] speeds, int slotCount, float damagePerSlot, DurabilityTier durability)
        {
            var weapon = new WeaponDefinition { SlotCount = slotCount, BaseDamagePerSlot = damagePerSlot, Durability = durability };
            return speeds.Select((speed, i) => new Combatant
            {
                Name = $"{(isPlayerSide ? "Party" : "Enemy")} {i + 1}",
                IsPlayerSide = isPlayerSide,
                MaxHP = 30,
                CurrentHP = 30,
                Speed = speed,
                MaxFocus = 2,
                Focus = 2,
                Weapon = weapon,
            }).ToList();
        }
    }
}
