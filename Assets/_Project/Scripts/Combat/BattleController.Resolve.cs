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
    // 판정 공통: 공격·회복·상태 (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ───────────── 판정 공통 ─────────────
        // 피의 기도: 인간형 적에게 피해 +10% (계수에 곱함 — 크리티컬 포함)
        private static AttackRequest WithPrayerDamage(BattleUnit attacker, BattleUnit target, AttackRequest request)
        {
            if (attacker.IsAlly && !target.IsAlly && attacker.Member.Prayer == PrayerKind.Blood && PrayerRules.IsHumanoid(target.Enemy))
                request.Coefficient *= 1f + PrayerRules.BloodHumanoidDamage;
            return request;
        }

        // 집중 사용 개수 (2026-11-06 변경): 행동 메뉴에서 플레이어가 고른 개수만큼 슬롯을 확정 성공.
        // 남은 집중력(reserved = 아직 안 낸 스킬 집중 비용)과 이 행동의 슬롯 수를 넘지 않게 자른다. 슬롯 없는 스킬은 0.
        private int FocusToUse(BattleUnit actor, SkillDefinition skill, int reserved)
        {
            if (skill != null && !skill.RollsSlots) return 0;
            int slots = skill != null ? actor.SkillAttack(skill, 0).SlotCount : actor.BasicAttack(0).SlotCount;
            return Mathf.Clamp(_hud.FocusCount, 0, Mathf.Max(0, Mathf.Min(actor.Member.CurrentFocus - reserved, slots)));
        }

        // 단일 공격 (기본 공격·공격 스킬·적 공격). skill이 null이면 기본 공격.
        private IEnumerator ResolveAttack(BattleUnit attacker, BattleUnit target, AttackRequest request, int useFocus,
            StatusEffectDefinition[] onCritStatuses, TurnShift onCritShift, SkillDefinition skill)
        {
            bool weaponBased = skill == null || skill.SlotCount <= 0;
            request = WithFirstGunShot(attacker, request, weaponBased);
            request = WithPrayerDamage(attacker, target, request);

            var outcome = AttackResolver.Resolve(request, target.Defense());
            if (attacker.IsAlly && weaponBased) attacker.Statuses.Remove(StatusEffectType.AccuracyUp); // 영점 조정은 다음 무기 공격 1회

            string result = ResultText(outcome);
            Color color = ResultColor(outcome);
            string title = skill != null ? $"{attacker.DisplayName} [{skill.DisplayName}] → {target.DisplayName}" : $"{attacker.DisplayName} → {target.DisplayName}";
            yield return _hud.PlayRoll(title, outcome.Slots, useFocus, result, color);
            yield return PlayAttackMotion(attacker, IsMelee(attacker, request) ? target : null);

            ApplyHit(attacker, target, request, outcome, useFocus, onCritStatuses, onCritShift, skill);
            _hud.RefreshEnemies(null);
            yield return new WaitForSeconds(0.25f);
        }

        // 모든 적 공격 (검은 연무): 공통 슬롯 판정 1회, 각 적의 방어·회피는 따로 (System.md 보스 광역 규칙과 같은 방식).
        private IEnumerator ResolveAreaAttack(BattleUnit attacker, SkillDefinition skill, int useFocus)
        {
            var request = attacker.SkillAttack(skill, useFocus);
            var slots = SlotCheck.RollWithChance(request.SlotCount, request.SlotChance, useFocus);
            string summary = slots.AllSucceeded ? "크리티컬! (전체)" : $"{slots.SuccessCount}/{slots.SlotCount} 성공";
            yield return _hud.PlayRoll($"{attacker.DisplayName} [{skill.DisplayName}] → 모든 적", slots, useFocus, summary,
                slots.AllSucceeded ? BattleHud.CritColor : BattleHud.SlotSuccess);
            yield return PlayAttackMotion(attacker);

            BeginAreaLog();
            foreach (var enemy in _enemies.Where(e => e.InBattle).ToList())
            {
                var defense = enemy.Defense();
                var hit = WithPrayerDamage(attacker, enemy, request);
                var outcome = AttackResolver.Compute(hit, defense, slots, Fantasia.Dice.DiceRoller.RollChance(defense.EvasionChance));
                ApplyHit(attacker, enemy, hit, outcome, useFocus, skill.OnCritStatuses, skill.OnCritTurnShift, skill);
            }
            EndAreaLog();
            _hud.RefreshEnemies(null);
            yield return new WaitForSeconds(0.25f);
        }

        // 보호막 나누기: 전체 피해 → 보호막이 먼저 (보호막만큼) → 남은 피해만 HP (HP를 넘는 부분은 버림).
        // 예) 아벨 모로 10 vs 보호막 8 → 보호막 -8, HP -2. 화면 숫자는 "보호막 -8"과 "2".
        public static (int absorbed, int hpDamage) ShieldSplit(int damage, int shield, int currentHP)
        {
            int absorbed = Mathf.Clamp(Mathf.Min(shield, damage), 0, int.MaxValue);
            int rest = Mathf.Max(0, damage - absorbed);
            return (absorbed, Mathf.Min(rest, Mathf.Max(0, currentHP)));
        }

        // 명중 처리 공통: HP 차감 → 로그·팝업 → 쓰러짐 → (크리티컬) 상태 효과 → 턴 밀기.
        private void ApplyHit(BattleUnit attacker, BattleUnit target, AttackRequest request, AttackOutcome outcome, int useFocus,
            StatusEffectDefinition[] onCritStatuses, TurnShift onCritShift, SkillDefinition skill)
        {
            // 11/11 방어 보호막이 HP보다 먼저 받음. 11/12 고침: 보호막은 "전체 피해"로 계산 (예전엔 HP를 넘는 부분을 뺀 값이라 HP가 낮으면 틀렸음)
            var (absorbed, hpDamage) = outcome.Evaded ? (0, 0) : ShieldSplit(outcome.Damage, target.Shield, target.CurrentHP);
            if (absorbed > 0)
            {
                target.Shield -= absorbed;
                DevAbsorbed += absorbed;
                ShieldChanged(target);
            }
            if (!outcome.Evaded) target.TakeDamage(hpDamage);
            _lastOutcomeEvaded = outcome.Evaded;
            _lastDamageDealt = hpDamage;
            if (absorbed > 0)
            {
                Log($"  → {target.DisplayName} 보호막이 {absorbed} 막음 (남은 보호막 {target.Shield})");
                _hud.PopText(_camera, ModelTop(target) + Vector3.up * 0.45f, $"보호막 -{absorbed}", new Color(0.95f, 0.96f, 1f));
            }
            LogHit(attacker, request, outcome);
            string result = ResultText(outcome);
            Log($"{attacker.DisplayName}{(skill != null ? $" [{skill.DisplayName}]" : "")} → {target.DisplayName}: 슬롯 {outcome.Slots.SuccessCount}/{outcome.Slots.SlotCount} (슬롯당 {request.SlotChance * 100f:0.0}%{(useFocus > 0 ? $", 집중 {useFocus}" : "")}) · 공격 {request.AttackValue}×{request.Coefficient:0.##}{(request.PierceRatio > 0f ? $" 관통 {request.PierceRatio * 100f:0}%" : "")} vs 방어 {target.Defense().DefenseAgainst(request.Type)} → {result} · {target.DisplayName} HP {target.CurrentHP}/{target.MaxHP}");
            SfxPlayer.Play(outcome.Evaded ? SfxIds.Miss : outcome.IsCritical ? SfxIds.Crit : SfxIds.Hit);
            _hud.PopText(_camera, ModelTop(target), outcome.Evaded ? (outcome.EvadedByState ? "회피 상태" : "MISS") : outcome.Grazed ? $"스침 {Mathf.Max(0, outcome.Damage - absorbed)}" /* 11/15 팀장: 회피 상태 적 = 크리티컬 아니면 절반 */ : Mathf.Max(0, outcome.Damage - absorbed).ToString(), ResultColor(outcome), outcome.IsCritical && !outcome.Evaded ? 38 : 30);
            if (outcome.Evaded) MotionOf(target)?.Dodge(); // 11/12 N7 회피: 몸을 옆으로 비킴 (동작 있는 아군만)
            PlayHitVfx(attacker, target, request, outcome, skill); // 11/13 이펙트 1차 (무기 종류별) · 6차 스킬별
            if (!target.IsAlive) KnockDown(target);
            else if (!outcome.Evaded)
            {
                PlayHitMotion(target);
                // 11/11 받은 추스르기: 아군이 최대 HP 20% 이상 맞으면 맞음 뒤에 몸 추스름 (작은 피해마다 하면 산만)
                if (target.IsAlly && hpDamage >= target.MaxHP * RecoverDamageRatio) StartCoroutine(RecoverAfterHit(target));
            }

            // 공격에 붙은 상태 효과는 크리티컬이 실제 명중한 뒤 적용 (System.md).
            if (!outcome.IsCritical || outcome.Evaded || !target.IsAlive) return;
            int line = ApplyStatuses(target, onCritStatuses, 1);

            // 턴 밀기 (방해함·초기화) — 아직 행동하지 않은 대상만, 같은 대상 라운드당 1회.
            if (onCritShift != TurnShift.None)
            {
                string shiftName = onCritShift == TurnShift.Delay ? "방해함" : "초기화";
                bool moved = _queue.Shift(target, onCritShift);
                if (moved) _hud.PopText(_camera, ModelTop(target) + Vector3.up * (0.45f * line), shiftName, BattleHud.StatusColor);
                Log($"  → {target.DisplayName} [{shiftName}] {(moved ? "— 이번 라운드 차례가 뒤로 밀림" : "— 효과 없음(이미 행동함·이번 라운드 이미 밀림·이미 남은 큐 뒤쪽)")}");
                _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
            }
        }

        // 회복 (응급 봉합): 기본 회복은 확정, 판정 전부 성공 시에만 지정 상태 제거 (System.md).
        public static System.Action DevOnCritHeal; // 자동 플레이: 회복 크리티컬 장면 찍기
        public static bool DevForceHealCrit; // 자동 플레이 -autoplay-force-heal-crit: 회복 판정을 모두 성공으로
        public const float CritHealMultiplier = 1.5f; // 11/15 팀장: 회복 크리티컬(모든 칸 성공) = 회복량 1.5배
        public static int DevCritHeals;
        private IEnumerator ResolveHeal(BattleUnit healer, BattleUnit target, SkillDefinition skill, int useFocus)
        {
            int amount = skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * healer.Member.FinalMagicAttack + 0.0001f);
            int before = target.CurrentHP;
            target.Member.Heal(amount);
            SfxPlayer.Play(SfxIds.Heal);
            int healed = target.CurrentHP - before;
            // 11/13 팀장: 다른 아군 회복이 이상함 — 약을 내민 뒤 정화 판정 창(약 2초)이 끝나야 효과가 나와 동작과 따로 놂
            // → 회복은 이미 확정이므로 내미는 순간 바로 효과·숫자, 그다음 정화 판정
            _hud.PopText(_camera, ModelTop(target), $"+{healed}", BattleHud.HealColor);
            var healKey = SkillVfxKey(skill); BattleVfx.Play(healKey != null && BattleVfx.Has(healKey) ? healKey : BattleVfx.Heal, ModelFeet(target), VfxScale(target)); // 6차: 응급 봉합 = 봉합 원

            if (skill.SlotCount > 0)
            {
                var slots = DevForceHealCrit ? new SlotCheck.Result(skill.SlotCount, skill.SlotCount) : SlotCheck.Roll(skill.SlotCount, healer.Member.FinalStats.Get(skill.RollStat), useFocus);
                // 11/15 팀장: 회복도 판정이 크리티컬(모든 칸 성공)이면 회복량 1.5배 — 기본 회복은 이미 들어갔으니 0.5배를 더함
                int bonus = slots.AllSucceeded ? Mathf.Min(Mathf.FloorToInt(amount * (CritHealMultiplier - 1f) + 0.0001f), Mathf.Max(0, target.Member.MaxHP - target.CurrentHP)) : 0; // 11/15: 최대 HP를 넘는 몫은 표시하지 않음
                string rollText = slots.AllSucceeded
                    ? $"크리티컬! 회복 {healed} + {bonus}{(skill.CleanseNeedsFullSuccess ? " · 정화" : "")}"
                    : $"회복 {healed}";
                yield return _hud.PlayRoll($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}", slots, useFocus,
                    rollText, slots.AllSucceeded ? BattleHud.CritColor : BattleHud.SlotSuccess);
                if (bonus > 0)
                {
                    int beforeBonus = target.CurrentHP;
                    target.Member.Heal(bonus);
                    int bonusHealed = target.CurrentHP - beforeBonus;
                    healed += bonusHealed;
                    DevCritHeals++;
                    if (bonusHealed > 0) _hud.PopText(_camera, ModelTop(target) + Vector3.up * 0.95f /* 11/15: 0.45는 "정화" 글자와 겹침 */, $"크리티컬 +{bonusHealed}", BattleHud.CritColor, 34);
                    DevOnCritHeal?.Invoke();
                    BattleVfx.Play(healKey != null && BattleVfx.Has(healKey) ? healKey : BattleVfx.Heal, ModelFeet(target), VfxScale(target) * 1.2f);
                    SfxPlayer.Play(SfxIds.Heal);
                }
                Log($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}: 회복 {healed}{(bonus > 0 ? $" (크리티컬 +{bonus})" : "")} (고정 {skill.HealFlat} + {skill.HealMagicRatio:0.##}M = {amount}) · 추가 판정 {slots.SuccessCount}/{slots.SlotCount} · HP {target.CurrentHP}/{target.MaxHP}");
                if (slots.AllSucceeded && skill.CleanseNeedsFullSuccess) Cleanse(target, skill.CleansedStatuses, skill.DisplayName);
            }
            else
            {
                Log($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}: 회복 {healed} · HP {target.CurrentHP}/{target.MaxHP}");
                yield return new WaitForSeconds(0.4f);
            }
        }

        private void Cleanse(BattleUnit target, StatusEffectType[] types, string source)
        {
            var removed = types.Where(t => target.Statuses.Has(t)).ToList();
            foreach (var t in removed) target.Statuses.Remove(t);
            Log($"  → {target.DisplayName} 정화({source}): {(removed.Count > 0 ? string.Join(", ", removed) : "제거할 상태 없음")}");
            if (removed.Count > 0) _hud.PopText(_camera, ModelTop(target) + Vector3.up * 0.45f, "정화", BattleHud.SlotSuccess);
        }

        // 전투 중 속도가 바뀌면 안내 — 이번 라운드 순서는 그대로, 다음 라운드 정렬부터 반영 (속도 확정안)
        private void NoticeSpeedChange(BattleUnit unit, int before)
        {
            int now = unit.Speed;
            if (now == before) return;
            string text = $"{unit.DisplayName} 속도 {before} → {now} · 다음 라운드 순서에 반영";
            _hud.ShowNotice(text, 2.2f);
            Log($"  → {text}");
            _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
        }

        // 상태 여러 개 부여 + 팝업·로그. 다음 팝업 줄 번호를 돌려준다.
        private int ApplyStatuses(BattleUnit target, StatusEffectDefinition[] statuses, int line)
        {
            if (target == null || statuses == null) return line;
            foreach (var def in statuses)
            {
                int speedBefore = target.Speed;
                var applied = target.Statuses.Apply(def);
                if (applied != null) NoticeSpeedChange(target, speedBefore);
                if (applied == null) continue;
                string stacks = applied.Stacks > 1 ? $" ×{applied.Stacks}" : "";
                _hud.PopText(_camera, ModelTop(target) + Vector3.up * (0.45f * line++), def.DisplayName + stacks, BattleHud.StatusColor);
                if (StatusAura.IsDebuff(def.Effect)) BattleVfx.Play("Debuff_" + def.Effect, def.Effect is StatusEffectType.DefenseDown or StatusEffectType.WeakBleed ? ModelFront(target) : def.Effect is StatusEffectType.EvasionRevealed ? ModelChest(target) : def.Effect == StatusEffectType.Confusion ? ModelTop(target) /* 11/14 QA: 발밑은 안 보임 → 머리 위 */ : ModelFeet(target), VfxScale(target)); // 11/13 디버프가 걸리는 순간 (조준·장막·출혈은 가슴, 나머지 발밑)
                else if (def.Effect is StatusEffectType.AccuracyUp) BattleVfx.Play("Buff_AccuracyUp", ModelTop(target) + Vector3.down * 0.25f, VfxScale(target)); // 11/13 강화: 영점 조정 = 머리 앞 조준선
                else if (def.Effect is StatusEffectType.SpeedUp) BattleVfx.Play("Buff_SpeedUp", ModelFeet(target), VfxScale(target)); // 신속 = 발밑 빛줄기
                Log($"  → {target.DisplayName}에게 [{def.DisplayName}{stacks}] (남은 {(applied.RemainingTurns < 0 ? "정화 전까지" : applied.RemainingTurns + "턴")})");
            }
            return line;
        }

        // 저격수 "첫 탄의 침묵": 전투 중 첫 활·총 공격(기본 공격 또는 무기 공격 스킬) 피해 +15%.
        private static AttackRequest WithFirstGunShot(BattleUnit attacker, AttackRequest request, bool weaponBased)
        {
            if (!attacker.IsAlly || !weaponBased || attacker.FirstGunShotUsed) return request;
            var weapon = attacker.Member.EquippedWeapon;
            if (weapon == null || !(weapon.Category is WeaponCategory.Bow or WeaponCategory.Pistol or WeaponCategory.Musket)) return request;
            float bonus = attacker.Member.PassiveValue(PassiveType.FirstGunShotBonus);
            if (bonus <= 0f) return request;
            attacker.FirstGunShotUsed = true;
            request.Coefficient *= 1f + bonus;
            Log($"  {attacker.DisplayName} [첫 탄의 침묵] 피해 +{bonus * 100f:0}%");
            return request;
        }

        private static string ResultText(AttackOutcome outcome) => outcome.Evaded
            ? (outcome.EvadedByState ? "회피 상태 — 빗나감" : "회피!")
            : outcome.IsCritical ? $"크리티컬! {outcome.Damage}" : outcome.Grazed ? $"스침 {outcome.Damage} (회피 상태 · 절반)" : $"명중 {outcome.Damage}";

        private static Color ResultColor(AttackOutcome outcome) =>
            outcome.Evaded ? BattleHud.SlotFailure : outcome.IsCritical ? BattleHud.CritDamageColor : BattleHud.DamageColor; // 11/13 피해 주황 · 크리티컬 붉은 주황

        // 쓰러짐 연출. 적은 대상 선택에서 빠지게 콜라이더를 끄고, 아군은 부활 대상으로 클릭할 수 있게 둔다.
        // instant: 전투 시작·이어하기에서 이미 쓰러져 있던 경우 (쓰러지는 움직임 없이 누운 자세로)
        private void KnockDown(BattleUnit unit, bool instant = false)
        {
            if (unit.Boss != null) { unit.Boss.Pending = null; RefreshBossTelegraph(); } // 쓰러진 보스는 예고 해제·실행 안 함
            SfxPlayer.Play(SfxIds.Down);
            if (unit.IsAlly && _playLog != null) _playLog.Downs++;
            if (!_models.TryGetValue(unit, out var model)) return;
            var motion = CharacterMotion.Of(model);
            if (motion != null) motion.Die(instant); // 받은 Death 움직임 (끝에서 누운 채 멈춤)
            else
            {
                if (!_standingRotation.ContainsKey(unit)) _standingRotation[unit] = model.transform.rotation;
                model.transform.rotation = _standingRotation[unit] * Quaternion.Euler(0f, 0f, 90f); // 움직임 없는 모델: 옆으로 눕히기
            }
            if (!unit.IsAlly) foreach (var col in model.GetComponents<Collider>()) col.enabled = false;
        }

        private void StandUp(BattleUnit unit)
        {
            if (!_models.TryGetValue(unit, out var model)) return;
            var motion = CharacterMotion.Of(model);
            if (motion != null) motion.Revive();
            else if (_standingRotation.TryGetValue(unit, out var rotation)) model.transform.rotation = rotation;
        }

        // 공격·스킬 움직임 (11/6 받은 Attack) — 주사위 판정이 끝난 뒤 휘두르고, 팔이 나오는 때에 맞음 처리
        // 11/14 팀장: 근접(아군·적 모두)이 제자리에서 허공을 휘두르는 느낌 → 근접은 대상 앞까지 달려가서 치고 돌아옴. 원거리·광역은 예전처럼 반 걸음 (11/10 기획 4A)
        private IEnumerator PlayAttackMotion(BattleUnit attacker, BattleUnit meleeTarget = null)
        {
            if (attacker == null || !_models.TryGetValue(attacker, out var model)) yield break;
            var motion = CharacterMotion.Of(model);
            if (motion != null && motion.IsDown) yield break;
            if (meleeTarget != null && _models.TryGetValue(meleeTarget, out var targetModel) && targetModel != null)
            {
                var rush = new RushState();
                yield return RushIn(model, targetModel, motion, rush);
                if (motion != null) { motion.Attack(); yield return new WaitForSeconds(CharacterMotion.AttackLead); }
                _rushBack[model] = StartCoroutine(RushOut(model, motion, rush));
                yield break;
            }
            StartCoroutine(HalfStep(model)); // 11/10 기획 4A: 반 걸음 나갔다가 원위치 (맞음 처리와 동시에 진행)
            if (motion == null) yield break;
            motion.Attack();
            yield return new WaitForSeconds(CharacterMotion.AttackLead);
        }

        // 근접 판단: 마법 피해 = 원거리. 아군은 총·활이면 원거리. 적의 물리 공격은 모두 근접 (짐승·사신·아벨 메스)
        private static bool IsMelee(BattleUnit attacker, AttackRequest request)
        {
            if (request.Type == DamageType.Magic) return false;
            var category = attacker.Member != null && attacker.Member.EquippedWeapon != null ? attacker.Member.EquippedWeapon.Category : (WeaponCategory?)null;
            return !(category is WeaponCategory.Pistol or WeaponCategory.Musket or WeaponCategory.Bow);
        }

        // 달려가기: 0.45초에 대상 몸 앞(두 몸 반지름 + 여유)까지. 끝나면 맞힌 뒤 돌아서 걸어 제자리로
        private const float RushTime = 0.45f, RushAfterHit = 0.35f, RushBackTime = 0.5f, RushGap = 0.2f;
        private class RushState { public Vector3 Home, Dest; public Quaternion HomeRot; }
        private readonly Dictionary<GameObject, Coroutine> _rushBack = new();
        public static int DevRushCount; public static float DevLastRushGap = -1f; // 점검용: 달려간 횟수, 마지막으로 멈춘 두 몸 사이 거리

        private IEnumerator RushIn(GameObject model, GameObject target, CharacterMotion motion, RushState rush)
        {
            if (!_homePositions.TryGetValue(model, out var home)) _homePositions[model] = home = model.transform.position;
            if (_rushBack.TryGetValue(model, out var running) && running != null) StopCoroutine(running); // 돌아오는 도중이면 제자리에서 다시 시작
            _rushBack.Remove(model);
            if (!_homeRotations.TryGetValue(model, out var homeRot)) _homeRotations[model] = homeRot = model.transform.rotation;
            model.transform.SetPositionAndRotation(home, homeRot);
            rush.Home = home; rush.HomeRot = homeRot;
            var to = target.transform.position - home; to.y = 0f;
            if (to.sqrMagnitude < 0.01f) { rush.Dest = home; yield break; }
            var dir = to.normalized;
            float stop = BodyRadius(model, dir) + BodyRadius(target, dir) + RushGap;
            rush.Dest = home + dir * Mathf.Max(0f, to.magnitude - stop);
            // 카메라가 아군 뒤에 있어서 적이 아군 바로 앞에 서면 아군 몸에 가려 안 보임 (11/14 QA: 쥐가 저격수 뒤로 숨음) → 화면 오른쪽(적 쪽)으로 비켜 섬
            if (_camera != null && Vector3.Dot(dir, _camera.transform.right) < 0f)
            {
                var side = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;
                rush.Dest += side * (stop * 0.6f);
                var face = target.transform.position - rush.Dest; face.y = 0f;
                if (face.sqrMagnitude > 0.01f) dir = face.normalized;
            }
            DevRushCount++; DevLastRushGap = stop;
            model.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            motion?.SetWalking(true);
            for (float t = 0f; t < RushTime && model != null; t += Time.deltaTime)
            {
                model.transform.position = Vector3.Lerp(home, rush.Dest, Mathf.SmoothStep(0f, 1f, t / RushTime));
                yield return null;
            }
            motion?.SetWalking(false);
            if (model != null) model.transform.position = rush.Dest;
        }

        private IEnumerator RushOut(GameObject model, CharacterMotion motion, RushState rush)
        {
            yield return new WaitForSeconds(RushAfterHit); // 공격 동작이 끝날 때까지 그 자리
            if (model == null) yield break;
            var back = rush.Home - model.transform.position; back.y = 0f;
            bool down = motion != null && motion.IsDown;
            if (back.sqrMagnitude > 0.01f && !down) { model.transform.rotation = Quaternion.LookRotation(back.normalized, Vector3.up); motion?.SetWalking(true); }
            var from = model.transform.position;
            for (float t = 0f; t < RushBackTime && model != null; t += Time.deltaTime)
            {
                model.transform.position = Vector3.Lerp(from, rush.Home, Mathf.SmoothStep(0f, 1f, t / RushBackTime));
                yield return null;
            }
            motion?.SetWalking(false);
            if (model != null && !(motion != null && motion.IsDown)) model.transform.SetPositionAndRotation(rush.Home, rush.HomeRot);
            else if (model != null) model.transform.position = rush.Home;
            _rushBack.Remove(model);
        }

        // 몸 반지름 (다가가는 방향 기준): 몸 메시 상자의 그 방향 폭 절반. 무기·효과는 빼고, 0.25~1.1m
        private readonly Dictionary<GameObject, Quaternion> _homeRotations = new();
        private static float BodyRadius(GameObject model, Vector3 dir)
        {
            bool skinned = model.GetComponentInChildren<SkinnedMeshRenderer>() != null;
            bool any = false; var b = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer) || r.GetComponentInParent<ParticleSystem>() != null) continue;
                if (skinned && !(r is SkinnedMeshRenderer)) continue; // 들고 있는 무기(MeshRenderer)는 빼기
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return 0.4f;
            float along = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
            return Mathf.Clamp(along * 0.8f, 0.25f, 1.1f);
        }

        // 공격 반 걸음: 바라보는 쪽으로 0.15초 나감 → 공격 동작 동안 머묾 → 0.25초 돌아옴. 제자리는 처음 놓인 자리로 고정 (겹쳐 불려도 밀리지 않게).
        private const float HalfStepDistance = 0.6f, StepOut = 0.15f, StepHold = 0.4f, StepBack = 0.25f;
        private readonly Dictionary<GameObject, Vector3> _homePositions = new();
        private IEnumerator HalfStep(GameObject model)
        {
            if (model == null) yield break;
            if (!_homePositions.TryGetValue(model, out var home)) _homePositions[model] = home = model.transform.position;
            var forward = model.transform.forward; forward.y = 0f;
            var target = home + forward.normalized * HalfStepDistance;
            for (float t = 0f; t < StepOut && model != null; t += Time.deltaTime)
            {
                model.transform.position = Vector3.Lerp(home, target, Mathf.SmoothStep(0f, 1f, t / StepOut));
                yield return null;
            }
            if (model == null) yield break;
            model.transform.position = target;
            yield return new WaitForSeconds(StepHold);
            for (float t = 0f; t < StepBack && model != null; t += Time.deltaTime)
            {
                model.transform.position = Vector3.Lerp(target, home, Mathf.SmoothStep(0f, 1f, t / StepBack));
                yield return null;
            }
            if (model != null) model.transform.position = home;
        }

        private const float RecoverDamageRatio = 0.2f;
        private IEnumerator RecoverAfterHit(BattleUnit unit)
        {
            yield return new WaitForSeconds(0.35f); // 맞음(0.4초)이 거의 끝난 뒤
            if (unit.IsAlive) MotionOf(unit)?.Recover();
        }

        // 11/12 N8 승리: 마지막 적이 쓰러지면 서 있는 아군이 승리 동작(1.5초, 끝 자세 유지) → 그다음 결과 창·대화.
        // 저장·보상은 먼저 끝내 둠 (동작 중 게임을 꺼도 보상은 저장됨). 동작이 없으면 바로 다음.
        public const float VictoryStartDelay = 0.4f; // 마지막 공격·쓰러짐 동작이 끝나게 잠깐
        private IEnumerator AfterVictoryMotion(System.Action next)
        {
            yield return new WaitForSeconds(VictoryStartDelay);
            bool any = false;
            foreach (var ally in _allies.Where(a => a.InBattle && a.IsAlive)) any |= MotionOf(ally)?.Victory() == true;
            if (any) yield return new WaitForSeconds(CharacterMotion.VictoryHold);
            next();
        }

        private CharacterMotion MotionOf(BattleUnit unit) => unit != null && _models.TryGetValue(unit, out var model) ? CharacterMotion.Of(model) : null;

        private void PlayHitMotion(BattleUnit target)
        {
            if (target != null && _models.TryGetValue(target, out var model)) CharacterMotion.Of(model)?.Hit();
        }
    }
}
