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
    // 소모품·전투 중 부활 (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ── D17 소모품: 개인 행동 1회. 아군 대상(회복·정화)·적 대상(성수)·즉시(화염병·탈출권) ──
        private PartyInventory Inventory => BoardSession.Instance.Party.Inventory;

        private void OnQuickSlotClicked(PartyMember member, int slot)
        {
            Debug.Log($"[QuickSlot] {member?.Job.DisplayName} {slot + 1}번 칸 클릭 · 단계 {_phase} · 차례 {_current?.DisplayName}");
            if (_phase != Phase.ChoosingAction || _current == null) return;
            if (_current.Member != member)
            {
                // 11/14 팀장 QA "1~4번 칸이 안 됨": 다른 사람 카드의 칸은 조용히 무시되던 것 → 안내
                if (member.QuickSlots[slot] != null) _hud.SetHint($"지금은 {_current.DisplayName}의 차례 — {_current.DisplayName} 카드의 칸이나 숫자키 1~4로 씁니다");
                return;
            }
            var def = member.QuickSlots[slot];
            if (def == null) { _hud.SetHint($"{slot + 1}번 칸이 비어 있음 (가방에서 소모품을 칸에 넣을 수 있음)"); return; }
            OnItemChosen(def);
        }

        private void OnItemChosen(ConsumableDefinition item)
        {
            if (_phase == Phase.ChoosingTarget && item != null) { bool same = _pendingItem == item; CancelTargeting(quiet: !same); if (same) return; }
            if (_phase != Phase.ChoosingAction || item == null) return;
            if (Inventory.CountOf(item) <= 0) { _hud.SetHint($"{item.DisplayName}: 남은 개수 없음"); return; }
            if (!item.UsableIn(UseContext.Battle)) { _hud.SetHint($"{item.DisplayName}: 전투에서 쓸 수 없음"); return; }
            if (item.Effect == ConsumableEffect.Escape && EscapeBlocked) { _hud.SetHint($"{item.DisplayName}: 보스 봉쇄 중에는 쓸 수 없음"); return; }
            _pendingSkill = null;
            _pendingItem = item;
            _hud.MarkChosenItem(item);
            if (ConsumableRules.TargetsAlly(item)) BeginTargeting(allies: true);
            else if (ConsumableRules.TargetsEnemy(item)) BeginTargeting(allies: false);
            else StartCoroutine(PlayerAct(null));
        }

        private IEnumerator ResolveItem(BattleUnit user, BattleUnit target, ConsumableDefinition item)
        {
            if (MotionOf(user)?.UseItem() == true) yield return new WaitForSeconds(CharacterMotion.ItemLead); // 11/11 아이템 동작 (N4)
            Inventory.RemoveConsumable(item);
            _playLog?.Item(item.DisplayName);
            if (_playLog != null && (item.Effect == ConsumableEffect.RestoreHP || item.Effect == ConsumableEffect.Cure)) _playLog.Heals++;
            if (item.Effect == ConsumableEffect.RestoreHP || item.Effect == ConsumableEffect.Cure) SfxPlayer.Play(SfxIds.Heal);
            switch (item.Effect)
            {
                case ConsumableEffect.RevealEvasion:
                    Log($"{user.DisplayName} [{item.DisplayName}] → {target.DisplayName}");
                    ApplyStatuses(target, item.AppliedStatuses, 0);
                    break;
                case ConsumableEffect.Firebomb:
                    // 모든 적에게 고정 기본값의 마법 피해, 각 마법 방어 적용, 크리티컬 없음
                    foreach (var enemy in _enemies.Where(e => e.InBattle).ToList())
                    {
                        int dmg = Mathf.Max(0, item.Amount - enemy.Defense().DefenseAgainst(DamageType.Magic));
                        enemy.TakeDamage(dmg);
                        _hud.PopText(_camera, ModelTop(enemy), dmg.ToString(), BattleHud.DamageColor);
                        Log($"{user.DisplayName} [{item.DisplayName}] → {enemy.DisplayName}: {dmg} · HP {enemy.CurrentHP}/{enemy.MaxHP}");
                        if (!enemy.IsAlive) KnockDown(enemy);
                        else PlayHitMotion(enemy);
                    }
                    _hud.RefreshEnemies(null);
                    break;
                case ConsumableEffect.Escape:
                    // 생존 아군 전원 퇴각 — 경험치·전리품 없음 (퇴각 결과로 처리)
                    Log($"{user.DisplayName} [{item.DisplayName}] — 생존 아군 전원 퇴각");
                    var escaping = _allies.Where(a => a.InBattle).ToList();
                    foreach (var ally in escaping) ally.HasRetreated = true;
                    yield return Withdraw(escaping);
                    break;
                default:
                    int speedBefore = target.Speed;
                    string line = ConsumableRules.ApplyToMember(item, target.Member);
                    Log($"{user.DisplayName}: {line}");
                    _hud.PopText(_camera, ModelTop(target), item.DisplayName, BattleHud.SlotSuccess);
                    NoticeSpeedChange(target, speedBefore);
                    break;
            }
            yield return new WaitForSeconds(0.6f);
        }

        // 11/11 팀장 결정: 안내 줄은 크게 "약점 간파 → 대상 적 선택" + 취소 방법
        private const string CancelHelp = " (취소: 우클릭·Esc)";
        private string TargetingHint()
        {
            if (_reviving) return "부활 → 쓰러진 아군 선택 (LP 1, HP 30%)" + CancelHelp;
            string what = _pendingItem != null ? _pendingItem.DisplayName : _pendingSkill != null ? _pendingSkill.DisplayName : "공격";
            if (_targetAllies) return $"{what} → 쓸 아군의 카드나 캐릭터를 누르세요" + CancelHelp; // 11/14 팀장 QA: 빠른 칸을 누른 뒤 무엇을 해야 할지 몰랐음
            return $"{what} → 대상 적 선택" + CancelHelp;
        }

        // 대상 고르기 취소 — 고른 칸 표시·대상 표시·적 카드 강조를 모두 지우고 행동 고르기로. quiet: 다른 행동으로 바로 넘어갈 때
        private int _escConsumedFrame = -1;
        public static bool ConsumesEscape { get; private set; } // 대상 고르는 중 Esc = 취소 (메뉴 안 열림)
        private void CancelTargeting(bool quiet = false)
        {
            if (_phase != Phase.ChoosingTarget) return;
            string what = _reviving ? "부활" : _pendingItem != null ? _pendingItem.DisplayName : _pendingSkill != null ? _pendingSkill.DisplayName : "공격";
            _phase = Phase.ChoosingAction;
            _pendingSkill = null;
            _pendingItem = null;
            _reviving = false;
            _hovered = null;
            HideMarker();
            _hud.RefreshEnemies(null);
            _hud.ClearChosen();
            if (!quiet) _hud.SetHint($"{what} 취소됨 — 행동을 다시 고르세요");
        }

        // 지금 고르는 행동의 대상이 될 수 있는지 — 부활은 쓰러진(LP 남은) 아군, 나머지는 전투 중인 해당 진영.
        private bool IsValidTarget(BattleUnit unit)
        {
            if (unit == null) return false;
            if (_reviving) return unit.IsAlly && unit.Member.IsDowned && unit.Member.CurrentLP > 0;
            if (_pendingItem != null && unit.IsAlly && unit.InBattle) return ConsumableRules.CanUseOn(_pendingItem, unit.Member, out _);
            return unit.InBattle && unit.IsAlly == _targetAllies;
        }

        private void OnUnitPicked(BattleUnit unit)
        {
            if (_phase != Phase.ChoosingTarget) return;
            if (_pendingItem != null && unit != null && unit.IsAlly && !ConsumableRules.CanUseOn(_pendingItem, unit.Member, out string why))
            {
                _hud.SetHint($"{_pendingItem.DisplayName}: {why}");
                return;
            }
            if (!IsValidTarget(unit)) return;
            StartCoroutine(PlayerAct(unit));
        }

        // ── D21 전투 중 부활: 살아 있는 아군의 행동 1회 + 대상 LP 1 → HP 30% (대상 LP 0이면 불가) ──
        private bool CanRevive() => _allies.Any(a => a.IsAlly && a.Member.IsDowned && a.Member.CurrentLP > 0 && !a.HasRetreated);

        private void OnReviveClicked()
        {
            if (_phase == Phase.ChoosingTarget) { bool same = _reviving; CancelTargeting(quiet: !same); if (same) return; }
            if (_phase != Phase.ChoosingAction || !CanRevive()) return;
            _pendingSkill = null;
            _reviving = true;
            BeginTargeting(allies: true);
        }

        private IEnumerator ResolveRevive(BattleUnit reviver, BattleUnit target)
        {
            target.Member.TrySpendLP();
            target.Member.Revive(DefeatRules.InBattleReviveHpRatio);
            SfxPlayer.Play(SfxIds.Revive);
            StandUp(target);
            Log($"{reviver.DisplayName} → {target.DisplayName} 부활 (LP 1 소모, 남은 LP {target.Member.CurrentLP}) · HP {target.CurrentHP}/{target.MaxHP} · 출혈·중독 제거 — 다음 라운드부터 행동");
            _hud.PopText(_camera, ModelTop(target), $"부활 +{target.CurrentHP}", BattleHud.HealColor);
            BattleVfx.Play(BattleVfx.Revive, ModelFeet(target), VfxScale(target)); // 11/13 이펙트 1차
            yield return new WaitForSeconds(1.2f); // 11/11 일어서기 동작(1.5초)이 보이게 0.7 → 1.2
        }

        // 비용 지불 → 쿨다운 → 효과. 기본 공격·스킬·부활 공용 (target: 단일 대상, 전체 대상이면 null).
        private IEnumerator PlayerAct(BattleUnit target)
        {
            // 행동 확정 → 저장 (결과 계산·표시 전). 이어하면 이 행동을 같은 난수로 다시 실행
            var kind = _reviving ? BattleSnapshot.CommandKind.Revive : _pendingItem != null ? BattleSnapshot.CommandKind.Item
                : _pendingSkill == null ? BattleSnapshot.CommandKind.Attack : BattleSnapshot.CommandKind.Skill;
            SaveBattle($"전투 행동 확정 — {kind}", CommandFor(kind, target, _pendingSkill, _pendingItem));
            _phase = Phase.Resolving;
            _hud.HideActions();
            HideMarker();
            var actor = _current;
            var skill = _pendingSkill;
            _pendingSkill = null;

            if (_reviving)
            {
                _reviving = false;
                yield return ResolveRevive(actor, target);
                NextTurn();
                yield break;
            }

            if (_pendingItem != null)
            {
                var item = _pendingItem;
                _pendingItem = null;
                yield return ResolveItem(actor, target, item);
                NextTurn();
                yield break;
            }

            if (skill == null)
            {
                int focus = FocusToUse(actor, null, 0);
                if (focus > 0) actor.Member.TrySpendFocus(focus);
                if (_playLog != null) { _playLog.MpSpent += actor.BasicAttackMPCost; _playLog.FocusSpent += focus; }
                actor.Member.TrySpendMP(actor.BasicAttackMPCost);
                yield return ResolveAttack(actor, target, actor.BasicAttack(focus), focus, actor.OnCritStatuses, actor.OnCritTurnShift, null);
                NextTurn();
                yield break;
            }

            // 스킬 비용과 추가 집중은 따로 (System.md) — 비용을 먼저 내고 남은 집중력으로 추가 집중.
            actor.Member.TrySpendFocus(skill.FocusCost);
            actor.Member.TrySpendMP(skill.MPCost);
            if (skill.HPCost > 0) actor.Member.TakeDamage(skill.HPCost);
            actor.StartCooldown(skill);
            int useFocus = FocusToUse(actor, skill, 0); // 비용을 낸 뒤 남은 집중력 안에서
            if (useFocus > 0) actor.Member.TrySpendFocus(useFocus);
            if (_playLog != null) { _playLog.MpSpent += skill.MPCost; _playLog.FocusSpent += skill.FocusCost + useFocus; if (skill.Effect == SkillEffect.Heal || skill.Effect == SkillEffect.Cleanse) _playLog.Heals++; }
            Log($"{actor.DisplayName} 스킬 [{skill.DisplayName}] ({BattleHud.SkillCostText(skill)}){(useFocus > 0 ? $" + 집중 {useFocus}" : "")}");

            switch (skill.Effect)
            {
                case SkillEffect.Attack when skill.Target == SkillTarget.AllEnemies:
                    yield return ResolveAreaAttack(actor, skill, useFocus);
                    break;
                case SkillEffect.Attack:
                    yield return ResolveAttack(actor, target, actor.SkillAttack(skill, useFocus), useFocus, skill.OnCritStatuses, skill.OnCritTurnShift, skill);
                    TryCancelBossFollowUp(target, skill);
                    break;
                case SkillEffect.Heal:
                {
                    // 11/13 팀장 지적: 다른 아군 회복 때 모습이 이상함 — 회복 동작은 "약을 아군 쪽으로 내밂"인데 적 쪽을 본 채 내밀었음
                    // → 다른 아군이면 먼저 그쪽으로 돌아서 내밀고, 끝나면 원래 방향으로
                    var turnedFrom = target != actor ? TurnToward(actor, target) : null;
                    if (turnedFrom.HasValue) yield return new WaitForSeconds(TurnTime);
                    if (MotionOf(actor)?.Heal() == true) yield return new WaitForSeconds(CharacterMotion.HealLead); // 11/11 회복 동작 (N1)
                    yield return ResolveHeal(actor, target, skill, useFocus);
                    if (turnedFrom.HasValue && _models.TryGetValue(actor, out var healerModel)) StartCoroutine(TurnModel(healerModel, turnedFrom.Value));
                    break;
                }
                case SkillEffect.Cleanse:
                {
                    var turnedFrom = target != actor ? TurnToward(actor, target) : null; // 회복과 같은 동작 → 같은 규칙
                    if (turnedFrom.HasValue) yield return new WaitForSeconds(TurnTime);
                    if (MotionOf(actor)?.Heal() == true) yield return new WaitForSeconds(CharacterMotion.HealLead);
                    Cleanse(target, skill.CleansedStatuses, skill.DisplayName);
                    if (SkillVfxKey(skill) is string ck) BattleVfx.Play(ck, ModelFront(target), VfxScale(target)); // 6차: 역병 정화 = 맑은 빛 (몸 앞 — 몸에 묻히지 않게)
                    yield return new WaitForSeconds(0.6f);
                    if (turnedFrom.HasValue && _models.TryGetValue(actor, out var cleanserModel)) StartCoroutine(TurnModel(cleanserModel, turnedFrom.Value));
                    break;
                }
                case SkillEffect.ApplyStatus:
                    if (MotionOf(actor)?.BuffSkill() == true) yield return new WaitForSeconds(CharacterMotion.SkillLead); // 11/11 강화 스킬 동작 (N3)
                    ApplyStatuses(target, skill.AppliedStatuses, 0);
                    yield return new WaitForSeconds(0.6f);
                    break;
            }
            _hud.RefreshEnemies(null);
            NextTurn();
        }

        // 방어: 지금 차례인 본인에게만 (대상 고르기 없음). 11/11 팀장 결정 — 방어력 +8 대신 보호막 8 (피해를 HP보다 먼저 받음, 다음 자기 차례에 사라짐)
        private void OnDefendClicked()
        {
            if (_phase == Phase.ChoosingTarget) CancelTargeting(quiet: true); // 고르던 스킬이 다른 아군에게 들어가지 않게
            if (_phase != Phase.ChoosingAction) return;
            SaveBattle("전투 행동 확정 — 방어", CommandFor(BattleSnapshot.CommandKind.Defend));
            _current.IsDefending = true;
            _current.Shield = AttackResolver.DefendShield;
            MotionOf(_current)?.Defend(); // 11/11 방어 동작 (N5)
            ShieldChanged(_current);
            Log($"{_current.DisplayName} 방어 태세 — 보호막 {AttackResolver.DefendShield} (다음 자기 차례까지)");
            _hud.PopText(_camera, ModelTop(_current), $"방어 태세 · 보호막 {AttackResolver.DefendShield}", new Color(0.95f, 0.96f, 1f));
            BattleVfx.Play(BattleVfx.Defend, ModelChest(_current), VfxScale(_current)); // 11/13 이펙트 1차
            NextTurn();
        }

        // 보호막 표시 갱신: 파티 카드 HP 바(흰색) + 발밑 방패
        private void ShieldChanged(BattleUnit unit)
        {
            if (unit == null) return;
            if (unit.IsAlly) PartyHudPanel.SetShield(unit.Member, unit.Shield);
            // 머리 위 (발밑은 행동 패널에 가려짐). 11/12: 좌표 고정 대신 캐릭터를 따라감 — 이동했다 돌아와도 바로 제자리
            if (_models.TryGetValue(unit, out var model) && model != null)
                _hud.SetShieldMark(unit, _camera, model.transform, ModelTop(unit) - Vector3.up * 0.15f - model.transform.position, unit.Shield);
            else _hud.SetShieldMark(unit, _camera, ModelTop(unit) - Vector3.up * 0.15f, unit.Shield);
        }

        // 전투 중 퇴각: 고른 캐릭터의 속도로 2슬롯 판정, 성공하면 생존 아군 전원 전투 이탈 (2026-11-06 변경, 확률은 System.md 표 그대로).
        private void OnRetreatClicked()
        {
            if (_phase == Phase.ChoosingTarget) CancelTargeting(quiet: true);
            if (_phase != Phase.ChoosingAction) return;
            if (EscapeBlocked) { _hud.SetHint("보스가 퇴로를 막고 있다 — 퇴각할 수 없다"); return; }
            StartCoroutine(PlayerRetreat());
        }

        private IEnumerator PlayerRetreat()
        {
            SaveBattle("전투 행동 확정 — 퇴각", CommandFor(BattleSnapshot.CommandKind.Retreat));
            _phase = Phase.Resolving;
            _hud.HideActions();
            int speed = _current.Member.FinalStats.Speed;
            var slots = SlotCheck.Roll(EncounterRules.RetreatDuringCombatSlotCount, speed);
            bool ok = slots.AllSucceeded;
            yield return _hud.PlayRoll($"{_current.DisplayName} 퇴각 (속도 {speed})", slots, 0,
                ok ? "퇴각 성공 — 파티 전원 이탈" : "퇴각 실패 — 행동 소모", ok ? BattleHud.SlotSuccess : BattleHud.SlotFailure);
            Log($"{_current.DisplayName} 퇴각 판정 {slots.SuccessCount}/{slots.SlotCount} → {(ok ? "성공 — 생존 아군 전원 퇴각" : "실패")}");
            if (ok)
            {
                // 2026-11-06 변경: 한 명이 퇴각에 성공하면 파티 전체가 함께 물러난다 (쓰러진 동료 처리는 탈출권과 같음 — DefeatRules).
                var leaving = _allies.Where(a => a.InBattle).ToList();
                foreach (var ally in leaving) ally.HasRetreated = true;
                yield return Withdraw(leaving);
            }
            NextTurn();
        }

        // 11/11 팀장 결정: 퇴각·탈출권 — 순간 사라지던 것을 "돌아서서 뒤로 물러난 뒤 사라짐"으로 (0.2초 돌기 + 0.5초 2.5m 물러남).
        // 11/13 팀장 요청: 물러나는 동안 걷기 모션(N9). 걸음이 보이게 물러나는 시간 0.5 → 0.8초 (걷기 한 바퀴 0.95초).
        private const float WithdrawTurn = 0.2f, WithdrawMove = 0.8f, WithdrawDistance = 2.5f;
        private IEnumerator Withdraw(List<BattleUnit> units)
        {
            var models = units.Where(u => _models.ContainsKey(u)).Select(u => _models[u]).Where(m => m != null && m.activeSelf).ToList();
            var from = models.Select(m => m.transform.position).ToList();
            var faceFrom = models.Select(m => m.transform.rotation).ToList();
            var back = models.Select(m => { var f = -m.transform.forward; f.y = 0f; return f.normalized; }).ToList();
            var walkers = models.Select(m => CharacterMotion.Of(m)).Where(w => w != null).ToList();
            foreach (var w in walkers) w.SetWalking(true); // 도는 동안 걸음 시작 → 자연스럽게 이어짐
            for (float t = 0f; t < WithdrawTurn + WithdrawMove; t += Time.deltaTime)
            {
                for (int i = 0; i < models.Count; i++)
                {
                    if (models[i] == null) continue;
                    float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / WithdrawTurn));
                    models[i].transform.rotation = faceFrom[i] * Quaternion.Euler(0f, 180f * turn, 0f);
                    float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - WithdrawTurn) / WithdrawMove));
                    models[i].transform.position = from[i] + back[i] * (WithdrawDistance * move);
                }
                yield return null;
            }
            foreach (var w in walkers) if (w != null) w.SetWalking(false);
            foreach (var m in models) if (m != null) m.SetActive(false);
        }
    }
}
