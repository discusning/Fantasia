using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Fantasia.Combat
{
    // 보스 차례: BossBrain 기술·예고·후속 행동 (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ── D14 보스 차례: BossBrain이 고른 기술 (패턴·임계치 예고·후속 행동) ──
        private IEnumerator BossTurn(BattleUnit boss)
        {
            var action = BossBrain.NextAction(boss.Enemy, boss.Boss, boss.CurrentHP, boss.MaxHP);
            if (action == null)
            {
                var t = EnemyAI.ChooseTarget(boss, _allies);
                if (t != null) yield return ResolveAttack(boss, t, boss.BasicAttack(0), 0, boss.OnCritStatuses, boss.OnCritTurnShift, null);
                yield break;
            }

            _hud.SetHint($"{boss.DisplayName} — [{action.Name}]");
            _hud.PopText(_camera, ModelTop(boss) + Vector3.up * 0.4f, action.Name, BattleHud.StatusColor);
            switch (action.Kind)
            {
                case EnemyActionKind.Attack:
                    var target = EnemyAI.ChooseTarget(boss, _allies);
                    if (target == null) yield break;
                    Log($"{boss.DisplayName} [{action.Name}] → {target.DisplayName}");
                    yield return ResolveAttack(boss, target, BossRequest(boss, action), 0, action.OnCritStatuses, TurnShift.None, null);
                    break;

                case EnemyActionKind.AreaAttack:
                    yield return ResolveEnemyAreaAttack(boss, action);
                    break;

                case EnemyActionKind.Announce:
                    // 예고는 피해 없음 (보스 공통 규칙). HP 바 아래 예고는 실행·저지·사망·전투 종료까지 유지, 하단 알림은 2초.
                    MotionOf(boss)?.Announce(); // 11/12 N6 손을 높이 들고 위협 (1초)
                    var pending = boss.Boss.Pending;
                    string next = pending != null ? $"다음 보스 행동: {pending.FollowUp.Name}" : "";
                    Log($"{boss.DisplayName} 예고 [{action.Name}] {next}");
                    RefreshBossTelegraph();
                    _hud.ShowNotice($"예고! {boss.DisplayName} [{action.Name}] — {next}", 2f);
                    yield return new WaitForSeconds(1.2f);
                    break;

                case EnemyActionKind.Fizzle:
                    // 저지당한 봉합: 예정된 회복 차례를 "봉합 실패"로 소모 (즉시 일반 공격으로 바꾸지 않음) / 아군 차례 보장으로 미룸
                    Log($"{boss.DisplayName} [{action.Name}] — {action.Description}");
                    RefreshBossTelegraph();
                    yield return new WaitForSeconds(0.9f);
                    break;

                case EnemyActionKind.SelfHeal:
                    // 11/12 N6 금지된 봉합: 왼팔을 세 번 꿰매는 동작 → 꿰맨 뒤 회복 숫자
                    if (MotionOf(boss)?.SelfHeal() == true) yield return new WaitForSeconds(CharacterMotion.SelfHealLead);
                    int healed = boss.HealEnemy(action.HealAmount);
                    Log($"{boss.DisplayName} [{action.Name}] HP +{healed} · HP {boss.CurrentHP}/{boss.MaxHP}");
                    RefreshBossTelegraph();
                    _hud.ShowNotice($"{boss.DisplayName} [{action.Name}] HP +{healed}", 2f);
                    _hud.PopText(_camera, ModelTop(boss), $"+{healed}", BattleHud.HealColor);
                    _hud.RefreshEnemies(null);
                    yield return new WaitForSeconds(0.7f);
                    break;
            }
        }

        private static AttackRequest BossRequest(BattleUnit boss, EnemyAction action)
        {
            var request = AttackResolver.FromEnemy(boss.Enemy, action.Coefficient, action.UsesMagic ? DamageType.Magic : DamageType.Physical);
            request.AttackValue = Mathf.Max(0, request.AttackValue - boss.Statuses.AttackPenalty);
            return request;
        }

        // 적 광역 (봉인된 수은 등): 공통 슬롯 판정 1회, 각 아군의 방어·회피는 따로 (보스 공통 규칙).
        private IEnumerator ResolveEnemyAreaAttack(BattleUnit boss, EnemyAction action)
        {
            var request = BossRequest(boss, action);
            var slots = SlotCheck.RollWithChance(request.SlotCount, request.SlotChance, 0);
            string summary = slots.AllSucceeded ? "크리티컬! (전체)" : $"{slots.SuccessCount}/{slots.SlotCount} 성공";
            yield return _hud.PlayRoll($"{boss.DisplayName} [{action.Name}] → 모든 아군", slots, 0, summary,
                slots.AllSucceeded ? BattleHud.CritColor : BattleHud.SlotFailure);
            yield return PlayAttackMotion(boss);
            BeginAreaLog();
            foreach (var ally in _allies.Where(a => a.InBattle).ToList())
            {
                var defense = ally.Defense();
                var outcome = AttackResolver.Compute(request, defense, slots, Fantasia.Dice.DiceRoller.RollChance(defense.EvasionChance));
                ApplyHit(boss, ally, request, outcome, 0, action.OnCritStatuses, TurnShift.None, null);
                // 봉인된 수은: 명중하면 확정 상태 (크리티컬이어도 추가 없음, 회피하면 없음)
                if (!outcome.Evaded && ally.IsAlive && action.OnHitStatuses != null && action.OnHitStatuses.Length > 0) ApplyStatuses(ally, action.OnHitStatuses, 1);
            }
            EndAreaLog();
            yield return new WaitForSeconds(0.3f);
        }

        // 예고 중인 보스의 후속 행동을 계수 높은 공격 기술로 맞혀 취소 (아벨 모로 금지된 봉합: 1.2A 이상 + 실제 HP 피해 1 이상).
        // 0.9A 저지 사격·일반 공격·회피/피해 0 공격은 취소하지 않는다. 크리티컬 배율은 더하지 않는다 (Boss_Abel_v1.0).
        private void TryCancelBossFollowUp(BattleUnit target, SkillDefinition skill)
        {
            if (target == null || target.Boss == null || skill == null || _lastOutcomeEvaded) return;
            var pending = target.Boss.Pending;
            if (!BossBrain.TryCancel(target.Boss, skill.Coefficient, _lastDamageDealt)) return;
            Log($"  → {target.DisplayName}의 [{pending.FollowUp.Name}] 저지! ({skill.DisplayName} {skill.Coefficient:0.##}배)");
            AnnounceSealStopped(target, pending);
        }

        // 「봉합 방해」 전용 행동: 예고 중 아군 개인 턴에만, 행동 1회·MP 0, 피해 0. 11/14부터 인지 판정 2칸 (PlayerInterrupt).
        private BattleUnit InterruptibleBoss => _enemies.FirstOrDefault(e => e.IsAlive && e.Boss != null && BossBrain.CanInterrupt(e.Boss));

        private void OnInterruptClicked()
        {
            if (_phase == Phase.ChoosingTarget) CancelTargeting(quiet: true); // 11/11 고르던 대상 취소 후 새 행동
            if (_phase != Phase.ChoosingAction) return; // 두 번 눌러도 한 번만 (행동하면 페이즈가 바뀜)
            var boss = InterruptibleBoss;
            if (boss == null) return;
            SaveBattle("전투 행동 확정 — 봉합 방해", CommandFor(BattleSnapshot.CommandKind.Interrupt));
            _phase = Phase.Resolving; // 두 번 눌러도 한 번만
            _hud.HideActions();
            StartCoroutine(PlayerInterrupt(boss));
        }

        // 11/14 팀장 결정: 봉합 방해에 성공·실패 (판정 없이 확정 → 판정 2칸).
        //  인지로 2칸 판정, 1칸 이상 성공 = 저지 성공. 집중 1을 걸면 1칸 확정 = 반드시 성공 (집중력 0이어도 버튼은 그대로, 확률로 시도).
        //  실패 = 행동 1회 소모, 봉합 예고는 그대로 → 다음 아군이 다시 봉합 방해 또는 1.2배 이상 공격 기술로 끊을 수 있음.
        //  판정 화면은 퇴각·공격과 같은 판정 칸(PlayRoll)을 그대로 씀.
        public const int InterruptSlots = 2;
        private IEnumerator PlayerInterrupt(BattleUnit boss)
        {
            var pending = boss.Boss.Pending;
            int stat = _current.Member.FinalStats.Perception;
            int focus = Mathf.Clamp(_hud.FocusCount, 0, Mathf.Min(_current.Member.CurrentFocus, 1));
            if (focus > 0) _current.Member.TrySpendFocus(focus);
            var slots = SlotCheck.Roll(InterruptSlots, stat, focus);
            bool ok = slots.SuccessCount >= 1;
            yield return _hud.PlayRoll($"{_current.DisplayName} [{pending.InterruptName}] → {boss.DisplayName} (인지 {stat})", slots, focus,
                ok ? $"저지 성공 — {pending.FollowUp.Name} 취소" : "저지 실패 — 봉합 예고 그대로",
                ok ? BattleHud.SlotSuccess : BattleHud.SlotFailure);
            if (ok && BossBrain.TryInterrupt(boss.Boss))
            {
                if (_playLog != null) _playLog.Interrupts++;
                Log($"{_current.DisplayName} [{pending.InterruptName}] 판정 {slots.SuccessCount}/{slots.SlotCount}{(focus > 0 ? " (집중 1)" : "")} → {boss.DisplayName}의 [{pending.FollowUp.Name}] 저지 성공");
                AnnounceSealStopped(boss, pending);
            }
            else
            {
                DevInterruptFails++;
                Log($"{_current.DisplayName} [{pending.InterruptName}] 판정 {slots.SuccessCount}/{slots.SlotCount} → 저지 실패 (예고 유지)");
                _hud.PopText(_camera, ModelTop(boss) + Vector3.up * 0.9f, "저지 실패", BattleHud.SlotFailure);
                _hud.FlashBossTelegraph(boss, $"{pending.Announce.Name} 저지 실패", 1.5f);
                _hud.ShowNotice($"저지 실패 — {boss.DisplayName}의 {pending.Announce.Name} 예고가 남아 있다. 다른 아군이 다시 막을 수 있다", 2f);
            }
            yield return new WaitForSeconds(0.8f);
            NextTurn();
        }
        public static int DevInterruptFails;

        private void AnnounceSealStopped(BattleUnit boss, EnemyThresholdAction pending)
        {
            RefreshBossTelegraph();
            _hud.PopText(_camera, ModelTop(boss) + Vector3.up * 0.9f, "저지!", BattleHud.CritColor);
            _hud.FlashBossTelegraph(boss, $"{pending.Announce.Name} 저지 성공", 1.5f);
            _hud.ShowNotice($"{boss.DisplayName}의 {pending.Announce.Name} 저지 성공 — 다음 보스 행동은 {(string.IsNullOrEmpty(pending.FailedName) ? "실패" : pending.FailedName)}", 2f);
        }

        // 보스 예고 표시 갱신 — HP 바 아래 (시간으로 사라지지 않음) + 타임라인 표식. 실행·저지·사망·전투 종료 때 해제.
        private void RefreshBossTelegraph()
        {
            foreach (var e in _enemies.Where(u => u.Boss != null))
            {
                var p = e.Boss.Pending;
                if (!e.IsAlive || _phase == Phase.Finished || p == null || e.Boss.PendingCancelled) { _hud.SetBossTelegraph(e, null); continue; }
                string counter = string.Join("\n", new[]
                {
                    !string.IsNullOrEmpty(p.InterruptName) ? $"대응: {p.InterruptName}(행동 1회 · 판정 2칸 중 1칸 성공 · 집중 1이면 확정)" : null,
                    p.CancelMinCoefficient > 0f ? $"또는 {p.CancelMinCoefficient:0.#}배 이상 공격 기술 — {p.CounterHint}" : null,
                }.Where(s => s != null));
                _hud.SetBossTelegraph(e, $"{p.Announce.Name} 예고 중 | 다음 보스 행동: {p.FollowUp.Description}\n{counter}");
            }
            _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
        }

        // 아군 행동 메뉴 열기 — 예고 중이면 「봉합 방해」 버튼도 (집중력이 없어도 활성)
        private void ShowAllyActions()
        {
            foreach (var e in _enemies.Where(u => u.IsAlive && u.Boss != null)) BossBrain.NotifyAllyCommandTurn(e.Boss);
            _hud.RetreatBlocked = EscapeBlocked;
            _hud.ShowActions(_current, CanRevive());
            var boss = InterruptibleBoss;
            _hud.SetInterrupt(boss != null ? boss.Boss.Pending.InterruptName : null);
        }

        private bool _lastOutcomeEvaded;
        private int _lastDamageDealt;

        // 보스 봉쇄: 전투 중 퇴각·탈출권 불가 (System.md "주요 보스는 … 전투 중 퇴각을 막는다")
        private bool EscapeBlocked => _enemies.Any(e => e.IsAlive && e.Enemy.BlocksEscape);

        private IEnumerator ConfusedTurn(BattleUnit ally)
        {
            _hud.SetHint($"{ally.DisplayName} 혼란 — 행동을 고를 수 없다");
            yield return new WaitForSeconds(0.6f);
            var potion = Inventory.Consumables.Select(s => s.Definition)
                .FirstOrDefault(d => d.Effect == ConsumableEffect.RestoreHP && d.UsableIn(UseContext.Battle));
            if (potion != null && GameRandom.Value < 0.5f)
            {
                Log($"{ally.DisplayName} 혼란으로 회복약 사용");
                yield return ResolveItem(ally, ally, potion);
                NextTurn();
                yield break;
            }
            var targets = _enemies.Where(e => e.InBattle).ToList();
            if (targets.Count > 0)
            {
                Log($"{ally.DisplayName} 혼란으로 임의 공격");
                ally.Member.TrySpendMP(ally.BasicAttackMPCost);
                yield return ResolveAttack(ally, targets[GameRandom.Range(0, targets.Count)], ally.BasicAttack(0), 0, ally.OnCritStatuses, ally.OnCritTurnShift, null);
            }
            NextTurn();
        }
    }
}
