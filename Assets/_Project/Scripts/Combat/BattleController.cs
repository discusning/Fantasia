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
    // 소모품(D17)은 행동 메뉴·퀵 슬롯에서 쓰고(개인 행동 1회), 승리 보상(D19)은 LootRules.
    public partial class BattleController : MonoBehaviour
    {
        private const string BoardSceneName = "BoardTest";
        private const float LineX = 2.6f;
        private const float LineSpacing = 2.2f;

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

        // 지금 고르는 행동 — null이면 기본 공격(또는 _reviving이면 부활). 대상 진영은 _targetAllies.
        private SkillDefinition _pendingSkill;
        private bool _targetAllies;
        private bool _reviving; // D21 전투 중 부활 — 대상은 쓰러진(LP 남은) 아군
        private ConsumableDefinition _pendingItem; // D17 소모품 — 대상 고르는 중
        private readonly Dictionary<BattleUnit, Quaternion> _standingRotation = new();

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
            // 저장된 전투가 있으면 그 진입 정보로 (전투 진입·진행 중 저장에서 이어하기 — Save.md 5)
            var snap = ReadSnapshot(session.BattleSnapshotJson);
            _setup = SetupFromSnapshot(snap, session) ?? session.PendingBattle ?? DefaultSetup();
            session.PendingBattle = null;

            SpawnUnits(session.Party);
            BattleScenery.Apply(_setup.Enemies.Any(e => ActOneStory.IsActOneBoss(e))); // 11/11 아벨전 조명 · 일반 전투 런던 거리

            _hud = BattleHud.Create();
            _hud.AttackClicked += OnAttackClicked;
            _hud.DefendClicked += OnDefendClicked;
            _hud.InterruptClicked += OnInterruptClicked;
            _hud.RetreatClicked += OnRetreatClicked;
            _hud.FocusChanged += () => { if (_phase == Phase.ChoosingTarget && _hovered != null) _hud.SetHint(PreviewText(_current, _hovered)); };
            _hud.ReviveClicked += OnReviveClicked;
            _hud.EnemyBarClicked += OnUnitPicked;
            PartyHudPanel.TargetCardClicked += OnTargetCardClicked; // 11/12 아군 대상은 아래 캐릭터 카드로도 고름
            _hud.SkillChosen += OnSkillChosen;
            _hud.ItemChosen += OnItemChosen;
            _hud.CancelRequested += () => { if (_phase == Phase.ChoosingTarget) CancelTargeting(); };
            _hud.SetEnemies(_enemies);
            CaseLogPanel.EnsureExists(new Vector2(-10f, -6f));

            PartyHudPanel.Show(session.Party, null);
            PartyHudPanel.QuickSlotClicked += OnQuickSlotClicked;
            _targetMarker = CreateTargetMarker();

            // 그림자의 기도: 파티에 받은 캐릭터가 있으면 기습 실패의 적 선공 페널티 없음 → 정상 속도순
            if (_setup.Opening == EncounterOutcome.AmbushFailure && PrayerRules.PartyHas(session.Party, PrayerKind.Shadow))
            {
                _setup.Opening = EncounterOutcome.FightStarted;
                Log("그림자의 기도 — 기습 실패 페널티 면역 (정상 속도순)");
            }
            _queue = new BattleQueue(_allies.Concat(_enemies), _setup.Opening);
            _queue.RoundEnded += () => { foreach (var u in _allies.Concat(_enemies)) u.Statuses.EndRound(); }; // 라운드 단위 상태(속도 증가) 기간
            Log($"전투 시작 — 개시 {_setup.Opening} · 아군 {string.Join(", ", _allies.Select(u => $"{u.DisplayName}(HP {u.CurrentHP}/{u.MaxHP}, 속도 {u.Speed})"))}"
                + $" · 적 {string.Join(", ", _enemies.Select(u => $"{u.DisplayName}(HP {u.CurrentHP}, 속도 {u.Speed})"))}");
            if (_setup.Opening == EncounterOutcome.AmbushSuccess || _setup.Opening == EncounterOutcome.AmbushFailure)
                _hud.SetHint(EncounterPanel.OutcomeMessage(_setup.Opening));
            if (snap != null && snap.started)
            {
                // 진행 중 저장에서 이어하기: 그 개인 턴 시작 상태 → 확정된 행동이 있으면 그대로 다시 실행 (도주 처리 아님)
                ApplySnapshot(snap);
                BeginPlayLog(resumed: true);
                _current = FromRef(snap.current);
                Log($"전투 이어하기 — 라운드 {_queue.Round} · {_current?.DisplayName} 차례{(snap.pending.kind != BattleSnapshot.CommandKind.None ? $" · 확정된 행동 {snap.pending.kind}" : "")}");
                MusicPlayer.Play(_enemies.Any(e => ActOneStory.IsActOneBoss(e.Enemy)) ? MusicIds.BossAbel : MusicIds.Battle, 0f);
                if (_current == null || !_current.InBattle) { NextTurn(); return; }
                BeginCurrentTurn(snap.currentConfused);
                if (snap.pending.kind != BattleSnapshot.CommandKind.None && _current.IsAlly) ReplayCommand(snap.pending);
                return;
            }
            BeginPlayLog(resumed: false);
            StartCoroutine(Opening());
        }

        // ───────────── 전투 시작 연출 (Boss_Abel_v1.0 04·05) ─────────────
        // 최초 진입: 필드 음악 0.4초 페이드아웃 → 보스 0.8초 밝기 강조 + 이름 배너 1.8초 → 전투 전 대화 → 첫 턴.
        // 재도전(같은 플레이에서 이미 봄): 배너 1초 → 전투 (대화 생략). [연출 건너뛰기]는 화면 재생만 생략하고 첫 턴은 한 번만 시작.
        private bool _skipIntro;

        private IEnumerator Opening()
        {
            var boss = _enemies.FirstOrDefault(e => ActOneStory.IsActOneBoss(e.Enemy));
            if (boss == null)
            {
                MusicPlayer.Play(MusicIds.Battle);
                _introDone = true;
                NextTurn();
                yield break;
            }

            var session = BoardSession.Instance;
            bool first = session.StoryFlags.Add(ActOneStory.FlagIntroSeen); // 최초 등장 확인
            _skipIntro = false;
            _hud.HideActions();
            MusicPlayer.Stop(0.4f);
            yield return new WaitForSeconds(0.4f);
            MusicPlayer.Play(MusicIds.BossAbel, 0f); // 필드 음악이 꺼진 뒤 한 번만 시작, 전투 중 반복

            float bannerSeconds = first ? 1.8f : 1f;
            _hud.ShowBossBanner(ActOneStory.BossBannerAct, ActOneStory.BossBannerName, bannerSeconds, () => _skipIntro = true);
            StartCoroutine(Highlight(boss, 0.8f));
            for (float t = 0f; t < bannerSeconds && !_skipIntro; t += Time.deltaTime) yield return null;
            _hud.HideBossBanner();

            if (first && !_skipIntro)
            {
                DialoguePanel.Play(ActOneStory.BeforeBattle, skipCloses: true);
                while (DialoguePanel.IsOpen) yield return null;
            }
            Log($"보스 등장 연출 — {(first ? "최초" : "재도전")}{(_skipIntro ? " (건너뜀)" : "")}");
            _introDone = true;
            NextTurn();
        }

        // 보스 모델을 잠깐 밝게 (등장 강조)
        private IEnumerator Highlight(BattleUnit unit, float seconds)
        {
            if (!_models.TryGetValue(unit, out var model)) yield break;
            var renderers = model.GetComponentsInChildren<Renderer>();
            var originals = renderers.Select(r => r.material.color).ToArray();
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = 1f + 0.8f * Mathf.Sin(Mathf.PI * t / seconds);
                for (int i = 0; i < renderers.Length; i++) renderers[i].material.color = originals[i] * k;
                yield return null;
            }
            for (int i = 0; i < renderers.Length; i++) renderers[i].material.color = originals[i];
        }

        private IEnumerator PlayDialogueThen(DialoguePanel.DialogueLine[] lines, System.Action then)
        {
            DialoguePanel.Play(lines, skipCloses: true);
            while (DialoguePanel.IsOpen) yield return null;
            then();
        }

        // ───────────── 턴 진행 ─────────────
        private void NextTurn()
        {
            if (CheckBattleEnd()) return;

            _current = _queue.Advance();
            if (_current == null) { CheckBattleEnd(); return; }
            _current.IsDefending = false; // 방어 효과는 다음 자기 개인 턴 시작 전까지
            if (_current.Shield > 0) { _current.Shield = 0; ShieldChanged(_current); } // 남은 보호막도 사라짐
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
            BeginCurrentTurn(start.Confused);
        }

        // 개인 턴 시작 처리(지속 피해·쿨다운) 뒤 — 저장하고 행동 선택·적 행동으로. 이어하기도 여기서 시작.
        private void BeginCurrentTurn(bool confused)
        {
            foreach (var u in _allies.Concat(_enemies)) if (u.Shield > 0) ShieldChanged(u); // 이어하기 뒤에도 보호막 표시
            SaveBattle($"전투 R{_queue.Round} {_current.DisplayName} 차례", confused: confused);
            Log($"[R{_queue.Round}] {_current.DisplayName} 차례 (속도 {_current.Speed}) — 순서: {string.Join(" > ", _queue.Preview(6).Select(x => x.Unit.DisplayName))}");
            _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
            _hud.RefreshEnemies(null);

            if (_current.IsAlly && confused)
            {
                // 혼란: 50% 확률로 소지한 일반 HP 회복약 1개를 자신에게, 아니면 임의의 적을 기본 공격 (약이 없으면 공격).
                _phase = Phase.Resolving;
                PartyHudPanel.SetActive(_current.Member, "혼란!");
                _hud.HideActions();
                StartCoroutine(ConfusedTurn(_current));
            }
            else if (_current.IsAlly)
            {
                _phase = Phase.ChoosingAction;
                PartyHudPanel.SetActive(_current.Member, "행동 선택 중");
                ShowAllyActions();
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
            if (enemy.Boss != null)
            {
                yield return BossTurn(enemy);
                NextTurn();
                yield break;
            }
            // 1막 소형 적: 기본 공격만, 대상은 데이터의 Targeting 규칙 (EnemyAI).
            var target = EnemyAI.ChooseTarget(enemy, _allies);
            if (target == null) { NextTurn(); yield break; }
            yield return ResolveAttack(enemy, target, enemy.BasicAttack(0), 0, enemy.OnCritStatuses, enemy.OnCritTurnShift, null);
            NextTurn();
        }

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
                    _hud.PopText(_camera, ModelTop(boss), $"+{healed}", BattleHud.SlotSuccess);
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

        // 「봉합 방해」 전용 행동: 예고 중 아군 개인 턴에만, 행동 1회·집중력 0·MP 0, 판정 없이 취소, 피해 0.
        private BattleUnit InterruptibleBoss => _enemies.FirstOrDefault(e => e.IsAlive && e.Boss != null && BossBrain.CanInterrupt(e.Boss));

        private void OnInterruptClicked()
        {
            if (_phase == Phase.ChoosingTarget) CancelTargeting(quiet: true); // 11/11 고르던 대상 취소 후 새 행동
            if (_phase != Phase.ChoosingAction) return; // 두 번 눌러도 한 번만 (행동하면 페이즈가 바뀜)
            var boss = InterruptibleBoss;
            if (boss == null) return;
            var pending = boss.Boss.Pending;
            SaveBattle("전투 행동 확정 — 봉합 방해", CommandFor(BattleSnapshot.CommandKind.Interrupt));
            if (!BossBrain.TryInterrupt(boss.Boss)) return;
            if (_playLog != null) _playLog.Interrupts++;
            _phase = Phase.Resolving;
            _hud.HideActions();
            Log($"{_current.DisplayName} [{pending.InterruptName}] → {boss.DisplayName}의 [{pending.FollowUp.Name}] 저지 (집중력 0)");
            AnnounceSealStopped(boss, pending);
            StartCoroutine(FinishInterrupt());
        }

        private IEnumerator FinishInterrupt()
        {
            yield return new WaitForSeconds(0.8f);
            NextTurn();
        }

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
                    !string.IsNullOrEmpty(p.InterruptName) ? $"대응: {p.InterruptName}(행동 1회·집중력 0)" : null,
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

        // ───────────── 아군 행동 선택 ─────────────
        private void OnAttackClicked()
        {
            if (_phase == Phase.ChoosingTarget) { bool same = _pendingSkill == null && _pendingItem == null && !_reviving; CancelTargeting(quiet: !same); if (same) return; } // 11/11 다시 누르면 취소, 다른 행동이면 바꿈
            if (_phase != Phase.ChoosingAction || !_current.CanBasicAttack) return;
            _pendingSkill = null;
            _hud.MarkChosenAttack();
            BeginTargeting(allies: false);
        }

        private void OnSkillChosen(SkillDefinition skill)
        {
            if (_phase == Phase.ChoosingTarget && skill != null) { bool same = _pendingSkill == skill; CancelTargeting(quiet: !same); if (same) return; } // 11/11 같은 스킬 다시 누르면 취소
            if (_phase != Phase.ChoosingAction || skill == null) return;
            string reason = _current.CannotUseReason(skill);
            if (reason != null) { _hud.SetHint($"{skill.DisplayName}: {reason}"); return; }

            _hud.MarkChosenSkill(skill); // 고른 칸 빨간 테두리
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

        // ── D17 소모품: 개인 행동 1회. 아군 대상(회복·정화)·적 대상(성수)·즉시(화염병·탈출권) ──
        private PartyInventory Inventory => BoardSession.Instance.Party.Inventory;

        private void OnQuickSlotClicked(PartyMember member, int slot)
        {
            if (_phase != Phase.ChoosingAction || _current == null || _current.Member != member) return;
            OnItemChosen(member.QuickSlots[slot]);
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
                        _hud.PopText(_camera, ModelTop(enemy), dmg.ToString(), BattleHud.SlotSuccess);
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
        private const string CancelHelp = "   (취소: 다시 누르기 · 우클릭 · Esc)";
        private string TargetingHint()
        {
            if (_reviving) return "부활 → 쓰러진 아군 선택 (LP 1, HP 30%)" + CancelHelp;
            string what = _pendingItem != null ? _pendingItem.DisplayName : _pendingSkill != null ? _pendingSkill.DisplayName : "공격";
            return $"{what} → 대상 {(_targetAllies ? "아군" : "적")} 선택" + CancelHelp;
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
            _hud.PopText(_camera, ModelTop(target), $"부활 +{target.CurrentHP}", BattleHud.SlotSuccess);
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
                    if (MotionOf(actor)?.Heal() == true) yield return new WaitForSeconds(CharacterMotion.HealLead); // 11/11 회복 동작 (N1)
                    yield return ResolveHeal(actor, target, skill, useFocus);
                    break;
                case SkillEffect.Cleanse:
                    if (MotionOf(actor)?.Heal() == true) yield return new WaitForSeconds(CharacterMotion.HealLead);
                    Cleanse(target, skill.CleansedStatuses, skill.DisplayName);
                    yield return new WaitForSeconds(0.6f);
                    break;
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
            yield return PlayAttackMotion(attacker);

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
            _hud.PopText(_camera, ModelTop(target), outcome.Evaded ? "MISS" : Mathf.Max(0, outcome.Damage - absorbed).ToString(), ResultColor(outcome));
            if (outcome.Evaded) MotionOf(target)?.Dodge(); // 11/12 N7 회피: 몸을 옆으로 비킴 (동작 있는 아군만)
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
        private IEnumerator ResolveHeal(BattleUnit healer, BattleUnit target, SkillDefinition skill, int useFocus)
        {
            int amount = skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * healer.Member.FinalMagicAttack + 0.0001f);
            int before = target.CurrentHP;
            target.Member.Heal(amount);
            SfxPlayer.Play(SfxIds.Heal);
            int healed = target.CurrentHP - before;

            if (skill.CleanseNeedsFullSuccess && skill.SlotCount > 0)
            {
                var slots = SlotCheck.Roll(skill.SlotCount, healer.Member.FinalStats.Get(skill.RollStat), useFocus);
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
        private IEnumerator PlayAttackMotion(BattleUnit attacker)
        {
            if (attacker == null || !_models.TryGetValue(attacker, out var model)) yield break;
            var motion = CharacterMotion.Of(model);
            if (motion != null && motion.IsDown) yield break;
            StartCoroutine(HalfStep(model)); // 11/10 기획 4A: 근접·원거리 모두 반 걸음 나갔다가 원위치 (맞음 처리와 동시에 진행)
            if (motion == null) yield break;
            motion.Attack();
            yield return new WaitForSeconds(CharacterMotion.AttackLead);
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

        // ── 전투 종료 (D21, System.md "패배 시 약탈") ──
        // 승리: 적 전멸 / 퇴각: 쓰러지지 않은 아군이 모두 이탈 / 패배: 아군 전원 쓰러짐.
        private bool CheckBattleEnd()
        {
            if (_phase == Phase.Finished) return true;
            bool enemiesLeft = _enemies.Any(e => e.IsAlive);
            bool alliesLeft = _allies.Any(a => a.InBattle);
            if (enemiesLeft && alliesLeft) return false;

            _phase = Phase.Finished;
            BoardSession.Instance.BattleSnapshotJson = null; // 전투 끝 — 이어하기는 보드에서
            RefreshBossTelegraph(); // 전투 종료 → 예고 해제
            foreach (var ally in _allies) ally.Statuses.ClearOnBattleEnd(); // 중독처럼 남는 것만 유지
            _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
            bool wiped = enemiesLeft && !_allies.Any(a => a.IsAlive);
            Log($"전투 종료 — {(!enemiesLeft ? "승리" : wiped ? "패배(전원 쓰러짐)" : "퇴각")} · 라운드 {_queue.Round} · 아군 HP {string.Join(", ", _allies.Select(u => $"{u.DisplayName} {u.CurrentHP}/{u.MaxHP} LP {u.Member.CurrentLP}"))}");
            PartyHudPanel.SetActive(null);
            HideMarker();
            var session = BoardSession.Instance;

            if (!enemiesLeft)
            {
                if (session.PendingEncounterCoord.HasValue) session.ClearedEncounters.Add(session.PendingEncounterCoord.Value);
                if (session.PendingReaperFight) session.Hunt.ReaperKilled = true; // D24 피의 사냥 사신 처치 → 보드에서 성공 처리
                // 경험치는 끝까지 서 있던 캐릭터만 → 그다음 쓰러진 아군 정리
                bool actBoss = _enemies.Any(en => ActOneStory.IsActOneBoss(en.Enemy)) && !session.StoryFlags.Contains(ActOneStory.FlagBossDefeated);
                if (actBoss) MusicPlayer.Stop(0.7f); // 처치 직후 보스 음악 0.7초에 걸쳐 종료
                string loot = GrantRewards(session);
                string downed = SettleDowned(DefeatRules.PostBattleReviveHpRatio);
                if (actBoss)
                {
                    // Boss_Abel_v1.0 처치 후 순서: 처치·최초 보상·증거품·퀘스트 갱신(중복 없이) → 처치 후 대화 → 보상 창 → 월드 복귀
                    session.StoryFlags.Add(ActOneStory.FlagRewardGiven);
                    ActOneStory.MarkBossDefeated(session);
                    EndPlayLog("승리 (보스)", LastRewards, 0);
                    SaveGame.Checkpoint("보스 처치·보상·증거품"); // 보상 확정 → 저장 → 대화·보상 창
                    StartCoroutine(AfterVictoryMotion(() => StartCoroutine(PlayDialogueThen(ActOneStory.AfterBattle, () =>
                    {
                        session.StoryFlags.Add(ActOneStory.FlagAfterDialogueDone); // 도중 종료면 이어하기 때 이 대화만 다시 (결정서 T16-35)
                        SaveGame.Checkpoint("처치 후 대화");
                        SfxPlayer.Play(SfxIds.Victory); _hud.ShowPanelResult(true, "아벨 모로 격파", RewardSummary(), $"검시관 아벨 모로를 쓰러뜨렸다.\n{loot}{downed}", ReturnToBoard);
                    }))));
                }
                else
                {
                    EndPlayLog("승리", LastRewards, 0); SaveGame.Checkpoint("전투 승리·보상");
                    StartCoroutine(AfterVictoryMotion(() => { SfxPlayer.Play(SfxIds.Victory); _hud.ShowPanelResult(true, "전투 승리", RewardSummary(), $"적을 모두 쓰러뜨렸다.\n{loot}{downed}", ReturnToBoard); }));
                }
            }
            else if (!wiped)
            {
                // 전원 이탈 → 직전 안전 타일. 단일·전원 이탈은 약탈 없음.
                string downed = SettleDowned(DefeatRules.PostBattleReviveHpRatio);
                session.PlayerPosition = session.LastSafeCoord;
                Log("  → 전원 이탈: 직전 안전 타일로 이동 (약탈 없음)");
                EndPlayLog("퇴각", null, 0);
                SaveGame.Checkpoint("전투 퇴각");
                _hud.ShowResult("퇴각", $"파티가 전투에서 이탈해 직전 안전 타일로 물러났다.\n조우는 그대로 남는다.{downed}", ReturnToBoard);
            }
            else
            {
                // 전원 쓰러짐: 약탈 1회(인원수만큼 반복하지 않음) → LP 남은 캐릭터는 LP 1로 안전 타일에서 HP 50% 복귀,
                // LP 없는 캐릭터는 영구 사망. 아무도 복귀하지 못하면 플레이 종료.
                // 부의 기도: 파티에 받은 캐릭터가 있으면 약탈 면역 (판정 난수는 그대로 소비 — 이어하기 재현 유지)
                int percent = DefeatRules.RollPlunderPercent();
                bool plunderImmune = PrayerRules.PartyHas(session.Party, PrayerKind.Wealth);
                if (plunderImmune) percent = 0;
                int lost = session.Party.LoseCentPercent(percent);
                PlaytestLog.Cent("약탈", -lost, session.Party.Cent, plunderImmune ? "부의 기도 면역" : $"{percent}%");
                Log(plunderImmune ? "  → 부의 기도 — 약탈 면역" : $"  → 약탈 판정: 소지 Cent의 {percent}% → {lost} Cent 손실 (남은 {session.Party.Cent})");
                string downed = SettleDowned(DefeatRules.WipeReturnHpRatio);

                if (session.Party.IsWipedOut)
                {
                    Log("  → 전원 영구 사망 — 플레이 종료");
                    EndPlayLog("전멸 (회차 종료)", null, lost);
                    SaveGame.EndRun(session); // Save.md 1: 회차 종료 기록을 먼저 저장한 뒤 게임 오버 표시
                    SfxPlayer.Play(SfxIds.Defeat); _hud.ShowPanelResult(false, ActOneStory.GameOverTitle, "모든 캐릭터가 영구 사망했다", $"{ActOneStory.GameOverBody}{downed}", NewGameAfterWipe, "새 게임", "메인 메뉴", RestartGame); // 결정서 06
                }
                else
                {
                    session.PlayerPosition = session.LastSafeCoord;
                    EndPlayLog("패배", null, lost);
                    SaveGame.Checkpoint("전투 패배");
                    SfxPlayer.Play(SfxIds.Defeat); _hud.ShowPanelResult(false, "전투 패배", lost > 0 ? $"약탈 {lost} Cent · 안전 타일에서 다시 일어남" : "안전 타일에서 다시 일어남", $"파티가 쓰러졌다. 약탈 {lost} Cent ({percent}%).{downed}\n직전 안전 타일에서 다시 일어났다.", ReturnToBoard);
                }
            }
            return true;
        }

        // 쓰러진 아군 정리: LP가 있으면 1을 써서 hpRatio로 복귀, 없으면 영구 사망. 결과 창에 붙일 문구를 돌려준다.
        private string SettleDowned(float hpRatio)
        {
            var lines = new List<string>();
            foreach (var ally in _allies.Where(a => a.Member.IsDowned))
            {
                if (ally.Member.TrySpendLP())
                {
                    ally.Member.Revive(hpRatio);
                    lines.Add($"{ally.DisplayName}: LP 1 소모 → HP {ally.CurrentHP} (남은 LP {ally.Member.CurrentLP})");
                }
                else
                {
                    ally.Member.MarkPermanentlyDead();
                    lines.Add($"{ally.DisplayName}: LP 없음 → 영구 사망");
                }
                Log("  → " + lines[^1]);
            }
            return lines.Count > 0 ? "\n" + string.Join("\n", lines) : "";
        }

        // 게임 오버 [새 게임]: 파티 구성 화면부터
        private void NewGameAfterWipe()
        {
            Fantasia.UI.MainMenuController.OpenPartySelectOnStart = true;
            RestartGame();
        }

        private void RestartGame()
        {
            BoardSession.Instance.ResetForNewGame();
            // 게임 오버 [메인 메뉴] (메인 메뉴 씬이 빌드에 없으면 보드로)
            SceneManager.LoadScene(Application.CanStreamedLevelBeLoaded(Fantasia.UI.MainMenuController.SceneName) ? Fantasia.UI.MainMenuController.SceneName : BoardSceneName);
        }

        // D19 승리 보상: Cent(첫 칸) + 추가 칸 아이템, 생존 캐릭터별 경험치. 인벤토리가 차서 못 받은 것은
        // BoardSession.PendingLoot에 남겨 인벤토리 창에서 버리기·교체 후 받게 한다 (자동 삭제 안 함).
        private string GrantRewards(BoardSession session)
        {
            var party = session.Party;
            var levels = party.Members.ToDictionary(m => m, m => m.Level);
            var rewards = LootRules.Roll(_setup.Enemies, party, ItemCatalog.Current, GameRandom.NewSystemRandom());
            // 지도 적 칸 확정 보상 — 몬스터별 합산 대신 전투 전체 값 (아이템 칸은 기존 표 그대로)
            if (_setup.FixedCent > 0) rewards.Cent = _setup.FixedCent;
            if (_setup.FixedExp > 0) rewards.Experience = _setup.FixedExp;
            // 부의 기도: 전투 Cent +20%, 여러 명이 받아도 파티당 1회 (버림)
            if (PrayerRules.PartyHas(party, PrayerKind.Wealth)) rewards.Cent += Mathf.FloorToInt(rewards.Cent * PrayerRules.WealthCentBonus);
            if (DevShowcaseLoot) ShowcaseLoot(rewards);
            LootRules.Apply(rewards, party);
            session.PendingLoot.AddRange(rewards.Overflow);
            LastRewards = rewards;
            PlaytestLog.Cent("전투 보상", rewards.Cent, party.Cent, string.Join(" + ", _setup.Enemies.Select(e => e.DisplayName)));

            var lines = new List<string> { $"보상 {rewards.SlotCount}칸 — {rewards.Cent} Cent (소지 {party.Cent})" };
            var shown = new List<string>(lines); // 결과 창 글 — 아이템은 아래 그림 띠로 (11/12)
            var cells = new List<(Sprite, string, Color)>();
            foreach (var item in rewards.Items)
            {
                bool lost = rewards.Overflow.Contains(item);
                lines.Add($"· {item.Name}{(lost ? " (인벤토리 가득 — 보관함에 대기)" : "")}");
                cells.Add((ItemLook.IconOf(item), ItemLook.NameWithGrade(item) + (lost ? "\n(보관함 대기)" : ""), ItemLook.NameColor(item)));
            }
            _hud.SetResultItems(cells);
            foreach (var key in _setup.Enemies.Where(en => en.KeyItemRewards != null).SelectMany(en => en.KeyItemRewards))
            {
                if (session.KeyItems.Contains(key)) continue;
                session.KeyItems.Add(key);
                lines.Add($"★ 퀘스트 물건: {key}");
                shown.Add($"★ 퀘스트 물건: {key}");
            }
            var survivors = party.AliveMembers.ToList();
            lines.Add($"경험치 +{rewards.Experience} ({string.Join(", ", survivors.Select(m => m.Job.DisplayName))})");
            shown.Add(lines[^1]);
            foreach (var m in survivors.Where(m => m.Level > levels[m]))
            {
                lines.Add($"{m.Job.DisplayName} 레벨 업! Lv {m.Level}");
                shown.Add(lines[^1]);
            }
            Log($"  → 보상: {string.Join(" / ", lines)}");
            if (rewards.Items.Count > 0) ItemAcquiredToast.Show($"전리품 {rewards.Items.Count}개 획득"); // 11/12 팀장: 하나씩 알림 X → 결과 창에서 그림·등급 색으로 한꺼번에
            return string.Join("\n", shown);
        }

        public BattleRewards LastRewards { get; private set; }

        // 점검용 (자동 플레이): 다음 승리 보상 아이템을 전설 무기 · 에픽 방어구 · 소모품으로 (결과 창 그림 띠·등급 색 사진)
        public static bool DevShowcaseLoot;
        private static void ShowcaseLoot(BattleRewards rewards)
        {
            DevShowcaseLoot = false;
            var cat = ItemCatalog.Current;
            if (cat == null) return;
            var w = cat.Weapons.FirstOrDefault(x => x != null && x.Icon == null);
            var a = cat.Armors.FirstOrDefault(x => x != null);
            var c = cat.Consumables.FirstOrDefault(x => x != null && x.Icon != null);
            rewards.Items.Clear();
            if (w != null) rewards.Items.Add(new LootDrop(new GearItem(w, EquipmentGrade.Legendary)));
            if (a != null) rewards.Items.Add(new LootDrop(new GearItem(a, EquipmentGrade.Epic)));
            if (c != null) rewards.Items.Add(new LootDrop(c));
        }

        // 결과 판 한 줄 요약: "180 Cent · 경험치 100 · 전리품 획득" (보고서 11/3 B6)
        private string RewardSummary()
        {
            var r = LastRewards;
            if (r == null) return "";
            var parts = new List<string> { $"{r.Cent} Cent", $"경험치 {r.Experience}" };
            if (r.Items.Count > 0) parts.Add("전리품 획득");
            return string.Join(" · ", parts);
        }

        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 버튼·클릭과 같은 처리를 부른다 ─────
        public bool DevAwaitingPlayer => _phase == Phase.ChoosingAction;
        public bool DevFinished => _phase == Phase.Finished;
        public void DevCheckEnd() => CheckBattleEnd(); // 자동 플레이: 강제로 바꾼 상태(전멸 등)를 바로 판정
        public BattleUnit DevCurrent => _current;
        public BattleHud DevHud => _hud;
        public IReadOnlyList<BattleUnit> DevAllies => _allies;
        public IReadOnlyList<BattleUnit> DevEnemies => _enemies;
        public int DevRound => _queue != null ? _queue.Round : 0;

        public void DevAttack(int focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnAttackClicked();
            OnUnitPicked(_enemies.FirstOrDefault(e => e.InBattle));
        }

        // 스킬 버튼 → 스킬 선택 → (필요하면) 대상 클릭과 같은 순서.
        public void DevUseSkill(SkillDefinition skill, BattleUnit target, int focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnSkillChosen(skill);
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }

        // 촬영용 (-autoplay-motions, 11/11): 아군 1명을 쓰러뜨림 / 적 HP를 1로 (다음 공격에 처치 → 승리 순간 촬영)
        public void DevKnockDownAlly(BattleUnit unit)
        {
            if (unit == null || !unit.IsAlly || !unit.IsAlive) return;
            unit.TakeDamage(unit.CurrentHP);
            KnockDown(unit);
            _hud.RefreshEnemies(null);
        }
        public void DevSetEnemyHP1(BattleUnit unit) { if (unit != null && !unit.IsAlly && unit.IsAlive) unit.TakeDamage(unit.CurrentHP - 1); }
        public GameObject DevModelOf(BattleUnit unit) => unit != null && _models.TryGetValue(unit, out var m) ? m : null;

        public void DevDefend() => OnDefendClicked();
        public void DevPressItem(ConsumableDefinition item) => OnItemChosen(item); // 11/12 아이템만 고르고 대상은 아직 (카드 클릭 점검)
        public void DevPressSkill(SkillDefinition skill) => OnSkillChosen(skill); // 11/11 스킬 칸 누르기 (대상 고르기 시작·같은 칸 다시 누르면 취소)
        public void DevDefendButton() => OnDefendClicked();
        public bool DevChoosingTarget => _phase == Phase.ChoosingTarget;
        public int DevShieldTotal => _allies.Sum(a => a.Shield);
        public int DevAbsorbed { get; private set; } // 보호막이 막은 피해 합 (자동 점검용)
        public int DevLastHpDamage => _lastDamageDealt;
        // 점검: 보호막 있는 캐릭터의 방패 표시가 "지금 캐릭터 머리 위"에서 얼마나 떨어져 있나 (m)
        public float DevShieldMarkError => _allies.Concat(_enemies).Where(u => u.Shield > 0 && _models.ContainsKey(u)).Select(u => _hud.DevShieldMarkWorld(u) is Vector3 w ? Vector3.Distance(w, ModelTop(u) - Vector3.up * 0.15f) : 99f).DefaultIfEmpty(0f).Max();
        public void DevShieldAll(int amount) { foreach (var a in _allies) if (a.IsAlive && !a.HasRetreated) { a.Shield = amount; ShieldChanged(a); } }
        public void DevRetreat() => OnRetreatClicked();

        // 소모품 메뉴 → 아이템 → (대상 클릭)과 같은 순서.
        public void DevUseItem(ConsumableDefinition item, BattleUnit target)
        {
            if (_phase != Phase.ChoosingAction) return;
            OnItemChosen(item);
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }

        // 부활 버튼 → 쓰러진 아군 클릭과 같은 순서.
        public bool DevCanRevive => CanRevive();
        public void DevRevive(BattleUnit target)
        {
            if (_phase != Phase.ChoosingAction) return;
            OnReviveClicked();
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }
        public void DevContinue() => _hud.DevPressResult(); // 결과 창 버튼 (보드로 / 처음부터 다시)

        private void ReturnToBoard()
        {
            BoardSession.Instance.PendingEncounterCoord = null;
            SceneManager.LoadScene(BoardSceneName);
        }

        private void OnDestroy()
        {
            PartyHudPanel.QuickSlotClicked -= OnQuickSlotClicked;
            PartyHudPanel.TargetCardClicked -= OnTargetCardClicked;
            PartyHudPanel.ClearShields(); // 전투가 끝나면 보호막 표시도 끝
            ConsumesEscape = false;
        }

        // ───────────── 11/12 아군 대상: 아래 캐릭터 카드로 고르기 ─────────────
        // 가운데 행동 창이 전장의 아군 모델을 가려서, 아군 대상(회복·강화 스킬, 아이템, 부활)을 고르는 동안 카드에 "대상" 덮개를 띄움.
        private object _cardPickKey;
        private void SyncTargetCards()
        {
            bool want = _phase == Phase.ChoosingTarget && _targetAllies;
            object key = !want ? null : _reviving ? "부활" : _pendingItem != null ? _pendingItem : _pendingSkill != null ? _pendingSkill : (object)"아군";
            if (Equals(key, _cardPickKey)) return;
            _cardPickKey = key;
            PartyHudPanel.SetTargetPick(want ? m => IsValidTarget(_allies.FirstOrDefault(a => a.Member == m)) : null);
        }

        private void OnTargetCardClicked(PartyMember member)
        {
            if (_phase != Phase.ChoosingTarget || !_targetAllies) return;
            OnUnitPicked(_allies.FirstOrDefault(a => a.Member == member));
        }

        // ───────────── 대상 선택 (전장 클릭) ─────────────
        private void Update()
        {
            SyncTargetCards();
            if (PauseMenu.IsOpen) return; // 게임 중 메뉴 — 진행 일시정지
            // 사건 기록 (Q) — 전투 중엔 열람만
            if (Input.GetKeyDown(KeyCode.Q) && !DialoguePanel.IsOpen) CaseLogPanel.Toggle();
            if (CaseLogPanel.IsOpen) return;
            ConsumesEscape = _phase == Phase.ChoosingTarget || _escConsumedFrame == Time.frameCount;
            if (_phase != Phase.ChoosingTarget) return;

            // 11/11 취소: 우클릭 또는 Esc. 예전엔 우클릭만, 적 카드 강조가 남아 취소됐는지 헷갈렸음
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (Input.GetMouseButtonDown(1) || esc)
            {
                if (esc) _escConsumedFrame = Time.frameCount;
                CancelTargeting();
                ConsumesEscape = true;
                return;
            }

            BattleUnit hovered = null;
            var cardHover = PartyHudPanel.HoveredTargetCard != null ? _allies.FirstOrDefault(a => a.Member == PartyHudPanel.HoveredTargetCard) : null;
            if (cardHover != null && IsValidTarget(cardHover)) hovered = cardHover; // 11/12 아래 카드에 마우스 → 전장 표시·미리보기도 같이
            else if (!BattleHud.PointerOverUI && _camera != null &&
                Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out var hit) &&
                _targets.TryGetValue(hit.collider, out var unit) && IsValidTarget(unit))
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

            int focus = FocusToUse(actor, skill, skill != null ? skill.FocusCost : 0);
            var req = skill != null ? actor.SkillAttack(skill, focus) : actor.BasicAttack(focus);
            float crit = SlotCheck.AllSuccessProbability(req.SlotCount, req.SlotChance, focus);
            int normal = AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, target.Defense().DefenseAgainst(req.Type), req.PierceRatio);
            int critDmg = AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue);
            string evasion = target.HasEvasionState ? " · 회피 상태: 크리티컬만 명중" : "";
            string name = skill != null ? $"[{skill.DisplayName}] " : "";
            string focusText = focus > 0 ? $" (집중 {focus}개 확정)" : "";
            return $"{name}{target.DisplayName} — 슬롯 {req.SlotCount}개 × {req.SlotChance * 100f:0.0}%{focusText} · 크리티컬 {crit * 100f:0.0}% · 피해 {normal} / 크리 {critDmg}{evasion}";
        }

        // ───────────── 전장 구성 ─────────────
        private void SpawnUnits(Party party)
        {
            // 영구 사망한 파티원은 전투에 나오지 않는다.
            var living = party.LivingMembers.ToList();
            var allyPositions = BattleFormation.Line(living.Count, LineSpacing);
            for (int i = 0; i < living.Count; i++)
            {
                var unit = new BattleUnit(living[i], i);
                _allies.Add(unit);
                // 쓰러진 채로 들어온 아군(HP 0)은 행동 큐에서 빠지고, 다른 아군이 부활시킬 수 있다.
                SpawnModel(unit, new Vector3(-LineX, 0f, allyPositions[i].z), Vector3.right);
                if (!unit.IsAlive) KnockDown(unit, instant: true);
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
            BlobShadow.Attach(model, height); // 발밑 흐린 그림자 (11/10 기획 1A)
            HeldWeapon.Show(model, unit.Member); // 11/13 아군: 장착 무기 종류에 맞는 소품만 손에
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
            party.GiveStartingItems(ItemCatalog.Current);
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
