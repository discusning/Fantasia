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
    // 판 위 말(파티장 모델, 파티가 없으면 빨간 공)과 OnGUI 안내만으로 동작한다.
    [RequireComponent(typeof(HexBoard))]
    public partial class BoardTestController : MonoBehaviour
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
            CaseLogPanel.EnsureExists(new Vector2(-10f, -74f)); // 상단 바 바로 아래 오른쪽
            BoardHintPanel.EnsureExists();
            NotebookPanel.EnsureExists(); // 11/14 탐정 수첩 (H)
            BoardDiceButton.EnsureExists(); // 11/14 B안 주사위 버튼
            _board = GetComponent<HexBoard>();
            _hasSave = SaveGame.HasSave;
            if (_board.UsesWorldMap) MusicPlayer.Play(MusicIds.FieldLondon); // 월드 복귀 시 지역 음악 (☆ 지역별 곡은 런던만 정의)
            PlaceAtStartIfNew();
            _currentCoord = BoardSession.Instance.PlayerPosition;
            _lastSafeCoord = Clock.HasRolledThisTurn ? BoardSession.Instance.LastSafeCoord : _currentCoord; // 이동 뒤 저장에서 이어하면 직전 칸 유지
            BuildTestParty();
            SpawnToken();
            _dice = BoardDice.Create(transform);
            if (Camera.main != null && _board.UsesWorldMap) BoardCameraRig.Attach(Camera.main, _token);
            PlaceLandmarks();
            HookInventory();
            if (_board.UsesWorldMap) gameObject.AddComponent<BoardSkyTint>();
            ResolveReaperFightResult();
            RefreshReaperFigures();
            PlaceExitNotice();

            // 1막 클리어 상태로 들어오면 (개발용 F9 등) 결과 화면 → 메인 메뉴
            if (BoardSession.Instance.Run == BoardSession.RunState.ActCleared)
            {
                ActClearPanel.Show(SaveGame.Capture(BoardSession.Instance), "메인 메뉴로", () => Fantasia.Core.SceneFader.Load(MainMenuController.SceneName));
                return;
            }

            Fantasia.UI.ShopPanel.EnsureExists();
            StartCoroutine(EnterFlow());
        }

        private IEnumerator EnterFlow()
        {
            RefreshTopBar(announce: false);
            yield return StoryOnEnter();
            yield return SettleSideQuests();

            // 전투로 끝난 아군 턴이면 돌아오자마자 적 턴 진행, 아니면 지금 페이즈 표시.
            if (BoardSession.Instance.AllyTurnEndedByBattle)
            {
                BoardSession.Instance.AllyTurnEndedByBattle = false;
                StartCoroutine(EndAllyTurn());
            }
            else
            {
                RefreshTopBar(announce: true);
                if (Clock.Phase == WorldPhase.EnemyTurn) StartCoroutine(EndAllyTurn()); // 적 턴 중 저장에서 이어하기
                else if (!Clock.HasRolledThisTurn) AutoSave();
                else ResumeAllyTurn();
            }
        }

        // 1막 기획 결정서: 새 게임 첫 아군 턴에 시작 대화 1번 (05) / 보스 처치 후 대화 도중 종료했으면 그 대화만 다시 (T16-35, 보상 재지급 없음)
        private IEnumerator StoryOnEnter()
        {
            var s = BoardSession.Instance;
            if (!_board.UsesWorldMap || s.Run != BoardSession.RunState.Active) yield break;
            DialoguePanel.DialogueLine[] lines = null;
            string flag = null;
            if (!s.StoryFlags.Contains(ActOneStory.FlagIntroDone) && Clock.WorldRound <= 1 && !Clock.HasRolledThisTurn && Clock.Phase == WorldPhase.AllyTurn)
                (lines, flag) = (ActOneStory.Intro, ActOneStory.FlagIntroDone);
            else if (s.StoryFlags.Contains(ActOneStory.FlagBossDefeated) && !s.StoryFlags.Contains(ActOneStory.FlagAfterDialogueDone))
                (lines, flag) = (ActOneStory.AfterBattle, ActOneStory.FlagAfterDialogueDone);
            if (lines == null) yield break;
            DialoguePanel.Play(lines, skipCloses: true);
            while (DialoguePanel.IsOpen) yield return null;
            s.StoryFlags.Add(flag);
            SaveGame.Checkpoint("대화 완료");
        }

        // 런던 선택 의뢰: 수락한 의뢰의 목표 칸을 모두 이겼으면 보상 + 완료 대사 (전투에서 돌아올 때·수락할 때)
        private IEnumerator SettleSideQuests()
        {
            var done = SideQuests.SettleAll(BoardSession.Instance);
            if (done.Count == 0) yield break;
            foreach (var q in done)
            {
                ItemAcquiredToast.Show($"의뢰 완료: {q.Title} — {SideQuests.RewardCent} Cent · 경험치 {SideQuests.RewardExp}");
                Debug.Log($"[Quest] 의뢰 완료 {q.Id} — {q.CompleteLine}");
            }
            DialoguePanel.Play(done.Select(q => new DialoguePanel.DialogueLine("내레이션", q.CompleteLine)).ToArray(), skipCloses: true);
            SaveGame.Checkpoint("의뢰 완료"); // 보상 확정 → 저장 → 대사
            while (DialoguePanel.IsOpen) yield return null;
        }

        // 아군 턴 도중 저장에서 이어하기 (Save.md 3): 확정된 주사위 → 같은 이동력으로 칸 선택부터 / 이동 완료 → 도착 처리부터
        private void ResumeAllyTurn()
        {
            var s = BoardSession.Instance;
            if (s.PendingBonusMove > 0)
            {
                _lastEventBonus = s.PendingBonusMove;
                HighlightReachable(_lastEventBonus);
                Debug.Log($"[SaveGame] 이어하기 — 이벤트 칸 추가 이동 {_lastEventBonus}");
            }
            else if (s.PendingMovePoints >= 0)
            {
                _lastRoll = s.PendingMovePoints;
                HighlightReachable(_lastRoll);
                Debug.Log($"[SaveGame] 이어하기 — 주사위 {_lastRoll} 확정 상태, 칸 선택부터");
                if (!_reachability.ReachableTiles.Any()) { _awaitingSelection = false; _board.ClearHighlights(); StartCoroutine(EndAllyTurnAfter(1.2f)); }
            }
            else
            {
                Debug.Log("[SaveGame] 이어하기 — 이동 완료 상태, 도착 처리부터");
                StartCoroutine(Arrive());
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

            // 판 위 말 = 파티장(1번) 인물 모델 축소 (2026-11-07, AssetList "파티장 캐릭터 미니어처").
            // 루트 "Token"은 칸 윗면에 서고, 모델은 그 자식. 파티가 없으면(에디터 미리보기 등) 예전 빨간 공.
            var go = new GameObject("Token");
            _token = go.transform;
            _token.rotation = Quaternion.Euler(0f, 180f, 0f); // 사신 말과 같은 방향 (카메라 쪽)
            RefreshLeaderFigure();
            _token.position = TokenSurfacePosition(_currentCoord);
        }

        private const float LeaderFigureScale = 0.8f; // 11/10 기획 3-1 B: 파티장 말 0.8배 (사신 말은 0.6배 그대로)
        private const float TurnSeconds = 0.18f;      // 첫 칸 출발 전 몸 돌리는 시간 (11/13: 0.12 → 0.25는 느림 → 중간 0.18)
        private GameObject _tokenFigure;
        private JobDefinition _tokenFigureJob;
        private bool _tokenIsSphere;
        private float _tokenFigureHeight;

        // 파티장이 바뀌면(쓰러짐·영구 사망 → 다음 순번 승계) 말 모델도 바꾼다.
        private void RefreshLeaderFigure()
        {
            if (_token == null) return;
            var job = Leader?.Job;
            if (_tokenFigure != null && job == _tokenFigureJob && (job != null || _tokenIsSphere))
            { Fantasia.UI.HeldWeapon.Show(_tokenFigure, Leader); Fantasia.UI.HandGrip.Attach(_tokenFigure); return; } // 같은 사람이어도 무기는 바뀌었을 수 있음
            if (_tokenFigure != null) DestroySafe(_tokenFigure);

            _tokenFigureJob = job;
            _tokenIsSphere = job == null;
            if (_tokenIsSphere)
            {
                _tokenFigure = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _tokenFigure.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };
                _tokenFigure.transform.SetParent(_token, false);
                _tokenFigure.transform.localPosition = Vector3.up * tokenRadius;
            }
            else
            {
                _tokenFigure = Fantasia.UI.CharacterModels.Create(job, out _tokenFigureHeight); // 받은 모델이 있으면 그것, 없으면 임시 인형
                _tokenFigure.transform.SetParent(_token, false);
                _tokenFigure.transform.localPosition = Vector3.zero;
                _tokenFigure.transform.localRotation = Quaternion.identity;
                _tokenFigure.transform.localScale = Vector3.one * LeaderFigureScale;
            }
            _tokenFigure.name = "LeaderFigure";
            // 칸 클릭(레이캐스트)을 말이 가로막지 않게 충돌체 제거
            foreach (var col in _tokenFigure.GetComponentsInChildren<Collider>(true)) DestroySafe(col);
            if (!_tokenIsSphere) Fantasia.UI.BlobShadow.Attach(_tokenFigure, _tokenFigureHeight); // 발밑 흐린 그림자 (11/10 기획 1A)
            Fantasia.UI.HeldWeapon.Show(_tokenFigure, Leader); // 11/13 장착 무기 종류에 맞는 소품만
            Fantasia.UI.HandGrip.Attach(_tokenFigure);
        }

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // 다음 칸 쪽으로 몸을 돌린다 (수평만).
        private IEnumerator FaceToward(Vector3 target)
        {
            var dir = target - _token.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) yield break;
            var from = _token.rotation;
            var to = Quaternion.LookRotation(dir, Vector3.up);
            if (Quaternion.Angle(from, to) < 1f) yield break;
            for (float t = 0f; t < TurnSeconds; t += Time.deltaTime)
            {
                _token.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / TurnSeconds));
                yield return null;
            }
            _token.rotation = to;
        }

        // Tiles are positioned base-first (see HexBoard.TileHeight), so the
        // token root sits on the tile's top surface; the figure stands on it
        // (the fallback sphere is lifted by its own radius inside RefreshLeaderFigure).
        private Vector3 TokenSurfacePosition(HexCoord coord)
        {
            var pos = _board.CoordToWorld(coord) + Vector3.up * _board.TileHeight;
            // 구조물이 있는 밟는 칸(거점·보스·우물·분수·천막·출구·보물)은 구조물 앞(입구)에 세움.
            // 2026-11-08 거점만 → 11/12 모든 구조물 칸 (HexTile.FitWalkableProp). 보이는 위치만 바뀜, 칸·이동 규칙은 그대로.
            if (_board.TryGetTile(coord, out var tile) && tile.StandFront > 0f)
                pos += Vector3.back * tile.StandFront;
            return pos;
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
            RefreshHint();
            ToastBoardEvents();
            RefreshLeaderFigure();
        }

        private void Update()
        {
            UpdateTileTooltip();
            Fantasia.UI.ShopPanel.SetReopenButton(OnHubTile && !_isMoving && !_turnEnding && Clock.Phase == WorldPhase.AllyTurn && !BoardHintPanel.AnyModalOpen && !PauseMenu.IsOpen, OpenShopHere);
            // 11/14 팀장 B안: 굴릴 수 있을 때만 주사위 버튼 (보이는 것 = 지금 굴릴 차례). 눌러도 Space와 같음
            BoardDiceButton.Set(Clock.Phase == WorldPhase.AllyTurn && !Clock.HasRolledThisTurn && !_isMoving && !_turnEnding && !BoardHintPanel.AnyModalOpen && !PauseMenu.IsOpen && !NotebookPanel.IsOpen && !BoardTopBar.TurnDropBusy && !DialoguePanel.IsOpen && !EncounterPanel.IsOpen, TryRoll);
            if (PauseMenu.IsOpen) return; // 게임 중 메뉴 — 진행 일시정지
            // 11/14 탐정 수첩: H로 열기 · 첫 월드 라운드(도입 대화 뒤) 한 번 자동으로 열림. 열려 있는 동안 보드 입력 멈춤
            if (!NotebookPanel.AutoOpened && _board.UsesWorldMap && Clock.WorldRound <= 1 && Clock.Phase == WorldPhase.AllyTurn && !Clock.HasRolledThisTurn && !BoardHintPanel.AnyModalOpen && !NotebookPanel.SuppressAutoOpen && Time.timeSinceLevelLoad > 1.5f && BoardSession.Instance.StoryFlags.Contains(ActOneStory.FlagIntroDone)) { NotebookPanel.AutoOpened = true; NotebookPanel.OpenTab(0); }
            if (Input.GetKeyDown(KeyCode.H) && !NotebookPanel.IsOpen && NotebookPanel.CanOpenThisFrame && !BoardHintPanel.AnyModalOpen) NotebookPanel.Toggle();
            if (NotebookPanel.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Q) && !EncounterPanel.IsOpen && !InventoryPanel.IsOpen && !Fantasia.UI.ShopPanel.IsOpen) CaseLogPanel.Toggle();
            if (CaseLogPanel.IsOpen || ConfirmPanel.IsOpen || ActClearPanel.IsOpen || EventPanel.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.I) && !EncounterPanel.IsOpen) InventoryPanel.Toggle(_testParty, Leader);
            if (InventoryPanel.IsOpen || Fantasia.UI.ShopPanel.IsOpen) return;
            // 거점 칸에서 B: 상점 (아군 턴, 이동·턴 처리 중이 아닐 때)
            if (Input.GetKeyDown(KeyCode.B) && !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && Clock.Phase == WorldPhase.AllyTurn) OpenShopHere();
            // F9: 마지막 자동 저장 불러오기 (메인 메뉴의 "이어하기"가 생기기 전 임시)
            if (Input.GetKeyDown(KeyCode.F9) && !_isMoving && !_turnEnding && !EncounterPanel.IsOpen) LoadLastSave();
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

            if (Input.GetKeyDown(KeyCode.Space) && !BoardTopBar.TurnDropBusy && Time.frameCount != DialoguePanel.LastClosedFrame) TryRoll(); // 대화를 닫은 Space가 주사위로 새지 않게

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
            SfxPlayer.Play(SfxIds.Dice);
            _lastEventBonus = -1;
            _worldMessage = null;
            _lastRoll = MovementRules.RollMovePoints(pursuedByReaper: Hunt.Active, out int face); // 피의 사냥 중 주사위 −2 (☆)
            // 결과 확정 → 저장 → 표시 (Save.md: 주사위 2가 나오면 껐다 켜도 2인 상태로 칸 선택부터)
            BoardSession.Instance.PendingMovePoints = _lastRoll;
            SaveGame.Checkpoint($"주사위 확정 — {_lastRoll}");
            // 3D 주사위가 굴러 나온 눈(face)이 위로 멈춘 뒤 갈 수 있는 칸 표시 (2026-11-06). 모델이 없으면 바로 표시.
            _diceRolling = true;
            _lastFace = face;
            _dice.Roll(face, () => { _diceRolling = false; ShowRollResult(); }, "이동 " + _lastRoll);
        }

        private bool _diceRolling;
        private int _lastFace;
        private BoardDice _dice;

        private void ShowRollResult()
        {
            HighlightReachable(_lastRoll);
            // 이동력 0(피의 사냥 −2로 1·2) 또는 갈 칸이 없으면 제자리 — 멈추지 않게 아군 턴을 바로 끝낸다
            if (!_reachability.ReachableTiles.Any())
            {
                _awaitingSelection = false;
                _board.ClearHighlights();
                _encounterResultMessage = _lastRoll == 0 ? "이동력 0 — 사신에게 쫓겨 움직이지 못했다" : "갈 수 있는 칸이 없다";
                StartCoroutine(EndAllyTurnAfter(1.2f));
            }
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
            if (cam == null || UIFlow.PointerBlockedByUI()) return; // 11/14: 주사위 버튼·카드 등 UI 위 클릭이 칸 선택으로 새지 않게 (11/15: 투명한 판·글자는 막지 않음)

            // 11/15 팀장 "마우스로 지정해도 안 됨": 예전엔 광선이 처음 맞은 것만 봄 → 칸 위 건물·장식·말 콜라이더에 막히면 칸이 안 골라짐.
            // 이제 광선이 지나는 것 중 가장 가까운 "갈 수 있는 칸".
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            foreach (var hit in Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var tile = hit.collider.GetComponentInParent<HexTile>();
                if (tile == null || !_reachability.Contains(tile.Coordinate)) continue;
                MoveTo(tile.Coordinate);
                return;
            }
        }

        private void MoveTo(HexCoord target)
        {
            var path = _reachability.GetPathTo(target);
            _board.ClearHighlights();
            _awaitingSelection = false;
            BoardSession.Instance.PendingMovePoints = -1; // 칸을 골랐으면 확정된 이동력 소모
            BoardSession.Instance.PendingBonusMove = -1;
            StartCoroutine(WalkPath(path));
        }

        private IEnumerator WalkPath(List<HexCoord> path)
        {
            _isMoving = true;
            ClearCampTent(); // 11/14 Q4: 다음 이동이 시작되면 텐트를 치움
            _dice.Dock(); // 11/12: 칸을 고르면 주사위는 오른쪽으로 (이번 턴 동안 눈을 보여 줌)
            var walker = Fantasia.UI.CharacterMotion.Of(_tokenFigure); // 11/13 N9 걷기 모션
            if (walker != null) walker.SetWalking(true);
            // 11/13 팀장 지적 "시점이 확확 바뀜": 카메라는 출발→도착 전체 방향으로 한 번만 천천히 돎
            if (path.Count > 0 && Camera.main != null && Camera.main.TryGetComponent<BoardCameraRig>(out var camRig))
                camRig.SetTravel(_token.position, TokenSurfacePosition(path[path.Count - 1]));

            bool first = true;
            foreach (var coord in path)
            {
                var start = _token.position;
                var end = TokenSurfacePosition(coord);
                // 첫 칸만 제자리에서 돌고, 그다음부터는 걸으면서 부드럽게 방향을 바꿈 (칸마다 멈춰 휙 돌던 것)
                if (first) { yield return FaceToward(end); first = false; }
                var dir = end - start; dir.y = 0f;
                var fromRot = _token.rotation;
                var toRot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : fromRot;

                for (float t = 0f; t < secondsPerTile; t += Time.deltaTime)
                {
                    float k = t / secondsPerTile;
                    _token.position = Vector3.Lerp(start, end, k);
                    _token.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.45f))); // 칸의 앞 45% 동안 돌기
                    yield return null;
                }
                _token.rotation = toRot;
                _token.position = end;
                SfxPlayer.Play(SfxIds.Step);
                if (!coord.Equals(_currentCoord)) _lastSafeCoord = _currentCoord;
                _currentCoord = coord;
            }

            _isMoving = false;
            if (walker != null) walker.SetWalking(false);
            _dice.Hide(); // 11/12: 도착하면 오른쪽 주사위도 치움 (조우·이벤트 창과 겹치지 않게)
            BoardSession.Instance.PlayerPosition = _currentCoord;
            BoardSession.Instance.LastSafeCoord = _lastSafeCoord;
            SaveGame.Checkpoint("이동 완료"); // Save.md 3 — 도착 처리(조우·칸 효과)보다 먼저
            yield return Arrive();
        }

        // 도착 처리 — 이동 직후, 또는 이동 완료 저장에서 이어할 때 (조우 창·칸 효과·보고·턴 종료)
        private IEnumerator Arrive()
        {
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
                // 정식 보스가 아직 없는 보스 칸(베르사유·로마)은 "개발 중" — 임시 전투로 진행·막 완료 처리하지 않음 (Boss_Abel_v1.0 07)
                if (landedTile.IsBoss && !landedTile.Enemy.IsBoss && !DevAllowTempBosses)
                {
                    _encounterResultMessage = $"{landedTile.MapLabel} — 이 지역의 보스는 개발 중입니다";
                    StartCoroutine(EndAllyTurn());
                    yield break;
                }
                // 1막 보스 권장 레벨 경고 (진입 제한은 없음): [진입] [돌아가기]
                if (ActOneStory.IsActOneBoss(landedTile.Enemy) && _testParty.LivingMembers.Any(m => m.Level < ActOneStory.RecommendedLevel))
                {
                    bool? enter = null;
                    ConfirmPanel.Show(ActOneStory.LevelWarning, "진입", "돌아가기", () => enter = true, () => enter = false);
                    while (enter == null) yield return null;
                    if (enter == false)
                    {
                        // 돌아가기: 직전 칸으로 물러나고 아군 턴 종료 (☆ 불이익 없음)
                        _currentCoord = _lastSafeCoord;
                        _token.position = TokenSurfacePosition(_currentCoord);
                        BoardSession.Instance.PlayerPosition = _currentCoord;
                        StartCoroutine(EndAllyTurn());
                        yield break;
                    }
                }
                _encounterResultMessage = null;
                _pendingEnemy = landedTile.Enemy;
                _pendingIsReaper = false;
                // 판정 결과가 이미 확정·저장된 상태에서 이어하면 창 없이 그 결과로 (Save.md)
                if (BoardSession.Instance.PendingEncounterOutcome >= 0) { OnEncounterResolved((EncounterOutcome)BoardSession.Instance.PendingEncounterOutcome); yield break; }
                EncounterPanel.Show(landedTile.Enemy, Leader, OnEncounterResolved, TileEnemies(landedTile)); // 적 전체 구성 (Q9)
                yield break;
            }

            // 1막: 보스 처치 뒤 런던 거점에 오면 제임스에게 보고 → 1막 완료·베르사유 해금 (그다음 상점)
            if (_board.UsesWorldMap && ActOneStory.NeedsReport(BoardSession.Instance) && _board.Map.TileAt(ActOneStory.HubLabel).Coord.Equals(_currentCoord))
                yield return ReportToJames();

            HandleMapTile(landedTile);

            // 이벤트 칸 추가 이동이 생기면 한 번 더 움직이고, 아니면 아군 턴 종료.
            // 거점 도착으로 상점이 열렸으면 닫을 때까지 기다림 (서비스·제작을 쓰면 상점이 아군 턴을 끝냄)
            // 상점·이벤트·우물 창을 닫을 때까지 (우물은 확인창 → 선택 창 순서라 한 프레임 넘겨 다시 확인)
            while (Fantasia.UI.ShopPanel.IsOpen || EventPanel.IsOpen || ConfirmPanel.IsOpen || TreasurePanel.IsOpen) { yield return null; if (!ConfirmPanel.IsOpen) yield return null; }
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
                case MapTileKind.Event:
                    OpenEventHere(tile);
                    break;
                case MapTileKind.Recovery when !tile.IsUsed:
                    OfferRecovery(tile);
                    break;
                case MapTileKind.Treasure when !tile.IsUsed:
                    OpenTreasure(tile);
                    SaveGame.Checkpoint("칸 효과");
                    break;
            }
        }

        // 적 칸 위에 마우스를 올리면 적 구성 (UI 솔루션 Q7): "역병 쥐 ×2 · 가고일 유체 ×1 / 총 3마리"
        private void UpdateTileTooltip()
        {
            var cam = Camera.main;
            bool blocked = cam == null || PauseMenu.IsOpen || BoardTopBar.TurnDropBusy || BoardHintPanel.AnyModalOpen || UIFlow.PointerBlockedByUI();
            HexTile tile = null;
            if (!blocked) tile = Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Select(h => h.collider.GetComponentInParent<HexTile>()).FirstOrDefault(t => t != null); // 11/15 건물·장식에 막히지 않게
            var enemies = tile != null && !tile.IsCleared ? TileEnemies(tile) : null;
            if (enemies == null) { TileTooltip.Hide(); return; }
            TileTooltip.Show($"<b>{tile.MapLabel}</b>\n{Fantasia.World.MapTileRules.CompositionText(enemies)}", Input.mousePosition);
        }

        // 그 칸에서 싸우게 될 적 목록 — 지도 툴팁·조우 창·전투가 같은 목록을 쓴다 (Q7·Q9)
        public List<EnemyDefinition> TileEnemies(HexTile tile)
        {
            if (tile == null || !tile.IsEncounter || tile.Enemy == null) return null;
            if (tile.Enemy.IsBoss) return new List<EnemyDefinition> { tile.Enemy };
            var fixedGroup = Fantasia.World.MapTileRules.ResolveEnemies(Fantasia.World.MapTileRules.EncounterAt(CoordLabel(tile.Coordinate)), ItemCatalog.Current);
            return fixedGroup ?? new List<EnemyDefinition> { tile.Enemy };
        }

        // 지도 좌표 이름 (예: J06)
        private static string CoordLabel(HexCoord c) { var (row, col) = WorldMapData.AxialToOffset(c); return WorldMapData.Label(row, col); }

        // 회복 칸 = 우물 (UI 솔루션 Q1): [이용] / [지나가기] → 1명 골라 집중력 +1. 우물마다 1회.
        // 모두 집중력이 가득 찼거나, 취소·지나가기면 횟수를 쓰지 않는다.
        private void OfferRecovery(HexTile tile)
        {
            if (!Fantasia.World.MapTileRules.WellWouldHelp(_testParty))
            {
                _encounterResultMessage = $"{tile.MapLabel} — 모든 동료의 집중력이 가득 찼습니다 (다음에 이용 가능)";
                return;
            }
            ConfirmPanel.Show($"{tile.MapLabel}\n생존 동료 1명의 집중력 {Fantasia.World.MapTileRules.WellFocus} 회복 · 이 우물에서 1회", "이용", "지나가기", () =>
            {
                var session = BoardSession.Instance;
                EventPanel.OpenWell(_testParty, member =>
                {
                    if (session.UsedMapTiles.Contains(tile.Coordinate) || !Fantasia.World.MapTileRules.CanUseWell(member)) return "회복할 수 없다";
                    int gained = Fantasia.World.MapTileRules.ApplyWell(member);
                    session.UsedMapTiles.Add(tile.Coordinate);
                    tile.SetUsed(true);
                    SaveGame.Checkpoint("칸 효과");
                    return $"{member.Job.DisplayName} 집중력 +{gained} ({member.CurrentFocus}/{member.MaxFocus})";
                }, message => _encounterResultMessage = message != null ? $"{tile.MapLabel} {message}" : $"{tile.MapLabel} — 떠남 (다음에 이용 가능)");
            }, () => _encounterResultMessage = $"{tile.MapLabel} — 지나감 (다음에 이용 가능)");
        }

        // 보물 칸 (MapTileRules): 고정 보상 전부 지급, 타일별 파티 공유 1회. 인벤토리가 차면 받을 물건으로 남김.
        private void OpenTreasure(HexTile tile)
        {
            var session = BoardSession.Instance;
            var treasure = Fantasia.World.MapTileRules.TreasureAt(CoordLabel(tile.Coordinate));
            session.UsedMapTiles.Add(tile.Coordinate);
            tile.SetUsed(true);
            if (treasure == null) { _encounterResultMessage = $"{tile.MapLabel} — 빈 보물 칸"; return; }
            _testParty.AddCent(treasure.Cent);
            var names = new List<string> { $"{treasure.Cent} Cent" };
            var shown = new List<(Sprite icon, string name, Color color)>(); // 11/13 보물 창에 보일 물건
            int stored = 0;
            foreach (var id in treasure.ConsumableIds)
            {
                var c = ItemCatalog.Current != null ? ItemCatalog.Current.Consumable(id) : null;
                if (c == null) continue;
                var drop = new Fantasia.Combat.LootDrop(c);
                if (!Fantasia.Combat.LootRules.TryAdd(_testParty.Inventory, drop)) { session.PendingLoot.Add(drop); stored++; }
                names.Add(c.DisplayName);
                shown.Add((ItemLook.IconOf(c), c.DisplayName, ItemLook.NameColor(c)));
            }
            string list = string.Join(" + ", names);
            _encounterResultMessage = $"{tile.MapLabel} — {list}";
            TreasurePanel.Show($"{tile.MapLabel}", treasure.Cent, shown, stored); // 11/13: 알림 한 줄 대신 보상 창 (받았는지 바로 보이게)
            RefreshTopBar(announce: false);
            PlaytestLog.Cent("보물 칸", treasure.Cent, _testParty.Cent, $"{tile.MapLabel}: {list}");
        }
    }
}
