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
    // 전투 씬 진행 (프로토타입). 보드에서 넘어온 파티·적·기습 결과(BoardSession.PendingBattle)로
    // 전장을 세우고, D09 속도 큐(BattleQueue) 순서대로 아군은 행동 메뉴(BattleHud), 적은 EnemyAI로
    // 행동한다. 공격 판정·피해는 D10 AttackResolver, 상태는 D12 StatusSet, 스킬은 D11 SkillDefinition.
    //
    // 아직 없는 것: 소모품(D17)·전리품 표(D19)·LP 부활/약탈(D21) — 소모품 버튼은 비활성,
    // 패배 시엔 임시로 HP 1로 일으켜 보드로 돌려보낸다.
    public class BattleController : MonoBehaviour
    {
        private const string BoardSceneName = "BoardTest";
        private const float LineX = 2.6f;
        private const float LineSpacing = 2.2f;

        // 프로토타입 전리품 (승리 시 1개) — D19에서 System.md 보상 슬롯 표로 교체.
        public LootEntry[] PossibleLoot = System.Array.Empty<LootEntry>();

        private enum Phase { EnemyActing, ChoosingAction, ChoosingTarget, Resolving, Finished }

        private readonly List<BattleUnit> _allies = new();
        private readonly List<BattleUnit> _enemies = new();
        private readonly Dictionary<BattleUnit, GameObject> _models = new();
        private readonly Dictionary<Collider, BattleUnit> _targets = new();
        private BattleQueue _queue;
        private BattleHud _hud;
        private Camera _camera;
        private Transform _targetMarker;
        private Phase _phase;
        private BattleUnit _current;
        private BattleUnit _hovered;
        private BattleSetup _setup;

        // 지금 고르는 행동 — null이면 기본 공격. 대상 진영은 _targetAllies.
        private SkillDefinition _pendingSkill;
        private bool _targetAllies;

        // 전투 로그 (D29 "재현 가능한 전투 로그") — 콘솔 [Battle] 줄 + 구독자(자동 플레이 점검 등).
        public static event System.Action<string> Logged;

        private static void Log(string message)
        {
            Debug.Log("[Battle] " + message);
            Logged?.Invoke(message);
        }

        private void Start() => Begin();

        // Public so editor tooling can set up the battle for a headless screenshot (Start() doesn't run there).
        public void Begin()
        {
            BoardSession.EnsureExists();
            EnsureEventSystem();
            _camera = Camera.main;

            var session = BoardSession.Instance;
            session.Party ??= DefaultParty();
            _setup = session.PendingBattle ?? DefaultSetup();
            session.PendingBattle = null;

            SpawnUnits(session.Party);

            _hud = BattleHud.Create();
            _hud.AttackClicked += OnAttackClicked;
            _hud.DefendClicked += OnDefendClicked;
            _hud.RetreatClicked += OnRetreatClicked;
            _hud.EnemyBarClicked += OnUnitPicked;
            _hud.SkillChosen += OnSkillChosen;
            _hud.SetEnemies(_enemies);

            PartyHudPanel.Show(session.Party, null);
            _targetMarker = CreateTargetMarker();

            _queue = new BattleQueue(_allies.Concat(_enemies), _setup.Opening);
            Log($"전투 시작 — 개시 {_setup.Opening} · 아군 {string.Join(", ", _allies.Select(u => $"{u.DisplayName}(HP {u.CurrentHP}/{u.MaxHP}, 속도 {u.Speed})"))}"
                + $" · 적 {string.Join(", ", _enemies.Select(u => $"{u.DisplayName}(HP {u.CurrentHP}, 속도 {u.Speed})"))}");
            if (_setup.Opening == EncounterOutcome.AmbushSuccess || _setup.Opening == EncounterOutcome.AmbushFailure)
                _hud.SetHint(EncounterPanel.OutcomeMessage(_setup.Opening));
            NextTurn();
        }

        // ───────────── 턴 진행 ─────────────
        private void NextTurn()
        {
            if (CheckBattleEnd()) return;

            _current = _queue.Advance();
            if (_current == null) { CheckBattleEnd(); return; }
            _current.IsDefending = false; // 방어 효과는 다음 자기 개인 턴 시작 전까지
            _current.TickCooldowns();

            // 개인 턴 시작: 지속 피해 → 사망 확인 → 기간 감소 → 행동 선택 (System.md 상태 효과 처리 순서).
            var start = _current.Statuses.BeginTurn();
            if (start.Damage > 0)
            {
                int dot = StatusSet.ClampDamageOverTime(start.Damage, _current.CurrentHP);
                _current.TakeDamage(dot);
                _hud.PopText(_camera, ModelTop(_current), $"-{dot}", BattleHud.SlotFailure);
                Log($"{_current.DisplayName} 턴 시작 지속 피해 {dot} ({start.Summary}) · HP {_current.CurrentHP}/{_current.MaxHP}");
            }
            if (!_current.InBattle) { NextTurn(); return; }
            Log($"[R{_queue.Round}] {_current.DisplayName} 차례 (속도 {_current.Speed}) — 순서: {string.Join(" > ", _queue.Preview(6).Select(x => x.Unit.DisplayName))}");
            _hud.SetTimeline(_queue.Preview(10), _queue.Round);
            _hud.RefreshEnemies(null);

            if (_current.IsAlly && start.Confused)
            {
                // 혼란: 회복약이 없으면 임의의 적을 기본 공격 (소모품은 D17 — 지금은 항상 공격).
                _phase = Phase.Resolving;
                PartyHudPanel.SetActive(_current.Member, "혼란!");
                _hud.HideActions();
                StartCoroutine(ConfusedTurn(_current));
            }
            else if (_current.IsAlly)
            {
                _phase = Phase.ChoosingAction;
                PartyHudPanel.SetActive(_current.Member, "행동 선택 중");
                _hud.ShowActions(_current);
            }
            else
            {
                _phase = Phase.EnemyActing;
                PartyHudPanel.SetActive(null);
                _hud.HideActions();
                StartCoroutine(EnemyTurn(_current));
            }
        }

        private IEnumerator EnemyTurn(BattleUnit enemy)
        {
            _hud.SetHint($"{enemy.DisplayName}의 차례");
            yield return new WaitForSeconds(0.6f);
            // 1막 소형 적: 기본 공격만, 대상은 데이터의 Targeting 규칙 (EnemyAI).
            var target = EnemyAI.ChooseTarget(enemy, _allies);
            if (target == null) { NextTurn(); yield break; }
            yield return ResolveAttack(enemy, target, enemy.BasicAttack(false), false, enemy.OnCritStatuses, enemy.OnCritTurnShift, null);
            NextTurn();
        }

        private IEnumerator ConfusedTurn(BattleUnit ally)
        {
            _hud.SetHint($"{ally.DisplayName} 혼란 — 행동을 고를 수 없다");
            yield return new WaitForSeconds(0.6f);
            var targets = _enemies.Where(e => e.InBattle).ToList();
            if (targets.Count > 0)
            {
                Log($"{ally.DisplayName} 혼란으로 임의 공격");
                ally.Member.TrySpendMP(ally.BasicAttackMPCost);
                yield return ResolveAttack(ally, targets[Random.Range(0, targets.Count)], ally.BasicAttack(false), false, ally.OnCritStatuses, ally.OnCritTurnShift, null);
            }
            NextTurn();
        }

        // ───────────── 아군 행동 선택 ─────────────
        private void OnAttackClicked()
        {
            if (_phase != Phase.ChoosingAction || !_current.CanBasicAttack) return;
            _pendingSkill = null;
            BeginTargeting(allies: false);
        }

        private void OnSkillChosen(SkillDefinition skill)
        {
            if (_phase != Phase.ChoosingAction || skill == null) return;
            string reason = _current.CannotUseReason(skill);
            if (reason != null) { _hud.SetHint($"{skill.DisplayName}: {reason}"); return; }

            _pendingSkill = skill;
            switch (skill.Target)
            {
                case SkillTarget.SingleEnemy: BeginTargeting(allies: false); break;
                case SkillTarget.SingleAlly: BeginTargeting(allies: true); break;
                case SkillTarget.Self: StartCoroutine(PlayerAct(_current)); break;
                default: StartCoroutine(PlayerAct(null)); break; // 전체 대상
            }
        }

        private void BeginTargeting(bool allies)
        {
            _targetAllies = allies;
            _phase = Phase.ChoosingTarget;
            _hud.SetHint(TargetingHint());
        }

        private string TargetingHint()
        {
            string what = _pendingSkill != null ? _pendingSkill.DisplayName : "공격";
            return _targetAllies ? $"{what} — 대상 아군을 클릭하세요 (우클릭: 취소)" : $"{what} — 대상 적을 클릭하세요 (우클릭: 취소)";
        }

        private void OnUnitPicked(BattleUnit unit)
        {
            if (_phase != Phase.ChoosingTarget || unit == null || !unit.InBattle || unit.IsAlly != _targetAllies) return;
            StartCoroutine(PlayerAct(unit));
        }

        // 비용 지불 → 쿨다운 → 효과. 기본 공격·스킬 공용 (target: 단일 대상, 전체 대상이면 null).
        private IEnumerator PlayerAct(BattleUnit target)
        {
            _phase = Phase.Resolving;
            _hud.HideActions();
            HideMarker();
            var actor = _current;
            var skill = _pendingSkill;
            _pendingSkill = null;

            if (skill == null)
            {
                bool focus = _hud.UseFocus && actor.Member.TrySpendFocus(1);
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
            bool useFocus = skill.RollsSlots && _hud.UseFocus && actor.Member.TrySpendFocus(1);
            Log($"{actor.DisplayName} 스킬 [{skill.DisplayName}] ({BattleHud.SkillCostText(skill)}){(useFocus ? " + 추가 집중" : "")}");

            switch (skill.Effect)
            {
                case SkillEffect.Attack when skill.Target == SkillTarget.AllEnemies:
                    yield return ResolveAreaAttack(actor, skill, useFocus);
                    break;
                case SkillEffect.Attack:
                    yield return ResolveAttack(actor, target, actor.SkillAttack(skill, useFocus), useFocus, skill.OnCritStatuses, skill.OnCritTurnShift, skill);
                    break;
                case SkillEffect.Heal:
                    yield return ResolveHeal(actor, target, skill, useFocus);
                    break;
                case SkillEffect.Cleanse:
                    Cleanse(target, skill.CleansedStatuses, skill.DisplayName);
                    yield return new WaitForSeconds(0.6f);
                    break;
                case SkillEffect.ApplyStatus:
                    ApplyStatuses(target, skill.AppliedStatuses, 0);
                    yield return new WaitForSeconds(0.6f);
                    break;
            }
            _hud.RefreshEnemies(null);
            NextTurn();
        }

        private void OnDefendClicked()
        {
            if (_phase != Phase.ChoosingAction) return;
            _current.IsDefending = true;
            Log($"{_current.DisplayName} 방어 (물리·마법 방어 +{AttackResolver.DefendActionBonus})");
            _hud.PopText(_camera, ModelTop(_current), "방어 +8", BattleHud.SlotSuccess);
            NextTurn();
        }

        // 전투 중 퇴각: 2슬롯 속도 판정, 성공하면 그 캐릭터만 전투 이탈 (System.md 확률표).
        private void OnRetreatClicked()
        {
            if (_phase != Phase.ChoosingAction) return;
            StartCoroutine(PlayerRetreat());
        }

        private IEnumerator PlayerRetreat()
        {
            _phase = Phase.Resolving;
            _hud.HideActions();
            int speed = _current.Member.FinalStats.Speed;
            var slots = SlotCheck.Roll(EncounterRules.RetreatDuringCombatSlotCount, speed);
            bool ok = slots.AllSucceeded;
            yield return _hud.PlayRoll($"{_current.DisplayName} 퇴각 (속도 {speed})", slots, false,
                ok ? "퇴각 성공 — 전투 이탈" : "퇴각 실패 — 행동 소모", ok ? BattleHud.SlotSuccess : BattleHud.SlotFailure);
            Log($"{_current.DisplayName} 퇴각 판정 {slots.SuccessCount}/{slots.SlotCount} → {(ok ? "성공" : "실패")}");
            if (ok)
            {
                _current.HasRetreated = true;
                _models[_current].SetActive(false);
            }
            NextTurn();
        }

        // ───────────── 판정 공통 ─────────────
        // 단일 공격 (기본 공격·공격 스킬·적 공격). skill이 null이면 기본 공격.
        private IEnumerator ResolveAttack(BattleUnit attacker, BattleUnit target, AttackRequest request, bool useFocus,
            StatusEffectDefinition[] onCritStatuses, TurnShift onCritShift, SkillDefinition skill)
        {
            bool weaponBased = skill == null || skill.SlotCount <= 0;
            request = WithFirstGunShot(attacker, request, weaponBased);

            var outcome = AttackResolver.Resolve(request, target.Defense());
            if (attacker.IsAlly && weaponBased) attacker.Statuses.Remove(StatusEffectType.AccuracyUp); // 영점 조정은 다음 무기 공격 1회

            string result = ResultText(outcome);
            Color color = ResultColor(outcome);
            string title = skill != null ? $"{attacker.DisplayName} [{skill.DisplayName}] → {target.DisplayName}" : $"{attacker.DisplayName} → {target.DisplayName}";
            yield return _hud.PlayRoll(title, outcome.Slots, useFocus, result, color);

            ApplyHit(attacker, target, request, outcome, useFocus, onCritStatuses, onCritShift, skill);
            _hud.RefreshEnemies(null);
            yield return new WaitForSeconds(0.25f);
        }

        // 모든 적 공격 (검은 연무): 공통 슬롯 판정 1회, 각 적의 방어·회피는 따로 (System.md 보스 광역 규칙과 같은 방식).
        private IEnumerator ResolveAreaAttack(BattleUnit attacker, SkillDefinition skill, bool useFocus)
        {
            var request = attacker.SkillAttack(skill, useFocus);
            var slots = SlotCheck.RollWithChance(request.SlotCount, request.SlotChance, useFocus ? 1 : 0);
            string summary = slots.AllSucceeded ? "크리티컬! (전체)" : $"{slots.SuccessCount}/{slots.SlotCount} 성공";
            yield return _hud.PlayRoll($"{attacker.DisplayName} [{skill.DisplayName}] → 모든 적", slots, useFocus, summary,
                slots.AllSucceeded ? BattleHud.CritColor : BattleHud.SlotSuccess);

            foreach (var enemy in _enemies.Where(e => e.InBattle).ToList())
            {
                var defense = enemy.Defense();
                var outcome = AttackResolver.Compute(request, defense, slots, Fantasia.Dice.DiceRoller.RollChance(defense.EvasionChance));
                ApplyHit(attacker, enemy, request, outcome, useFocus, skill.OnCritStatuses, skill.OnCritTurnShift, skill);
            }
            _hud.RefreshEnemies(null);
            yield return new WaitForSeconds(0.25f);
        }

        // 명중 처리 공통: HP 차감 → 로그·팝업 → 쓰러짐 → (크리티컬) 상태 효과 → 턴 밀기.
        private void ApplyHit(BattleUnit attacker, BattleUnit target, AttackRequest request, AttackOutcome outcome, bool useFocus,
            StatusEffectDefinition[] onCritStatuses, TurnShift onCritShift, SkillDefinition skill)
        {
            if (!outcome.Evaded) target.TakeDamage(outcome.AppliedDamage);
            string result = ResultText(outcome);
            Log($"{attacker.DisplayName}{(skill != null ? $" [{skill.DisplayName}]" : "")} → {target.DisplayName}: 슬롯 {outcome.Slots.SuccessCount}/{outcome.Slots.SlotCount} (슬롯당 {request.SlotChance * 100f:0.0}%{(useFocus ? ", 집중 1" : "")}) · 공격 {request.AttackValue}×{request.Coefficient:0.##}{(request.PierceRatio > 0f ? $" 관통 {request.PierceRatio * 100f:0}%" : "")} vs 방어 {target.Defense().DefenseAgainst(request.Type)} → {result} · {target.DisplayName} HP {target.CurrentHP}/{target.MaxHP}");
            _hud.PopText(_camera, ModelTop(target), outcome.Evaded ? "MISS" : outcome.Damage.ToString(), ResultColor(outcome));
            if (!target.IsAlive) KnockDown(target);

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
                _hud.SetTimeline(_queue.Preview(10), _queue.Round);
            }
        }

        // 회복 (응급 봉합): 기본 회복은 확정, 판정 전부 성공 시에만 지정 상태 제거 (System.md).
        private IEnumerator ResolveHeal(BattleUnit healer, BattleUnit target, SkillDefinition skill, bool useFocus)
        {
            int amount = skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * healer.Member.FinalMagicAttack + 0.0001f);
            int before = target.CurrentHP;
            target.Member.Heal(amount);
            int healed = target.CurrentHP - before;

            if (skill.CleanseNeedsFullSuccess && skill.SlotCount > 0)
            {
                var slots = SlotCheck.Roll(skill.SlotCount, healer.Member.FinalStats.Get(skill.RollStat), useFocus ? 1 : 0);
                yield return _hud.PlayRoll($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}", slots, useFocus,
                    slots.AllSucceeded ? $"회복 {healed} + 정화" : $"회복 {healed}", BattleHud.SlotSuccess);
                Log($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}: 회복 {healed} (고정 {skill.HealFlat} + {skill.HealMagicRatio:0.##}M = {amount}) · 추가 판정 {slots.SuccessCount}/{slots.SlotCount} · HP {target.CurrentHP}/{target.MaxHP}");
                if (slots.AllSucceeded) Cleanse(target, skill.CleansedStatuses, skill.DisplayName);
            }
            else
            {
                Log($"{healer.DisplayName} [{skill.DisplayName}] → {target.DisplayName}: 회복 {healed} · HP {target.CurrentHP}/{target.MaxHP}");
                yield return new WaitForSeconds(0.4f);
            }
            _hud.PopText(_camera, ModelTop(target), $"+{healed}", BattleHud.SlotSuccess);
        }

        private void Cleanse(BattleUnit target, StatusEffectType[] types, string source)
        {
            var removed = types.Where(t => target.Statuses.Has(t)).ToList();
            foreach (var t in removed) target.Statuses.Remove(t);
            Log($"  → {target.DisplayName} 정화({source}): {(removed.Count > 0 ? string.Join(", ", removed) : "제거할 상태 없음")}");
            if (removed.Count > 0) _hud.PopText(_camera, ModelTop(target) + Vector3.up * 0.45f, "정화", BattleHud.SlotSuccess);
        }

        // 상태 여러 개 부여 + 팝업·로그. 다음 팝업 줄 번호를 돌려준다.
        private int ApplyStatuses(BattleUnit target, StatusEffectDefinition[] statuses, int line)
        {
            if (target == null || statuses == null) return line;
            foreach (var def in statuses)
            {
                var applied = target.Statuses.Apply(def);
                if (applied == null) continue;
                string stacks = applied.Stacks > 1 ? $" ×{applied.Stacks}" : "";
                _hud.PopText(_camera, ModelTop(target) + Vector3.up * (0.45f * line++), def.DisplayName + stacks, BattleHud.StatusColor);
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
            : outcome.IsCritical ? $"크리티컬! {outcome.Damage}" : $"명중 {outcome.Damage}";

        private static Color ResultColor(AttackOutcome outcome) =>
            outcome.Evaded ? BattleHud.SlotFailure : outcome.IsCritical ? BattleHud.CritColor : BattleHud.SlotSuccess;

        private void KnockDown(BattleUnit unit)
        {
            if (!_models.TryGetValue(unit, out var model)) return;
            model.transform.rotation *= Quaternion.Euler(0f, 0f, 90f);
            foreach (var col in model.GetComponents<Collider>()) col.enabled = false;
        }

        private bool CheckBattleEnd()
        {
            if (_phase == Phase.Finished) return true;
            bool enemiesLeft = _enemies.Any(e => e.IsAlive);
            bool alliesLeft = _allies.Any(a => a.InBattle);
            if (enemiesLeft && alliesLeft) return false;

            _phase = Phase.Finished;
            foreach (var ally in _allies) ally.Statuses.ClearOnBattleEnd(); // 중독처럼 남는 것만 유지
            _hud.SetTimeline(_queue.Preview(10), _queue.Round);
            Log($"전투 종료 — {(!enemiesLeft ? "승리" : _allies.Any(a => a.IsAlive) ? "퇴각" : "패배")} · 라운드 {_queue.Round} · 아군 HP {string.Join(", ", _allies.Select(u => $"{u.DisplayName} {u.CurrentHP}/{u.MaxHP}"))}");
            PartyHudPanel.SetActive(null);
            HideMarker();
            var session = BoardSession.Instance;

            if (!enemiesLeft)
            {
                if (session.PendingEncounterCoord.HasValue) session.ClearedEncounters.Add(session.PendingEncounterCoord.Value);
                string loot = GrantLoot(session);
                _hud.ShowResult("승리", $"적을 모두 쓰러뜨렸다.\n{loot}\n(경험치·보상 표는 D19에서)", ReturnToBoard);
            }
            else if (_allies.Any(a => a.IsAlive))
            {
                _hud.ShowResult("퇴각", "파티가 전투에서 이탈했다.\n조우는 그대로 남는다.", ReturnToBoard);
            }
            else
            {
                // D21(LP 부활·안전 타일 HP 50%·약탈) 전까지 임시 처리.
                foreach (var ally in _allies) if (!ally.IsAlive) ally.Member.Heal(1);
                _hud.ShowResult("패배", "파티가 쓰러졌다.\n(임시: HP 1로 보드 복귀 — LP 부활·약탈은 D21)", ReturnToBoard);
            }
            return true;
        }

        private string GrantLoot(BoardSession session)
        {
            var loot = PossibleLoot.Length > 0 ? LootTable.PickWeighted(PossibleLoot) : null;
            if (loot == null) return "";
            return session.AddItem(loot) ? $"전리품: {loot.ItemName}" : "인벤토리가 가득 차 전리품을 놓쳤다.";
        }

        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 버튼·클릭과 같은 처리를 부른다 ─────
        public bool DevAwaitingPlayer => _phase == Phase.ChoosingAction;
        public bool DevFinished => _phase == Phase.Finished;
        public BattleUnit DevCurrent => _current;
        public BattleHud DevHud => _hud;
        public IReadOnlyList<BattleUnit> DevAllies => _allies;
        public IReadOnlyList<BattleUnit> DevEnemies => _enemies;

        public void DevAttack(bool focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnAttackClicked();
            OnUnitPicked(_enemies.FirstOrDefault(e => e.InBattle));
        }

        // 스킬 버튼 → 스킬 선택 → (필요하면) 대상 클릭과 같은 순서.
        public void DevUseSkill(SkillDefinition skill, BattleUnit target, bool focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnSkillChosen(skill);
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }

        public void DevDefend() => OnDefendClicked();
        public void DevContinue() => ReturnToBoard();

        private void ReturnToBoard()
        {
            BoardSession.Instance.PendingEncounterCoord = null;
            SceneManager.LoadScene(BoardSceneName);
        }

        // ───────────── 대상 선택 (전장 클릭) ─────────────
        private void Update()
        {
            if (_phase != Phase.ChoosingTarget) return;

            if (Input.GetMouseButtonDown(1))
            {
                _phase = Phase.ChoosingAction;
                _pendingSkill = null;
                HideMarker();
                _hud.ShowActions(_current);
                return;
            }

            BattleUnit hovered = null;
            if (!BattleHud.PointerOverUI && _camera != null &&
                Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out var hit) &&
                _targets.TryGetValue(hit.collider, out var unit) && unit.InBattle && unit.IsAlly == _targetAllies)
            {
                hovered = unit;
            }

            if (hovered != _hovered)
            {
                _hovered = hovered;
                if (hovered != null) ShowMarker(hovered); else HideMarker();
                _hud.RefreshEnemies(hovered);
                _hud.SetHint(hovered != null ? PreviewText(_current, hovered) : TargetingHint());
            }

            if (hovered != null && Input.GetMouseButtonDown(0)) OnUnitPicked(hovered);
        }

        // 에디터 미리보기용 — 공격 대상 선택 중 첫 번째 적에 마우스를 올린 상태를 재현.
        public void PreviewTargeting()
        {
            if (_phase != Phase.ChoosingAction) return;
            var target = _enemies.FirstOrDefault(e => e.InBattle);
            if (target == null) return;
            _pendingSkill = null;
            _targetAllies = false;
            _phase = Phase.ChoosingTarget;
            _hovered = target;
            ShowMarker(target);
            _hud.RefreshEnemies(target);
            _hud.SetHint(PreviewText(_current, target));
        }

        // 대상에 마우스를 올리면 미리보기 — 공격은 슬롯·크리티컬 확률과 피해, 회복은 회복량.
        private string PreviewText(BattleUnit actor, BattleUnit target)
        {
            var skill = _pendingSkill;
            if (skill != null && skill.Effect == SkillEffect.Heal)
            {
                int amount = skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * actor.Member.FinalMagicAttack + 0.0001f);
                return $"{target.DisplayName} — 회복 {Mathf.Min(amount, target.MaxHP - target.CurrentHP)} (HP {target.CurrentHP}/{target.MaxHP})";
            }
            if (skill != null && skill.Effect != SkillEffect.Attack)
                return $"{target.DisplayName} — {skill.DisplayName}: {skill.EffectDescription}";

            bool focus = _hud.UseFocus && actor.Member.CurrentFocus > (skill != null ? skill.FocusCost : 0);
            var req = skill != null ? actor.SkillAttack(skill, focus) : actor.BasicAttack(focus);
            float crit = SlotCheck.AllSuccessProbability(req.SlotCount, req.SlotChance, focus ? 1 : 0);
            int normal = AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, target.Defense().DefenseAgainst(req.Type), req.PierceRatio);
            int critDmg = AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue);
            string evasion = target.HasEvasionState ? " · 회피 상태: 크리티컬만 명중" : "";
            string name = skill != null ? $"[{skill.DisplayName}] " : "";
            return $"{name}{target.DisplayName} — 슬롯 {req.SlotCount}개 × {req.SlotChance * 100f:0.0}% · 크리티컬 {crit * 100f:0.0}% · 피해 {normal} / 크리 {critDmg}{evasion}";
        }

        // ───────────── 전장 구성 ─────────────
        private void SpawnUnits(Party party)
        {
            var allyPositions = BattleFormation.Line(party.Members.Count, LineSpacing);
            for (int i = 0; i < party.Members.Count; i++)
            {
                var unit = new BattleUnit(party.Members[i], i);
                _allies.Add(unit);
                // 쓰러진 채로 들어온 아군(HP 0)은 행동 큐에서 빠진다 — D21 부활 전까지.
                SpawnModel(unit, new Vector3(-LineX, 0f, allyPositions[i].z), Vector3.right);
                if (!unit.IsAlive) KnockDown(unit);
            }

            var enemyPositions = BattleFormation.Line(_setup.Enemies.Count, LineSpacing);
            for (int i = 0; i < _setup.Enemies.Count; i++)
            {
                var unit = new BattleUnit(_setup.Enemies[i], i);
                if (_setup.Enemies.Count(e => e == _setup.Enemies[i]) > 1) unit.DisplayName += $" {(char)('A' + i)}";
                _enemies.Add(unit);
                SpawnModel(unit, new Vector3(LineX, 0f, enemyPositions[i].z), Vector3.left);
            }
        }

        // 모델은 초상과 같은 CharacterModels (데이터의 Model 프리팹 또는 임시 인형). 클릭용 콜라이더를 붙인다.
        private void SpawnModel(BattleUnit unit, Vector3 position, Vector3 facing)
        {
            var model = CharacterModels.Create(unit.Definition, out float height);
            model.name = unit.DisplayName;
            model.transform.position = position;
            model.transform.rotation = Quaternion.LookRotation(facing);
            var col = model.AddComponent<CapsuleCollider>();
            col.height = height;
            col.radius = Mathf.Max(0.4f, height * 0.3f);
            col.center = new Vector3(0f, height * 0.5f, 0f);
            _models[unit] = model;
            _targets[col] = unit;
        }

        private Vector3 ModelTop(BattleUnit unit)
        {
            if (unit == null || !_models.TryGetValue(unit, out var model)) return Vector3.zero;
            var col = model.GetComponent<CapsuleCollider>();
            return model.transform.position + Vector3.up * (col != null ? col.height + 0.2f : 2f);
        }

        // 대상 발밑 표시 — UITheme.TargetMarkerPrefab이 있으면 그걸, 없으면 붉은 육각형 링.
        private Transform CreateTargetMarker()
        {
            var prefab = UITheme.Current.TargetMarkerPrefab;
            GameObject marker = prefab != null ? Instantiate(prefab) : BuildHexRing();
            marker.SetActive(false);
            return marker.transform;
        }

        private static GameObject BuildHexRing()
        {
            var go = new GameObject("TargetMarker");
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = 6;
            line.widthMultiplier = 0.07f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(1f, 0.3f, 0.25f);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i);
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.9f, 0f, Mathf.Sin(a) * 0.9f));
            }
            go.AddComponent<TargetMarkerSpin>();
            return go;
        }

        private void ShowMarker(BattleUnit unit)
        {
            if (!_models.TryGetValue(unit, out var model)) return;
            _targetMarker.position = model.transform.position + Vector3.up * 0.03f;
            _targetMarker.gameObject.SetActive(true);
        }

        private void HideMarker()
        {
            _hovered = null;
            if (_targetMarker != null) _targetMarker.gameObject.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        // 보드를 거치지 않고 전투 씬을 바로 열었을 때(DevSceneNav 등)의 기본값 — 에디터에서만 시드 자산을 읽는다.
        private static Party DefaultParty()
        {
            var party = new Party();
#if UNITY_EDITOR
            foreach (var name in new[] { "Job_명탐정", "Job_역병의사", "Job_저격수" })
            {
                var job = UnityEditor.AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                if (job != null) party.AddMember(new PartyMember(job));
            }
#endif
            return party;
        }

        private static BattleSetup DefaultSetup()
        {
            var setup = new BattleSetup();
#if UNITY_EDITOR
            foreach (var name in new[] { "Enemy_그림자사냥개", "Enemy_역병쥐" })
            {
                var enemy = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset");
                if (enemy != null) setup.Enemies.Add(enemy);
            }
#endif
            return setup;
        }
    }

    // 기본 대상 링이 천천히 도는 효과.
    public class TargetMarkerSpin : MonoBehaviour
    {
        private void Update() => transform.Rotate(0f, 40f * Time.deltaTime, 0f);
    }
}
