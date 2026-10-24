using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // 보드 테스트 진행. 월드 턴 = [아군 턴] → [적 턴] (2026-10-24 기획 결정, Docs/Planning/Decisions.md):
    // 아군 턴에 파티장(1번) 1명이 주사위 1회 → 이동 → 칸 처리(인카운터·이벤트)를 하면 턴이 끝나고,
    // 적 턴에 월드 처리(피의 흔적·영혼 수확 등 — 해당 업무에서 연결)를 한 뒤 다음 아군 턴. 아트 없이
    // 토큰(빨간 공)과 OnGUI 안내만으로 동작한다.
    [RequireComponent(typeof(HexBoard))]
    public class BoardTestController : MonoBehaviour
    {
        private const string CombatSceneName = "CombatTest";
        private const float EnemyTurnSeconds = 0.9f;

        [SerializeField] private float tokenRadius = 0.5f; // matches the default primitive Sphere's radius
        [SerializeField] private float secondsPerTile = 0.25f;

        // Assigned by BoardTestSceneSetup from PlaceholderDataSetup — real
        // landmark placement (frequency, rules) is a design decision
        // (GDD TBD); this only proves Ctrl+Right-click info popups work for
        // several distinct landmark types.
        public LandmarkDefinition[] PlaceholderLandmarks = System.Array.Empty<LandmarkDefinition>();

        // 테스트 파티 (명탐정·역병 의사·저격수 — 1번이 파티장). Assigned by BoardTestSceneSetup.
        public JobDefinition TestHeroJob;
        public JobDefinition[] TestPartyJobs = System.Array.Empty<JobDefinition>();
        private Party _testParty;

        // Fixed offsets so every placeholder type is deterministically
        // reachable for testing, regardless of the board's random
        // obstacle/encounter rolls.
        private static readonly HexCoord[] LandmarkOffsets = { new HexCoord(2, -1), new HexCoord(-2, 1), new HexCoord(0, 2) };

        private HexBoard _board;
        private Transform _token;
        private HexCoord _currentCoord;
        private HexBoard.ReachabilityMap _reachability;
        private int _lastRoll;
        private bool _awaitingSelection;
        private bool _isMoving;
        private bool _turnEnding; // 적 턴 연출 중
        private int _lastEventBonus = -1;
        private string _worldMessage;

        // D08 조우 — 인카운터 칸에 도착하면 EncounterPanel에서 싸우기/기습/몰래 지나가기/후퇴.
        // 후퇴는 직전에 밟았던 칸으로 돌아간다.
        private HexCoord _lastSafeCoord;
        private EnemyDefinition _pendingEnemy;
        private bool _pendingIsReaper; // D24 피의 사냥 사신과의 조우
        private string _encounterResultMessage;

        // D24 인조 그림자 사신 (피의 사냥). Assigned by BoardTestSceneSetup.
        public EnemyDefinition ReaperEnemy;
        private readonly List<GameObject> _reaperFigures = new();
        private BloodHuntState Hunt => BoardSession.Instance.Hunt;

        private WorldClock Clock => BoardSession.Instance.World;
        private PartyMember Leader => _testParty?.Leader;

        private void Start()
        {
            BoardSession.EnsureExists();
            ItemAcquiredToast.EnsureExists();
            QuestTrackerPanel.EnsureExists();
            LandmarkInfoPanel.EnsureExists();
            DialoguePanel.EnsureExists();
            EncounterPanel.EnsureExists();
            _board = GetComponent<HexBoard>();
            PlaceAtStartIfNew();
            _currentCoord = BoardSession.Instance.PlayerPosition;
            _lastSafeCoord = _currentCoord;
            BuildTestParty();
            SpawnToken();
            if (Camera.main != null && _board.UsesWorldMap) BoardCameraRig.Attach(Camera.main, _token);
            PlaceLandmarks();
            HookInventory();
            if (_board.UsesWorldMap) gameObject.AddComponent<BoardSkyTint>();
            ResolveReaperFightResult();
            RefreshReaperFigures();

            // 전투로 끝난 아군 턴이면 돌아오자마자 적 턴 진행, 아니면 지금 페이즈 표시.
            if (BoardSession.Instance.AllyTurnEndedByBattle)
            {
                BoardSession.Instance.AllyTurnEndedByBattle = false;
                StartCoroutine(EndAllyTurn());
            }
            else
            {
                RefreshTopBar(announce: true);
            }
        }

        // Public so editor tooling can build the HUD for a headless screenshot (Start() doesn't run there).
        // 파티는 BoardSession에 두어 전투를 다녀와도 HP·집중력이 유지된다.
        public void BuildTestParty()
        {
            BoardSession.EnsureExists();
            if (BoardSession.Instance.Party != null && BoardSession.Instance.Party.Members.Count > 0)
            {
                _testParty = BoardSession.Instance.Party;
                PartyHudPanel.Show(_testParty, Leader);
                return;
            }

            _testParty = new Party();
            BoardSession.Instance.Party = _testParty;
            var jobs = TestPartyJobs.Length > 0 ? TestPartyJobs : LoadDefaultTestPartyJobs();
            foreach (var job in jobs)
            {
                if (job != null && _testParty.Members.Count < Party.MaxSize) _testParty.AddMember(new PartyMember(job));
            }
            if (_testParty.Members.Count == 0) return;
            _testParty.GiveStartingItems(ItemCatalog.Current);
            PartyHudPanel.Show(_testParty, Leader);
        }

        // 씬이 Setup Board Test Scene 재실행 전 버전이라 TestPartyJobs가 비어 있어도 3인이 나오게 —
        // 에디터에서만 시드된 직업 자산을 직접 읽는다 (빌드에서는 명탐정 1명으로 폴백).
        private JobDefinition[] LoadDefaultTestPartyJobs()
        {
#if UNITY_EDITOR
            var loaded = new List<JobDefinition>();
            foreach (var name in new[] { "Job_명탐정", "Job_역병의사", "Job_저격수" })
            {
                var job = UnityEditor.AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                if (job != null) loaded.Add(job);
            }
            if (loaded.Count > 0) return loaded.ToArray();
#endif
            return new[] { TestHeroJob };
        }

        // 월드맵 처음 시작: 런던 거점 S05 (WorldMap.md)
        private void PlaceAtStartIfNew()
        {
            BoardSession.EnsureExists();
            var session = BoardSession.Instance;
            if (!_board.UsesWorldMap || session.HasPlayerPosition) return;
            session.PlayerPosition = _board.Map.TileAt(StartHubLabel).Coord;
            session.LastSafeCoord = session.PlayerPosition;
            session.HasPlayerPosition = true;
        }

        private const string StartHubLabel = "S05";

        private void PlaceLandmarks()
        {
            if (_board.UsesWorldMap) return; // 확정 맵에는 임시 지형지물을 놓지 않음
            for (int i = 0; i < PlaceholderLandmarks.Length && i < LandmarkOffsets.Length; i++)
            {
                if (!_board.TryGetTile(LandmarkOffsets[i], out var tile)) continue;

                // A landmark is a real point of interest — clear any
                // obstacle/encounter roll on its tile so it's always visible
                // and walkable for testing.
                tile.SetBlocked(false);
                tile.SetEncounter(false);
                tile.SetLandmark(PlaceholderLandmarks[i]);
            }
        }

        // Public so editor tooling can spawn it for a headless screenshot
        // check without entering Play mode (Start() doesn't run there).
        public void SpawnToken()
        {
            _board ??= GetComponent<HexBoard>();
            if (_board.UsesWorldMap)
            {
                PlaceAtStartIfNew();
                _currentCoord = BoardSession.Instance.PlayerPosition;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Token";
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };

            _token = go.transform;
            _token.position = TokenSurfacePosition(_currentCoord);
        }

        // Tiles are positioned base-first (see HexBoard.TileHeight), so a
        // token resting on top needs the tile's height plus its own radius —
        // not a flat guessed offset that ends up burying it in the mesh.
        private Vector3 TokenSurfacePosition(HexCoord coord)
        {
            return _board.CoordToWorld(coord) + Vector3.up * (_board.TileHeight + tokenRadius);
        }

        private static int ZoneWearLevel(int region) => region switch { 1 => 4, 2 => 7, _ => 1 };

        // 보드 상단 바 갱신 — 턴 전환 때는 announce로 가운데 전환 배너.
        private void RefreshTopBar(bool announce)
        {
            var session = BoardSession.Instance;
            if (_board != null && _board.UsesWorldMap && _board.Map.RegionName(_currentCoord) is string regionName) session.RegionName = regionName;
            BoardTopBar.EnsureExists().Refresh(Clock.Phase, Clock.WorldRound, Leader, session.BloodTrail, session.SoulHarvest,
                _testParty != null ? _testParty.Cent : 0, session.RegionName, announce);
        }

        private void LateUpdate()
        {
            if (BoardTopBar.Instance != null) RefreshTopBar(announce: false);
        }

        // ── D17 인벤토리: 초상 클릭·I 키로 창, 퀵 슬롯 클릭으로 월드 사용 ──
        private bool WorldItemUseAllowed => !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && Clock.Phase == WorldPhase.AllyTurn;

        private void HookInventory()
        {
            InventoryPanel.WorldUseAllowed = () => WorldItemUseAllowed;
            InventoryPanel.AllyTurnEndRequested += OnItemEndedAllyTurn;
            Fantasia.UI.ShopPanel.AllyTurnEndRequested += OnItemEndedAllyTurn;
            PartyHudPanel.CardClicked += OnCardClicked;
            PartyHudPanel.QuickSlotClicked += OnBoardQuickSlot;
        }

        private void OnDestroy()
        {
            InventoryPanel.AllyTurnEndRequested -= OnItemEndedAllyTurn;
            Fantasia.UI.ShopPanel.AllyTurnEndRequested -= OnItemEndedAllyTurn;
            PartyHudPanel.CardClicked -= OnCardClicked;
            PartyHudPanel.QuickSlotClicked -= OnBoardQuickSlot;
        }

        private void OnCardClicked(PartyMember member)
        {
            if (!EncounterPanel.IsOpen) InventoryPanel.Open(_testParty, member);
        }

        private void OnBoardQuickSlot(PartyMember member, int slot)
        {
            var def = member.QuickSlots[slot];
            if (def == null) return;
            string message = WorldItemUseAllowed ? InventoryPanel.UseWorldItem(def, member) : "보드의 아군 턴(이동 중 아님)에만 쓸 수 있음";
            ItemAcquiredToast.Show(message);
        }

        // 안개 야영 세트: 사용 후 1월드 라운드 경과 → 아군 턴 종료
        private void OnItemEndedAllyTurn()
        {
            if (!_turnEnding && Clock.Phase == WorldPhase.AllyTurn) StartCoroutine(EndAllyTurn());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I) && !EncounterPanel.IsOpen) InventoryPanel.Toggle(_testParty, Leader);
            if (InventoryPanel.IsOpen || Fantasia.UI.ShopPanel.IsOpen) return;
            // 거점 칸에서 B: 상점 (아군 턴, 이동·턴 처리 중이 아닐 때)
            if (Input.GetKeyDown(KeyCode.B) && !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && Clock.Phase == WorldPhase.AllyTurn) OpenShopHere();
            if (LandmarkInfoPanel.IsOpen || DialoguePanel.IsOpen || EncounterPanel.IsOpen) return;

            // Ctrl+Right-click = "just look" (info popup only), independent
            // of move/roll state — right-click alone is left free for future
            // use (e.g. a board-side equivalent of the equip-toggle gesture).
            if (Input.GetMouseButtonDown(1) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                TryInspectLandmark();
                return;
            }

            if (_isMoving || _turnEnding) return;

            if (Input.GetKeyDown(KeyCode.Space)) TryRoll();

            if (_awaitingSelection && Input.GetMouseButtonDown(0)) TrySelectTile();
        }

        private void TryInspectLandmark()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit)) return;

            var tile = hit.collider.GetComponent<HexTile>();
            if (tile == null || !tile.IsLandmark) return;

            LandmarkInfoPanel.Show(tile.Landmark);
        }

        // 아군 턴에 주사위 1회 (이미 굴렸거나 적 턴이면 무시).
        private void TryRoll()
        {
            if (!Clock.TryBeginRoll()) return;
            _lastEventBonus = -1;
            _worldMessage = null;
            _lastRoll = MovementRules.RollMovePoints(pursuedByReaper: Hunt.Active); // 피의 사냥 중 주사위 −2 (☆)
            HighlightReachable(_lastRoll);
        }

        private void HighlightReachable(int movePoints)
        {
            _reachability = _board.ComputeReachability(_currentCoord, movePoints);
            _board.ClearHighlights();
            foreach (var coord in _reachability.ReachableTiles)
            {
                if (_board.TryGetTile(coord, out var tile)) tile.SetReachable(true);
            }
            _awaitingSelection = true;
        }

        private void TrySelectTile()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit)) return;

            var tile = hit.collider.GetComponent<HexTile>();
            if (tile == null || !_reachability.Contains(tile.Coordinate)) return;

            MoveTo(tile.Coordinate);
        }

        private void MoveTo(HexCoord target)
        {
            var path = _reachability.GetPathTo(target);
            _board.ClearHighlights();
            _awaitingSelection = false;
            StartCoroutine(WalkPath(path));
        }

        private IEnumerator WalkPath(List<HexCoord> path)
        {
            _isMoving = true;

            foreach (var coord in path)
            {
                var start = _token.position;
                var end = TokenSurfacePosition(coord);

                for (float t = 0f; t < secondsPerTile; t += Time.deltaTime)
                {
                    _token.position = Vector3.Lerp(start, end, t / secondsPerTile);
                    yield return null;
                }
                _token.position = end;
                if (!coord.Equals(_currentCoord)) _lastSafeCoord = _currentCoord;
                _currentCoord = coord;
            }

            _isMoving = false;
            BoardSession.Instance.PlayerPosition = _currentCoord;

            // 피의 사냥 사신이 있는 칸에 도착 → 그 사신과 조우
            if (Hunt.Active && Hunt.Reapers.Contains(_currentCoord) && ReaperEnemy != null)
            {
                ShowReaperEncounter();
                yield break;
            }

            // 도착한 칸이 인카운터면 조우 창 (지나가기만 하면 발동하지 않음, 클리어한 칸은 다시 안 뜸).
            if (_board.TryGetTile(_currentCoord, out var landedTile) && landedTile.IsEncounter && !landedTile.IsCleared
                && landedTile.Enemy != null)
            {
                _encounterResultMessage = null;
                _pendingEnemy = landedTile.Enemy;
                _pendingIsReaper = false;
                EncounterPanel.Show(landedTile.Enemy, Leader, OnEncounterResolved);
                yield break;
            }

            HandleMapTile(landedTile);

            // 이벤트 칸 추가 이동이 생기면 한 번 더 움직이고, 아니면 아군 턴 종료.
            // 거점 도착으로 상점이 열렸으면 닫을 때까지 기다림 (서비스·제작을 쓰면 상점이 아군 턴을 끝냄)
            while (Fantasia.UI.ShopPanel.IsOpen) yield return null;
            if (_turnEnding || Clock.Phase != WorldPhase.AllyTurn) yield break;
            if (!TryGrantEventBonusMove(landedTile)) StartCoroutine(EndAllyTurn());
        }

        // 월드맵 특수 칸 도착 처리 — 회복·보물은 칸마다 1회 (☆ 개발 임시: 기획 규칙 대기, WorldMap.md)
        private void HandleMapTile(HexTile tile)
        {
            if (tile == null || !_board.UsesWorldMap) return;
            var session = BoardSession.Instance;
            switch (tile.MapKind)
            {
                case MapTileKind.Hub:
                    OpenShopHere();
                    break;
                case MapTileKind.Recovery when !tile.IsUsed:
                    foreach (var m in _testParty.AliveMembers) m.Heal(Mathf.CeilToInt(m.MaxHP * RecoveryTileHpRatio));
                    session.UsedMapTiles.Add(tile.Coordinate);
                    tile.SetUsed(true);
                    _encounterResultMessage = $"{tile.MapLabel} — 파티 HP {RecoveryTileHpRatio * 100f:0}% 회복 (임시 규칙)";
                    ItemAcquiredToast.Show("회복 칸: 파티 HP 회복");
                    break;
                case MapTileKind.Treasure when !tile.IsUsed:
                    var drop = Fantasia.Combat.LootRules.RollDrop(_testParty, ItemCatalog.Current, new System.Random());
                    session.UsedMapTiles.Add(tile.Coordinate);
                    tile.SetUsed(true);
                    if (drop.HasValue)
                    {
                        if (!Fantasia.Combat.LootRules.TryAdd(_testParty.Inventory, drop.Value)) session.PendingLoot.Add(drop.Value);
                        _encounterResultMessage = $"{tile.MapLabel} — {drop.Value.Name} 획득 (임시 규칙)";
                        ItemAcquiredToast.Show($"보물: {drop.Value.Name}");
                    }
                    break;
            }
        }

        private const float RecoveryTileHpRatio = 0.3f;

        // ── D20 도시 상점: 거점 칸에서 열림 (도착하면 자동, 아군 턴 중 B) ──
        private void OpenShopHere()
        {
            if (!_board.UsesWorldMap || !_board.TryGetTile(_currentCoord, out var tile) || tile.MapKind != MapTileKind.Hub) return;
            var (row, col) = WorldMapData.AxialToOffset(_currentCoord);
            var shop = ItemCatalog.Current != null ? ItemCatalog.Current.ShopAtHub(WorldMapData.Label(row, col)) : null;
            if (shop == null)
            {
                _encounterResultMessage = $"{tile.MapLabel} — 안전 지대";
                return;
            }
            Fantasia.UI.ShopPanel.Open(shop, _testParty, ShopStages, () => Clock.WorldRound - 1);
        }

        // 판매 단계: 2막 = 런던 보스(M11) 처치, 고성 = 베르사유 보스(I25) 처치
        private (bool act2, bool castle) ShopStages()
        {
            var cleared = BoardSession.Instance.ClearedEncounters;
            bool Cleared(string label) => _board.UsesWorldMap && cleared.Contains(_board.Map.TileAt(label).Coord);
            return (Cleared("M11"), Cleared("I25"));
        }

        private bool OnHubTile => _board != null && _board.UsesWorldMap && _board.TryGetTile(_currentCoord, out var t) && t.MapKind == MapTileKind.Hub;

        // 조우한 적 + 같은 구역 소형 적 1마리 (프로토타입 편성 — 실제 조우 조합은 D13·P06 기획).
        // 기습 성공/실패는 BattleQueue가 첫 라운드 순서에 반영한다. 전투가 이 아군 턴의 마지막 행동.
        private void EnterCombat(EncounterOutcome opening)
        {
            var setup = new Fantasia.Combat.BattleSetup { Opening = opening };
            var session = BoardSession.Instance;
            session.PendingReaperFight = _pendingIsReaper;
            if (_pendingIsReaper)
            {
                // 피의 사냥 사신 1마리와 전투 (☆). 이기면 BattleController가 Hunt.ReaperKilled 표시
                setup.Enemies.Add(ReaperEnemy);
                session.PendingBattle = setup;
                session.PendingEncounterCoord = null;
                session.LastSafeCoord = _lastSafeCoord;
                session.AllyTurnEndedByBattle = true;
                SceneManager.LoadScene(CombatSceneName);
                return;
            }
            var pool = _board.EncounterEnemyPool;
            var forced = System.Array.Find(pool.Concat(_board.RegionBosses).ToArray(), e => e != null && e.Id == DevForcedEnemyId);
            var first = forced != null ? forced : _pendingEnemy;
            setup.Enemies.Add(first);
            // 보스는 단독 (D14). 보스 데이터가 없는 보스 칸은 임시로 소형 적 3마리 — 처치하면 지역 출구가 열린다
            int extra = first != null && first.IsBoss ? 0 : _board.TryGetTile(_currentCoord, out var here) && here.IsBoss ? 2 : 1;
            for (int i = 0; i < extra && pool.Length > 0; i++) setup.Enemies.Add(forced != null ? forced : pool[Random.Range(0, pool.Length)]);
            // 전리품 장비 레벨 = 지역 (런던 1 · 베르사유 4 · 로마 7)
            if (_board.UsesWorldMap) Fantasia.Combat.LootRules.CurrentZoneWearLevel = ZoneWearLevel(_board.Map.RegionOf(_currentCoord));
            session.PendingBattle = setup;
            session.PendingEncounterCoord = _currentCoord;
            session.LastSafeCoord = _lastSafeCoord;
            session.AllyTurnEndedByBattle = true;
            SceneManager.LoadScene(CombatSceneName);
        }

        // EncounterPanel이 판정(슬롯 공개 연출 포함)을 끝낸 뒤 호출한다.
        private void OnEncounterResolved(EncounterOutcome outcome)
        {
            _encounterResultMessage = EncounterPanel.OutcomeMessage(outcome);
            switch (outcome)
            {
                case EncounterOutcome.SneakSuccess:
                    // 전투 없이 이 칸에 남는다 (경험치·전리품 없음).
                    StartCoroutine(EndAllyTurn());
                    break;
                case EncounterOutcome.RetreatSuccess:
                    _currentCoord = _lastSafeCoord;
                    _token.position = TokenSurfacePosition(_currentCoord);
                    BoardSession.Instance.PlayerPosition = _currentCoord;
                    StartCoroutine(EndAllyTurn());
                    break;
                default:
                    EnterCombat(outcome);
                    break;
            }
        }

        // 이벤트 발판에 도착하면 0/1/2칸 중 랜덤으로 추가 이동 — 아군 턴당 1회(연쇄 금지). 추가 이동이 생겼으면 true.
        private bool TryGrantEventBonusMove(HexTile landedTile)
        {
            if (landedTile == null || !landedTile.IsEventTile || !Clock.TryConsumeEventBonus()) return false;

            _lastEventBonus = MovementRules.RollEventBonusMove();
            if (_lastEventBonus <= 0) return false;

            HighlightReachable(_lastEventBonus);
            return true;
        }

        // 아군 턴 종료 → 적 턴(월드 처리) → 다음 아군 턴 시작(중독 등 보드 지속 피해).
        private IEnumerator EndAllyTurn()
        {
            _turnEnding = true;
            _board.ClearHighlights();
            _awaitingSelection = false;

            Clock.EndAllyTurn();
            RefreshTopBar(announce: true);
            var enemyLog = WorldTurnRules.RunEnemyTurn(Clock);
            enemyLog.AddRange(RunBloodHuntEnemyTurn());
            RefreshReaperFigures();
            yield return new WaitForSeconds(EnemyTurnSeconds);

            Clock.EndEnemyTurn();
            // D20 상점 재입고: 완료 월드 라운드 3·6·9…
            if (ItemCatalog.Current != null && Fantasia.Items.ShopRules.TryRestock(BoardSession.Instance.Shop, ItemCatalog.Current.Shops, Clock.WorldRound - 1))
                enemyLog.Add("도시 상점 재입고");
            BoardSession.Instance.UsedWorldItemThisTurn.Clear();
            var allyLog = WorldTurnRules.BeginAllyTurn(_testParty);
            var lines = enemyLog.Concat(allyLog).ToList();

            // 피의 사냥: 아군 턴마다 사신이 2칸씩 다가옴 — 파티 칸에 닿으면 바로 조우
            int caught = _board.UsesWorldMap ? BloodHuntRules.MoveReapers(Hunt, _board.Map, _currentCoord, _board.IsExitOpen) : -1;
            RefreshReaperFigures();
            if (Hunt.Active) lines.Add($"사신이 다가온다 (남은 아군 턴 {Hunt.AllyTurnsLeft})");

            _worldMessage = lines.Count > 0 ? string.Join(" / ", lines) : null;
            foreach (var line in lines) Debug.Log("[World] " + line);
            RefreshTopBar(announce: true);
            _turnEnding = false;
            if (caught >= 0 && ReaperEnemy != null) ShowReaperEncounter();
        }

        // ── D24 피의 흔적·피의 사냥 / D25 영혼 수확 ──
        private List<string> RunBloodHuntEnemyTurn()
        {
            var session = BoardSession.Instance;
            int g = session.BloodTrail, s = session.SoulHarvest;
            var log = BloodHuntRules.OnEnemyTurn(Hunt, ref g, ref s, _testParty, _board.UsesWorldMap ? _board.Map : null,
                _currentCoord, new System.Random(), out var victim);
            session.BloodTrail = g;
            session.SoulHarvest = s;
            foreach (var line in log)
                if (line.StartsWith("피의 사냥!") || line.StartsWith("피의 사냥 실패")) ItemAcquiredToast.Show(line);
            return log;
        }

        private void ShowReaperEncounter()
        {
            _encounterResultMessage = null;
            _pendingEnemy = ReaperEnemy;
            _pendingIsReaper = true;
            EncounterPanel.Show(ReaperEnemy, Leader, OnEncounterResolved);
        }

        // 사신전에서 이기고 돌아왔으면 성공 처리 (보상), 아니면 사신은 그대로
        private void ResolveReaperFightResult()
        {
            var session = BoardSession.Instance;
            if (!session.PendingReaperFight) return;
            session.PendingReaperFight = false;
            if (!Hunt.ReaperKilled) return;
            int g = session.BloodTrail;
            string message = BloodHuntRules.Succeed(Hunt, ref g, _testParty, ItemCatalog.Current);
            session.BloodTrail = g;
            _encounterResultMessage = message;
            ItemAcquiredToast.Show(message);
            Debug.Log("[World] " + message);
        }

        // 보드 위 사신 말 (임시 인형 축소)
        private void RefreshReaperFigures()
        {
            foreach (var go in _reaperFigures) if (go != null) Destroy(go);
            _reaperFigures.Clear();
            if (!Hunt.Active || ReaperEnemy == null) return;
            foreach (var coord in Hunt.Reapers)
            {
                var fig = Fantasia.UI.PlaceholderFigure.Build(ReaperEnemy);
                fig.transform.position = _board.CoordToWorld(coord) + Vector3.up * _board.TileHeight;
                fig.transform.localScale = Vector3.one * 0.6f;
                fig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                _reaperFigures.Add(fig);
            }
        }

        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 키보드·마우스 대신 같은 굴림·이동 코드를 부른다 ─────
        // 자동 플레이 점검에서 특정 적 조합을 강제할 때 (예: 그림자 사냥개 특성 확인). 평소엔 null.
        public static string DevForcedEnemyId;

        // 새 아군 턴을 시작할 수 있는 상태 (이동·창·적 턴 연출이 모두 끝났고 아직 안 굴림).
        public bool DevIsIdle => !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && !LandmarkInfoPanel.IsOpen && !DialoguePanel.IsOpen && !InventoryPanel.IsOpen && !Fantasia.UI.ShopPanel.IsOpen
                                 && Clock.Phase == WorldPhase.AllyTurn && !Clock.HasRolledThisTurn;
        public bool DevAwaitingSelection => _awaitingSelection;
        public int DevWorldRound => Clock.WorldRound;

        public void DevRoll() => TryRoll();
        public void DevOpenShop() => OpenShopHere();

        // 자동 플레이: 월드맵 칸 이름(예: "M10")으로 파티를 옮긴다
        public void DevTeleport(string label)
        {
            if (!_board.UsesWorldMap || !WorldMapData.TryParseLabel(label, out var coord) || !_board.TryGetTile(coord, out _)) return;
            _currentCoord = coord;
            _lastSafeCoord = coord;
            BoardSession.Instance.PlayerPosition = coord;
            _token.position = TokenSurfacePosition(coord);
            var rig = Camera.main != null ? Camera.main.GetComponent<BoardCameraRig>() : null;
            if (rig != null) rig.SnapToTarget();
        }

        // 이동 가능한 칸 중 아직 안 깬 인카운터가 있으면 그 칸, 없으면 가장 가까운 인카운터에 제일 다가가는 칸.
        public void DevMoveTowardEncounter() => DevMoveTowardEncounter(bossOnly: false);

        // 피의 사냥 점검: 가장 가까운 사신 쪽으로
        public void DevMoveTowardReaper()
        {
            if (!_awaitingSelection || !Hunt.Active) return;
            var field = _board.DistanceField(Hunt.Reapers);
            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = field.TryGetValue(coord, out int v) ? v : int.MaxValue;
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }
            MoveTo(best);
        }

        // 피의 사냥 점검: 전투 칸을 피해 가까운 빈 칸으로 (게이지가 찰 때까지 시간 보내기)
        public void DevMoveAvoidingEncounters()
        {
            if (!_awaitingSelection) return;
            var safe = _reachability.ReachableTiles
                .Where(c => _board.TryGetTile(c, out var t) && !t.IsEncounter && t.MapKind == MapTileKind.Walkable)
                .OrderBy(c => c.DistanceTo(_currentCoord)).ToList();
            if (safe.Count > 0) MoveTo(safe[0]);
        }

        // bossOnly: 보스 칸만 목표 (자동 플레이 -autoplay-boss)
        public void DevMoveTowardEncounter(bool bossOnly)
        {
            if (!_awaitingSelection) return;
            var encounters = new List<HexCoord>();
            foreach (var tile in _board.Tiles)
                if (tile.IsEncounter && !tile.IsCleared && tile.Enemy != null && tile.IsBoss == bossOnly) encounters.Add(tile.Coordinate);

            // 장애물을 돌아가는 실제 이동 거리로 가장 가까운 적 칸 쪽
            var field = _board.DistanceField(encounters);
            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = field.TryGetValue(coord, out int v) ? v : int.MaxValue;
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }
            MoveTo(best);
        }

        private void OnGUI()
        {
            if (InventoryPanel.IsOpen || Fantasia.UI.ShopPanel.IsOpen) return; // 개발용 안내 글자가 창 위에 겹치지 않게
            float top = Screen.height * 0.13f; // 보드 상단 바·지역명 배너 아래
            string rollHint = Clock.Phase != WorldPhase.AllyTurn ? "적 턴 진행 중..."
                : Clock.HasRolledThisTurn ? $"주사위: {_lastRoll}" : "Space로 주사위 굴리기";
            GUI.Label(new Rect(10, top + 10, 420, 24), $"{rollHint}   (파티장: {Leader?.Job.DisplayName ?? "-"})");
            if (_awaitingSelection)
            {
                GUI.Label(new Rect(10, top + 34, 700, 24), "초록 칸 클릭: 이동 · 주황 적 / 자주 보스 / 파랑 거점 / 보라 이벤트 / 초록 회복 / 노랑 보물");
            }
            else if (_isMoving)
            {
                GUI.Label(new Rect(10, top + 34, 400, 24), "이동 중...");
            }
            if (_lastEventBonus >= 0)
            {
                GUI.Label(new Rect(10, top + 106, 400, 24), $"이벤트 칸 추가 이동: +{_lastEventBonus}칸 (이번 턴 1회)");
            }
            GUI.Label(new Rect(10, top + 58, 600, 24), _board != null && _board.UsesWorldMap ? "카메라: WASD·방향키 이동, 휠 줌, F 파티로" : "Ctrl+우클릭: 지형지물 정보 확인");
            GUI.Label(new Rect(10, top + 82, 600, 24), "I 키·파티 초상 클릭: 인벤토리·장비 / 퀵 슬롯 클릭: 소모품 사용" + (OnHubTile ? " / B: 상점" : ""));

            if (!EncounterPanel.IsOpen && !string.IsNullOrEmpty(_encounterResultMessage))
            {
                GUI.Label(new Rect(10, top + 130, 600, 24), _encounterResultMessage);
            }
            if (!string.IsNullOrEmpty(_worldMessage))
            {
                GUI.Label(new Rect(10, top + 154, 700, 24), "턴 시작: " + _worldMessage);
            }
        }
    }
}
