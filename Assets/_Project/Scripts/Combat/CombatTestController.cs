using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Combat
{
    // Play-mode test harness: builds a 3v3 fight over the CombatTestSceneSetup
    // capsules and drives it entirely from OnGUI buttons, so slot-roll combat +
    // the turn queue can be exercised with no UI/art in place yet. Logic here
    // (Attack/EndTurn/etc.) is what a real combat UI would call later.
    public class CombatTestController : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private List<Combatant> _party;
        private List<Combatant> _enemies;
        private TurnQueue _turnQueue;
        private readonly List<string> _log = new List<string>();
        private readonly Dictionary<Combatant, Renderer> _renderers = new Dictionary<Combatant, Renderer>();

        private void Start()
        {
            _party = BuildSide("Party", isPlayerSide: true, new[] { 8, 6, 10 },
                new WeaponDefinition { SlotCount = 3, BaseDamagePerSlot = 6f, Durability = DurabilityTier.Strong });
            _enemies = BuildSide("Enemy", isPlayerSide: false, new[] { 7, 9, 5 },
                new WeaponDefinition { SlotCount = 2, BaseDamagePerSlot = 8f, Durability = DurabilityTier.Normal });

            _turnQueue = new TurnQueue(_party.Concat(_enemies));
            Log($"전투 시작 — 선공: {_turnQueue.Advance().Name}");
        }

        private List<Combatant> BuildSide(string label, bool isPlayerSide, int[] speeds, WeaponDefinition weapon)
        {
            var list = new List<Combatant>();
            for (int i = 0; i < speeds.Length; i++)
            {
                var combatant = new Combatant
                {
                    Name = $"{label} {i + 1}",
                    IsPlayerSide = isPlayerSide,
                    MaxHP = 30,
                    CurrentHP = 30,
                    Speed = speeds[i],
                    MaxFocus = 2,
                    Focus = 2,
                    Weapon = weapon,
                };
                list.Add(combatant);

                var go = GameObject.Find(combatant.Name);
                if (go != null) _renderers[combatant] = go.GetComponent<Renderer>();
            }
            return list;
        }

        private bool CombatOver => !_party.Any(c => c.IsAlive) || !_enemies.Any(c => c.IsAlive);

        private void Attack(int focusSpent)
        {
            var actor = _turnQueue.Current;
            var target = (actor.IsPlayerSide ? _enemies : _party).FirstOrDefault(c => c.IsAlive);
            if (target == null) return;

            focusSpent = Mathf.Min(focusSpent, actor.Focus);
            actor.Focus -= focusSpent;

            var result = WeaponAttackResolver.Resolve(actor.Weapon, focusSpent);
            int damage = Mathf.RoundToInt(result.Damage);
            target.TakeDamage(damage);

            Log($"{actor.Name} → {target.Name}: {result.SuccessCount}/{actor.Weapon.SlotCount}슬롯 성공" +
                (result.IsPerfect ? " (퍼펙트!)" : "") +
                $", {damage} 데미지 (잔여 HP {target.CurrentHP}/{target.MaxHP})");

            RefreshVisual(target);
            EndTurn();
        }

        private void SkipTurn()
        {
            Log($"{_turnQueue.Current.Name} 턴 넘김");
            EndTurn();
        }

        private void EndTurn()
        {
            if (CombatOver)
            {
                Log(_party.Any(c => c.IsAlive) ? "전투 종료 — 파티 승리" : "전투 종료 — 적 승리");
                return;
            }

            Log($"— {_turnQueue.Advance().Name} 턴 —");
        }

        // Only a win clears the encounter tile — a loss leaves it to fight again.
        private void ReturnToBoard(bool partyWon)
        {
            var session = BoardSession.Instance;
            if (partyWon && session != null && session.PendingEncounterCoord.HasValue)
            {
                session.ClearedEncounters.Add(session.PendingEncounterCoord.Value);
            }
            if (session != null) session.PendingEncounterCoord = null;

            SceneManager.LoadScene("BoardTest");
        }

        private void RefreshVisual(Combatant combatant)
        {
            if (!_renderers.TryGetValue(combatant, out var renderer) || renderer == null) return;

            var color = combatant.IsAlive
                ? (combatant.IsPlayerSide ? new Color(0.25f, 0.4f, 0.85f) : new Color(0.8f, 0.25f, 0.25f))
                : new Color(0.2f, 0.2f, 0.2f);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        private void Log(string message)
        {
            _log.Add(message);
            if (_log.Count > 8) _log.RemoveAt(0);
            Debug.Log(message);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 440, 420), GUI.skin.box);

            if (!CombatOver)
            {
                var actor = _turnQueue.Current;
                GUILayout.Label($"현재 턴: {actor.Name}  (포커스 {actor.Focus}/{actor.MaxFocus})");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("공격 (포커스 0)")) Attack(0);
                if (actor.Focus >= 1 && GUILayout.Button("포커스 1 소모")) Attack(1);
                if (actor.Focus >= 2 && GUILayout.Button("포커스 전부 소모")) Attack(actor.Focus);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("턴 넘기기")) SkipTurn();
            }
            else
            {
                bool partyWon = _party.Any(c => c.IsAlive);
                GUILayout.Label(partyWon ? "승리!" : "패배...");
                if (GUILayout.Button("보드로 돌아가기")) ReturnToBoard(partyWon);
            }

            GUILayout.Space(8);
            GUILayout.Label("파티: " + string.Join("   ", _party.Select(c => $"{c.Name} {c.CurrentHP}/{c.MaxHP}")));
            GUILayout.Label("적: " + string.Join("   ", _enemies.Select(c => $"{c.Name} {c.CurrentHP}/{c.MaxHP}")));

            GUILayout.Space(8);
            foreach (var line in _log) GUILayout.Label(line);

            GUILayout.EndArea();
        }
    }
}
