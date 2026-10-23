using System.Collections;
using System.Collections.Generic;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // Standalone play-mode test harness for the board: rolls a d6, highlights
    // the reachable tiles, and walks a placeholder token tile-by-tile along
    // the rolled path on click. No UI prefabs or art required — draws its
    // own status line with OnGUI.
    [RequireComponent(typeof(HexBoard))]
    public class BoardTestController : MonoBehaviour
    {
        private const string CombatSceneName = "CombatTest";

        [SerializeField] private float tokenRadius = 0.5f; // matches the default primitive Sphere's radius
        [SerializeField] private float secondsPerTile = 0.25f;

        // Assigned by BoardTestSceneSetup from PlaceholderDataSetup — real
        // landmark placement (frequency, rules) is a design decision
        // (GDD TBD); this only proves Ctrl+Right-click info popups work for
        // several distinct landmark types.
        public LandmarkDefinition[] PlaceholderLandmarks = System.Array.Empty<LandmarkDefinition>();

        // D08 조우 판정 테스트용 — 실제 파티 전체가 아니라 명탐정 1명 스탯으로 기습/몰래
        // 회피 판정을 걸어본다. Assigned by BoardTestSceneSetup.
        public JobDefinition TestHeroJob;
        private PartyMember _testHero;

        // 하단 파티 HUD 표시용 3인 (명탐정·역병 의사·저격수). 보드 이동·조우 판정은 아직
        // 첫 번째(_testHero) 1명 기준 — 3인 보드 표현은 기획 확인 후. Assigned by BoardTestSceneSetup.
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

        // "개인 월드 턴당 1회"만 이벤트 추가 이동을 주기 위한 플래그 (System.md). 새로
        // 주사위를 굴릴 때(=새 턴 시작) 리셋되고, 한 번 쓰면 그 턴이 끝날 때까지 다시 안 준다
        // — 보너스로 다른 이벤트 칸에 도착해도 연쇄 이동이 생기지 않는다.
        private bool _eventBonusUsedThisTurn;
        private int _lastEventBonus = -1;

        // D08 조우 — 인카운터 칸에 도착하면 바로 전투로 넘기지 않고 EncounterPanel에서
        // 싸우기/기습/몰래 지나가기/후퇴를 고른다. 후퇴는 직전에 밟았던 칸으로 돌아간다.
        private HexCoord _lastSafeCoord;
        private EnemyDefinition _pendingEnemy;
        private string _encounterResultMessage;

        private void Start()
        {
            BoardSession.EnsureExists();
            ItemAcquiredToast.EnsureExists();
            QuestTrackerPanel.EnsureExists();
            LandmarkInfoPanel.EnsureExists();
            DialoguePanel.EnsureExists();
            EncounterPanel.EnsureExists();
            _board = GetComponent<HexBoard>();
            _currentCoord = BoardSession.Instance.PlayerPosition;
            _lastSafeCoord = _currentCoord;
            BuildTestParty();
            SpawnToken();
            PlaceLandmarks();
        }

        // Public so editor tooling can build the HUD for a headless screenshot (Start() doesn't run there).
        // 파티는 BoardSession에 두어 전투를 다녀와도 HP·집중력이 유지된다.
        public void BuildTestParty()
        {
            BoardSession.EnsureExists();
            if (BoardSession.Instance.Party != null && BoardSession.Instance.Party.Members.Count > 0)
            {
                _testParty = BoardSession.Instance.Party;
                _testHero = _testParty.Members[0];
                PartyHudPanel.Show(_testParty, _testHero);
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

            _testHero = _testParty.Members[0];
            PartyHudPanel.Show(_testParty, _testHero);
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

        private void PlaceLandmarks()
        {
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

        private void Update()
        {
            if (LandmarkInfoPanel.IsOpen || DialoguePanel.IsOpen || EncounterPanel.IsOpen) return;

            // Ctrl+Right-click = "just look" (info popup only), independent
            // of move/roll state — right-click alone is left free for future
            // use (e.g. a board-side equivalent of the equip-toggle gesture).
            if (Input.GetMouseButtonDown(1) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                TryInspectLandmark();
                return;
            }

            if (_isMoving) return;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                RollAndHighlight();
            }

            if (_awaitingSelection && Input.GetMouseButtonDown(0))
            {
                TrySelectTile();
            }
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

        private void RollAndHighlight()
        {
            _eventBonusUsedThisTurn = false; // 새 턴 시작 — 이벤트 보너스 다시 사용 가능
            _lastEventBonus = -1;
            _lastRoll = MovementRules.RollMovePoints(pursuedByReaper: false); // 사신 추격 연동은 D24에서
            _reachability = _board.ComputeReachability(_currentCoord, _lastRoll);

            _board.ClearHighlights();
            foreach (var coord in _reachability.ReachableTiles)
            {
                if (_board.TryGetTile(coord, out var tile))
                {
                    tile.SetReachable(true);
                }
            }

            _awaitingSelection = true;
        }

        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 키보드·마우스 대신 같은 굴림·이동 코드를 부른다 ─────
        public bool DevIsIdle => !_isMoving && !EncounterPanel.IsOpen && !LandmarkInfoPanel.IsOpen && !DialoguePanel.IsOpen;
        public bool DevAwaitingSelection => _awaitingSelection;

        public void DevRoll() => RollAndHighlight();

        // 이동 가능한 칸 중 아직 안 깬 인카운터가 있으면 그 칸, 없으면 가장 가까운 인카운터에 제일 다가가는 칸.
        public void DevMoveTowardEncounter()
        {
            if (!_awaitingSelection) return;
            var encounters = new List<HexCoord>();
            foreach (var tile in _board.Tiles)
                if (tile.IsEncounter && !tile.IsCleared && tile.Enemy != null) encounters.Add(tile.Coordinate);

            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = int.MaxValue;
                foreach (var e in encounters) d = Mathf.Min(d, coord.DistanceTo(e));
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }

            var path = _reachability.GetPathTo(best);
            _board.ClearHighlights();
            _awaitingSelection = false;
            StartCoroutine(WalkPath(path));
        }

        private void TrySelectTile()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit)) return;

            var tile = hit.collider.GetComponent<HexTile>();
            if (tile == null || !_reachability.Contains(tile.Coordinate)) return;

            var path = _reachability.GetPathTo(tile.Coordinate);
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

            // Landing on an encounter tile — not just passing through it —
            // is what starts a fight, matching how the highlight/click flow
            // already treats the destination as the meaningful stop. Cleared
            // ones don't re-trigger. D08부터는 바로 전투로 넘기지 않고 전투/기습/
            // 몰래 회피/퇴각 선택지를 먼저 띄운다 (System.md "전투").
            if (_board.TryGetTile(_currentCoord, out var landedTile) && landedTile.IsEncounter && !landedTile.IsCleared
                && landedTile.Enemy != null)
            {
                _encounterResultMessage = null;
                _pendingEnemy = landedTile.Enemy;
                EncounterPanel.Show(landedTile.Enemy, _testHero, OnEncounterResolved);
                yield break;
            }

            TryGrantEventBonusMove(landedTile);
        }

        // 조우한 적 + 같은 구역 소형 적 1마리 (프로토타입 편성 — 실제 조우 조합은 D13·P06 기획).
        // 기습 성공/실패는 BattleQueue가 첫 라운드 순서에 반영한다.
        private void EnterCombat(EncounterOutcome opening)
        {
            var setup = new Fantasia.Combat.BattleSetup { Opening = opening };
            setup.Enemies.Add(_pendingEnemy);
            var pool = _board.EncounterEnemyPool;
            if (pool.Length > 0) setup.Enemies.Add(pool[Random.Range(0, pool.Length)]);
            BoardSession.Instance.PendingBattle = setup;
            BoardSession.Instance.PendingEncounterCoord = _currentCoord;
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
                    break;
                case EncounterOutcome.RetreatSuccess:
                    _currentCoord = _lastSafeCoord;
                    _token.position = TokenSurfacePosition(_currentCoord);
                    BoardSession.Instance.PlayerPosition = _currentCoord;
                    break;
                default:
                    EnterCombat(outcome);
                    break;
            }
        }

        // 이벤트 발판에 도착하면 0/1/2칸 중 랜덤으로 추가 이동을 준다 (System.md "이동").
        // 턴당 1회만 — 이미 썼으면 조용히 지나간다(연쇄 이동 금지).
        private void TryGrantEventBonusMove(HexTile landedTile)
        {
            if (landedTile == null || !landedTile.IsEventTile || _eventBonusUsedThisTurn) return;

            _eventBonusUsedThisTurn = true;
            _lastEventBonus = MovementRules.RollEventBonusMove();
            if (_lastEventBonus <= 0) return;

            _reachability = _board.ComputeReachability(_currentCoord, _lastEventBonus);
            _board.ClearHighlights();
            foreach (var coord in _reachability.ReachableTiles)
            {
                if (_board.TryGetTile(coord, out var tile)) tile.SetReachable(true);
            }
            _awaitingSelection = true;
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 300, 24), $"주사위: {_lastRoll}  (Space로 굴리기)");
            if (_awaitingSelection)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "초록 타일을 클릭해서 이동 (주황 = 인카운터, 보라 = 이벤트)");
            }
            else if (_isMoving)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "이동 중...");
            }
            if (_lastEventBonus >= 0)
            {
                GUI.Label(new Rect(10, 106, 400, 24), $"이벤트 칸 추가 이동: +{_lastEventBonus}칸 (이번 턴 1회)");
            }
            GUI.Label(new Rect(10, 58, 400, 24), "Ctrl+우클릭: 지형지물 정보 확인");
            GUI.Label(new Rect(10, 82, 400, 24), "우측 퀘스트 창의 '달성!' 버튼으로 퀘스트 완료");

            if (!EncounterPanel.IsOpen && !string.IsNullOrEmpty(_encounterResultMessage))
            {
                GUI.Label(new Rect(10, 130, 500, 24), _encounterResultMessage);
            }
        }
    }
}
