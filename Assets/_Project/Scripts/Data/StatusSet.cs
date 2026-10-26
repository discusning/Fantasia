using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fantasia.Data
{
    // 한 캐릭터(아군 PartyMember 또는 전투 중 적)에 걸린 상태 효과 (D12, System.md "상태 효과 수치와 처리 순서").
    // - 같은 종류는 중첩하지 않고 최근 것으로 갱신(기간 리셋). 중독만 MaxStacks까지 스택.
    // - 기간은 "그 대상의 개인 턴" 기준. 개인 턴 시작 처리 순서: 지속 피해 → (호출부) 사망 확인 →
    //   기간 감소 → 행동 선택. 그래서 1턴짜리 혼란도 다음 자기 턴의 행동에는 적용된다.
    // - 출혈·중독 피해는 HP를 1 미만으로 낮추지 않는다.
    public class StatusSet
    {
        public class Active
        {
            public StatusEffectDefinition Definition;
            public int RemainingTurns; // -1 = 정화 전까지
            public int Stacks = 1;
            public StatusEffectType Effect => Definition.Effect;
        }

        public readonly struct TurnStart
        {
            public readonly int Damage;     // 지속 피해 합 (HP 1 하한 적용 전)
            public readonly bool Confused;  // 이번 턴 행동이 강제된다
            public readonly string Summary; // 로그용
            public TurnStart(int damage, bool confused, string summary) { Damage = damage; Confused = confused; Summary = summary; }
        }

        private readonly List<Active> _active = new();
        public IReadOnlyList<Active> All => _active;

        public bool Has(StatusEffectType effect) => _active.Any(a => a.Effect == effect);
        private Active Find(StatusEffectType effect) => _active.FirstOrDefault(a => a.Effect == effect);

        // 면역 (집중의 기도 = 혼란). 순수의 기도(저주·중독·화상)는 2차.
        public bool ImmuneToConfusion;

        // 부여. 반환값: 새로 걸렸거나 갱신·스택된 상태 (면역 등으로 안 걸리면 null — 면역은 기도·장비와 함께 추가).
        public Active Apply(StatusEffectDefinition definition)
        {
            if (definition == null || definition.Effect == StatusEffectType.None) return null;
            if (ImmuneToConfusion && definition.Effect == StatusEffectType.Confusion) return null;

            var existing = Find(definition.Effect);
            if (existing != null)
            {
                existing.Definition = definition;
                existing.RemainingTurns = definition.DurationTurns;
                existing.Stacks = Mathf.Min(existing.Stacks + 1, Mathf.Max(1, definition.MaxStacks));
                return existing;
            }

            var added = new Active { Definition = definition, RemainingTurns = definition.DurationTurns, Stacks = 1 };
            _active.Add(added);
            return added;
        }

        public void Remove(StatusEffectType effect) => _active.RemoveAll(a => a.Effect == effect);

        // D27 불러오기 — 저장된 상태를 그대로 (기간·스택)
        public void Restore(StatusEffectDefinition definition, int remainingTurns, int stacks)
        {
            if (definition == null) return;
            _active.Add(new Active { Definition = definition, RemainingTurns = remainingTurns, Stacks = stacks });
        }

        // 전투가 끝날 때 — 승리로 지워지는 것만 지우고 중독처럼 남는 건 유지.
        public void ClearOnBattleEnd() => _active.RemoveAll(a => a.Definition.ClearedByCombatVictory);
        public void ClearAll() => _active.Clear(); // 전투 저장 복원용

        // 개인 턴 시작: 지속 피해 합계와 혼란 여부를 계산한 뒤 기간을 1 줄인다.
        public TurnStart BeginTurn()
        {
            int damage = 0;
            var parts = new List<string>();
            foreach (var a in _active)
            {
                switch (a.Effect)
                {
                    case StatusEffectType.WeakBleed:
                        damage += a.Definition.Magnitude;
                        parts.Add($"{a.Definition.DisplayName} {a.Definition.Magnitude}");
                        break;
                    case StatusEffectType.Poison:
                        damage += a.Stacks * a.Definition.Magnitude;
                        parts.Add($"{a.Definition.DisplayName}×{a.Stacks} {a.Stacks * a.Definition.Magnitude}");
                        break;
                }
            }
            bool confused = Has(StatusEffectType.Confusion);
            if (confused) parts.Add("혼란");

            foreach (var a in _active) if (a.RemainingTurns > 0) a.RemainingTurns--;
            _active.RemoveAll(a => a.RemainingTurns == 0);

            return new TurnStart(damage, confused, string.Join(", ", parts));
        }

        // 지속 피해는 HP를 1 미만으로 낮추지 않는다.
        public static int ClampDamageOverTime(int damage, int currentHP) => Mathf.Clamp(damage, 0, Mathf.Max(0, currentHP - 1));

        // 능력치 보정 — 같은 의미의 속도 감소는 가장 큰 값 하나만 (중독의 전 스탯 감소는 별개로 1회).
        public int AllStatPenalty => Find(StatusEffectType.Poison)?.Definition.StatPenalty ?? 0;
        public int SpeedPenalty => Find(StatusEffectType.Bind)?.Definition.Magnitude ?? 0;
        public int AttackPenalty => Find(StatusEffectType.AttackDown)?.Definition.Magnitude ?? 0;
        public int DefensePenalty => Find(StatusEffectType.DefenseDown)?.Definition.Magnitude ?? 0;
        public bool EvasionRevealed => Has(StatusEffectType.EvasionRevealed);
        public float AccuracyBonus => (Find(StatusEffectType.AccuracyUp)?.Definition.Magnitude ?? 0) / 100f;
    }
}
