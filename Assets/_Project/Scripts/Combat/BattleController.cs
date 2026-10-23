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
    // 전장을 세우고, D09 속도 큐(BattleQueue) 순서대로 아군은 행동 메뉴(BattleHud), 적은 단순 AI로
    // 행동한다. 공격 판정·피해는 D10 AttackResolver.
    //
    // 아직 없는 것: 직업 스킬(D11)·상태이상(D12)·적 특성(D13)·소모품(D17)·전리품 표(D19)·
    // LP 부활/약탈(D21) — 해당 버튼은 비활성, 패배 시엔 임시로 HP 1로 일으켜 보드로 돌려보낸다.
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
            _hud.EnemyBarClicked += OnEnemyPicked;
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
            // D13 전까지: 살아 있는 아군 중 무작위 1명에게 기본 공격.
            var candidates = _allies.Where(a => a.InBattle).ToList();
            if (candidates.Count == 0) { NextTurn(); yield break; }
            var target = candidates[Random.Range(0, candidates.Count)];
            yield return ResolveAttack(enemy, target, useFocus: false);
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
                yield return ResolveAttack(ally, targets[Random.Range(0, targets.Count)], useFocus: false);
            }
            NextTurn();
        }

        // ───────────── 아군 행동 ─────────────
        private void OnAttackClicked()
        {
            if (_phase != Phase.ChoosingAction || !_current.CanBasicAttack) return;
            _phase = Phase.ChoosingTarget;
            _hud.SetHint("공격할 적을 클릭하세요 (우클릭: 취소)");
        }

        private void OnEnemyPicked(BattleUnit enemy)
        {
            if (_phase != Phase.ChoosingTarget || enemy == null || !enemy.InBattle) return;
            StartCoroutine(PlayerAttack(enemy));
        }

        private IEnumerator PlayerAttack(BattleUnit target)
        {
            _phase = Phase.Resolving;
            _hud.HideActions();
            HideMarker();

            bool useFocus = _hud.UseFocus && _current.Member.TrySpendFocus(1);
            _current.Member.TrySpendMP(_current.BasicAttackMPCost);
            yield return ResolveAttack(_current, target, useFocus);
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

        // ───────────── 공격 판정 공통 ─────────────
        private IEnumerator ResolveAttack(BattleUnit attacker, BattleUnit target, bool useFocus)
        {
            var request = attacker.BasicAttack(useFocus);
            var outcome = AttackResolver.Resolve(request, target.Defense());

            string result = outcome.Evaded
                ? (outcome.EvadedByState ? "회피 상태 — 빗나감" : "회피!")
                : outcome.IsCritical ? $"크리티컬! {outcome.Damage}" : $"명중 {outcome.Damage}";
            Color color = outcome.Evaded ? BattleHud.SlotFailure : outcome.IsCritical ? BattleHud.CritColor : BattleHud.SlotSuccess;

            yield return _hud.PlayRoll($"{attacker.DisplayName} → {target.DisplayName}", outcome.Slots, useFocus, result, color);

            if (!outcome.Evaded) target.TakeDamage(outcome.AppliedDamage);
            Log($"{attacker.DisplayName} → {target.DisplayName}: 슬롯 {outcome.Slots.SuccessCount}/{outcome.Slots.SlotCount} (슬롯당 {request.SlotChance * 100f:0.0}%{(useFocus ? ", 집중 1" : "")}) · 공격 {request.AttackValue}×{request.Coefficient} vs 방어 {target.Defense().DefenseAgainst(request.Type)} → {result} · {target.DisplayName} HP {target.CurrentHP}/{target.MaxHP}");
            _hud.PopText(_camera, ModelTop(target), outcome.Evaded ? "MISS" : outcome.Damage.ToString(), color);
            if (!target.IsAlive) KnockDown(target);

            // 공격에 붙은 상태 효과는 크리티컬이 실제 명중한 뒤 적용 (System.md).
            if (outcome.IsCritical && !outcome.Evaded && target.IsAlive)
            {
                int line = 1;
                foreach (var def in attacker.OnCritStatuses)
                {
                    var applied = target.Statuses.Apply(def);
                    if (applied == null) continue;
                    string stacks = applied.Stacks > 1 ? $" ×{applied.Stacks}" : "";
                    _hud.PopText(_camera, ModelTop(target) + Vector3.up * (0.45f * line++), def.DisplayName + stacks, BattleHud.StatusColor);
                    Log($"  → {target.DisplayName}에게 [{def.DisplayName}{stacks}] (남은 {(applied.RemainingTurns < 0 ? "정화 전까지" : applied.RemainingTurns + "턴")})");
                }
            }
            _hud.RefreshEnemies(null);
            yield return new WaitForSeconds(0.25f);
        }

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
            Log($"전투 종료 — {(!enemiesLeft ? "승리" : _allies.Any(a => a.IsAlive) ? "퇴각" : "패배")} · 아군 HP {string.Join(", ", _allies.Select(u => $"{u.DisplayName} {u.CurrentHP}/{u.MaxHP}"))}");
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

        public void DevAttack(bool focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnAttackClicked();
            OnEnemyPicked(_enemies.FirstOrDefault(e => e.InBattle));
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
                HideMarker();
                _hud.ShowActions(_current);
                return;
            }

            BattleUnit hovered = null;
            if (!BattleHud.PointerOverUI && _camera != null &&
                Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out var hit) &&
                _targets.TryGetValue(hit.collider, out var unit) && unit.InBattle)
            {
                hovered = unit;
            }

            if (hovered != _hovered)
            {
                _hovered = hovered;
                if (hovered != null) ShowMarker(hovered); else HideMarker();
                _hud.RefreshEnemies(hovered);
                _hud.SetHint(hovered != null ? PreviewText(_current, hovered) : "공격할 적을 클릭하세요 (우클릭: 취소)");
            }

            if (hovered != null && Input.GetMouseButtonDown(0)) OnEnemyPicked(hovered);
        }

        // 에디터 미리보기용 — 공격 대상 선택 중 첫 번째 적에 마우스를 올린 상태를 재현.
        public void PreviewTargeting()
        {
            if (_phase != Phase.ChoosingAction) return;
            var target = _enemies.FirstOrDefault(e => e.InBattle);
            if (target == null) return;
            _phase = Phase.ChoosingTarget;
            _hovered = target;
            ShowMarker(target);
            _hud.RefreshEnemies(target);
            _hud.SetHint(PreviewText(_current, target));
        }

        // 대상에 마우스를 올리면 슬롯 확률·크리티컬 확률 미리보기 (System.md: 둘 다 표시).
        private string PreviewText(BattleUnit attacker, BattleUnit target)
        {
            bool focus = _hud.UseFocus && attacker.Member.CurrentFocus > 0;
            var req = attacker.BasicAttack(focus);
            float crit = SlotCheck.AllSuccessProbability(req.SlotCount, req.SlotChance, focus ? 1 : 0);
            int normal = AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, target.Defense().DefenseAgainst(req.Type), req.PierceRatio);
            int critDmg = AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue);
            string evasion = target.HasEvasionState ? " · 회피 상태: 크리티컬만 명중" : "";
            return $"{target.DisplayName} — 슬롯 {req.SlotCount}개 × {req.SlotChance * 100f:0.0}% · 크리티컬 {crit * 100f:0.0}% · 피해 {normal} / 크리 {critDmg}{evasion}";
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
            if (!_models.TryGetValue(unit, out var model)) return Vector3.zero;
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
