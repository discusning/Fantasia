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
    }
}
