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
            // 11/13 6차: 일반 전투 맵 = 싸운 자리 구역 (이어하기 등으로 모르면 적 이름으로 고정 무작위 — 다시 불러와도 같은 맵)
            int theme = session.PendingBattleTheme;
            if (theme < 0) { uint hh = 2166136261; foreach (var e in _setup.Enemies) if (e != null) foreach (char ch in e.Id ?? "") hh = (hh ^ ch) * 16777619; theme = (int)(hh % 3); }
            BattleScenery.Apply(_setup.Enemies.Any(e => ActOneStory.IsActOneBoss(e)), theme); // 11/11 아벨전 조명 · 일반 전투 맵 3종

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
                _hud.PopText(_camera, ModelTop(_current), $"-{dot}", BattleHud.DamageColor);
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
            string reason = _current.CannotUseReason(skill, _hud.FocusCount);
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

    }

    // 기본 대상 링이 천천히 도는 효과.
    public class TargetMarkerSpin : MonoBehaviour
    {
        private void Update() => transform.Rotate(0f, 40f * Time.deltaTime, 0f);
    }
}
