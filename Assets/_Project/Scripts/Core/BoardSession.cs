using System.Collections.Generic;
using Fantasia.Board;
using UnityEngine;

namespace Fantasia.Core
{
    // Only decides QuestTrackerPanel's title color for now (gold=Main,
    // silver=Sub — the "tier" coloring convention common in MMO/mobile RPG
    // quest logs) and display order (Main pinned at top, Sub stacks below)
    // — no gameplay difference yet.
    public enum QuestType
    {
        Main,
        Sub,
    }

    // Persists across the board <-> combat scene round trip (DevSceneNav /
    // real encounter triggers) so player position and cleared-encounter
    // progress survive it. The board itself still regenerates fresh on every
    // scene load, but from this same seed, so each coordinate's
    // blocked/encounter roll comes out identical every time.
    public class BoardSession : MonoBehaviour
    {
        public static BoardSession Instance { get; private set; }

        public int BoardSeed { get; private set; }
        public HexCoord PlayerPosition { get; set; }
        public HashSet<HexCoord> ClearedEncounters { get; } = new HashSet<HexCoord>();
        public HexCoord? PendingEncounterCoord { get; set; }
        // 월드맵: 시작 위치를 정했는지 (처음이면 런던 거점 S05에서 시작), 이미 쓴 회복·보물 칸
        public bool HasPlayerPosition { get; set; }
        public HashSet<HexCoord> UsedMapTiles { get; } = new HashSet<HexCoord>();

        // 보드·전투가 같이 쓰는 3인 파티 (HP·집중력 등이 씬을 넘어 유지된다).
        public Fantasia.Data.Party Party { get; set; }
        // 조우 → 전투 씬으로 넘기는 적 구성·기습 결과. 전투 씬이 읽고 비운다.
        public Fantasia.Combat.BattleSetup PendingBattle { get; set; }
        // 조우 칸에 들어오기 직전 칸 — 전원 퇴각·전원 쓰러짐 뒤 돌아가는 "직전 안전 타일" (D21).
        public HexCoord LastSafeCoord { get; set; }

        // 월드 턴 시계 ([아군 턴] → [적 턴]) — 씬을 오가도 이어진다.
        public Fantasia.World.WorldClock World { get; private set; } = new();
        // 아군 턴이 전투로 끝났으면 보드로 돌아오자마자 적 턴을 진행한다.
        public bool AllyTurnEndedByBattle { get; set; }

        // 보드 상단 바 표시값. 피의 흔적 G·영혼 수확 S(0~100)의 증가 규칙은 D24·D25에서 적 턴에 연결한다.
        public int BloodTrail { get; set; }
        public int SoulHarvest { get; set; }
        // D24 피의 사냥 (사신 위치·남은 아군 턴·유예) / 지금 전투가 사신전인지
        public Fantasia.World.BloodHuntState Hunt { get; } = new();
        public bool PendingReaperFight { get; set; }
        // D20 도시 상점 재고·한정 구매·재입고 경계
        public Fantasia.Items.ShopState Shop { get; } = new();
        public string RegionName { get; set; } = "런던"; // 보드 맵(P13) 확정 전 임시 — 칸·구역별로 바뀔 예정

        // 안개 연막병: 다음 기습 슬롯 성공률 +%p (기습 판정 1회에 소모)
        public int PendingAmbushBonus { get; set; }
        // 인벤토리가 차서 받지 못한 전리품 — 인벤토리 창에서 자리를 비우고 받는다 (자동 삭제 안 함)
        public List<Fantasia.Combat.LootDrop> PendingLoot { get; } = new();
        // 퀘스트 물건 (보스 확정 보상 — 검시 기록·붉은 룬 조각 등). 퀘스트 시스템(D22) 전까지 이름만 보관
        public List<string> KeyItems { get; } = new();
        // 스토리 진행 플래그 (ActOneStory — 보스 최초 등장·처치·보상·제임스 보고 등). 저장 대상
        public HashSet<string> StoryFlags { get; } = new();
        // 월드 회복약·정화약은 아군 턴마다 파티원별 1회 (☆ 개발 임시 — System.md "이동 종료 전 1회"를 파티 턴에 맞춤)
        public HashSet<Fantasia.Data.PartyMember> UsedWorldItemThisTurn { get; } = new();

        public void RestoreBoardSeed(int seed) => BoardSeed = seed; // D27 불러오기

        // ── 회차·저장 (Save.md) ──
        public enum RunState { Active, Ended, ActCleared }
        public string RunId { get; set; } = "";
        public RunState Run { get; set; } = RunState.Active;
        // 누적 플레이 시간 (메인 메뉴·창 비활성 시간 제외 — Save.md 36 기본안)
        public float PlaySeconds { get; set; }
        // 주사위 확정 뒤 아직 칸을 고르지 않은 이동력 (재접속하면 같은 값으로 선택부터) / 이벤트 칸 추가 이동
        public int PendingMovePoints { get; set; } = -1;
        public int PendingBonusMove { get; set; } = -1;
        // 조우 판정(기습·몰래 지나가기) 결과가 확정됐지만 아직 처리 전 (EncounterOutcome, -1 없음)
        public int PendingEncounterOutcome { get; set; } = -1;
        // 진행 중인 전투 스냅숏 (Save.md 5 — 전투 중 종료 시 마지막 확정 상태에서 재개). 전투가 끝나면 null
        public string BattleSnapshotJson { get; set; }

        private void Update()
        {
            if (Party == null || !Application.isFocused || Run != RunState.Active) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == Fantasia.UI.MainMenuController.SceneName) return;
            PlaySeconds += Time.unscaledDeltaTime;
        }

        // 전원 영구 사망 → 플레이 종료 후 처음부터 (파티·진행·인벤토리·보드 시드 초기화).
        public void ResetForNewGame()
        {
            Party = null;
            PendingBattle = null;
            PendingEncounterCoord = null;
            PlayerPosition = default;
            LastSafeCoord = default;
            World = new Fantasia.World.WorldClock();
            AllyTurnEndedByBattle = false;
            BloodTrail = 0;
            SoulHarvest = 0;
            Hunt.Clear();
            Hunt.GraceRounds = 0;
            PendingReaperFight = false;
            Shop.Clear();
            PendingAmbushBonus = 0;
            UsedWorldItemThisTurn.Clear();
            PendingLoot.Clear();
            KeyItems.Clear();
            StoryFlags.Clear();
            ResetQuests();
            ClearedEncounters.Clear();
            UsedMapTiles.Clear();
            HasPlayerPosition = false;
            BoardSeed = Random.Range(int.MinValue, int.MaxValue);
            // 새 회차: 회차 ID·게임 난수 새로
            RunId = System.Guid.NewGuid().ToString("N");
            Run = RunState.Active;
            PlaySeconds = 0f;
            PendingMovePoints = -1;
            BattleSnapshotJson = null;
            PendingBonusMove = -1;
            PendingEncounterOutcome = -1;
            GameRandom.Seed(Random.Range(int.MinValue, int.MaxValue));
        }

        // Real quests (GDD content, not yet designed) will replace this —
        // for now it only proves the right-side quest-tracker UI mechanism
        // works (including several active at once), seeded with throwaway
        // dummy quests (see QuestTrackerPanel).
        public readonly struct QuestEntry
        {
            public readonly string Title;
            public readonly string Objective;
            public readonly QuestType Kind;

            public QuestEntry(string title, string objective, QuestType kind)
            {
                Title = title;
                Objective = objective;
                Kind = kind;
            }
        }

        private readonly List<QuestEntry> _quests = new List<QuestEntry>();
        public IReadOnlyList<QuestEntry> Quests => _quests;
        // 완료한 퀘스트 (사건 기록 창 [완료] 탭). 저장 대상
        private readonly List<QuestEntry> _completedQuests = new List<QuestEntry>();
        public IReadOnlyList<QuestEntry> CompletedQuests => _completedQuests;

        // 새 게임: 진행 퀘스트를 1막 시작 상태로
        public void ResetQuests()
        {
            _quests.Clear();
            _completedQuests.Clear();
            _quests.Add(new QuestEntry(ActOneStory.QuestCase, ActOneStory.QuestCaseObjective, QuestType.Main));
            QuestChanged?.Invoke();
        }

        // D27 불러오기
        public void RestoreQuests(IEnumerable<QuestEntry> active, IEnumerable<QuestEntry> completed)
        {
            _quests.Clear();
            _completedQuests.Clear();
            _quests.AddRange(active);
            _completedQuests.AddRange(completed);
            QuestChanged?.Invoke();
        }

        public event System.Action QuestChanged;

        // Adds a new quest, or updates the existing one with the same title.
        public void SetQuest(string title, string objective, QuestType kind)
        {
            for (int i = 0; i < _quests.Count; i++)
            {
                if (_quests[i].Title == title)
                {
                    _quests[i] = new QuestEntry(title, objective, kind);
                    QuestChanged?.Invoke();
                    return;
                }
            }
            _quests.Add(new QuestEntry(title, objective, kind));
            QuestChanged?.Invoke();
        }

        // Fires after a quest is removed by CompleteQuest — DialoguePanel
        // subscribes to this instead of any specific completion site calling
        // into UI directly (same decoupling idea as ItemAdded/QuestChanged).
        public event System.Action<QuestEntry> QuestCompleted;

        // Returns false if no quest had that title.
        public bool CompleteQuest(string title)
        {
            for (int i = 0; i < _quests.Count; i++)
            {
                if (_quests[i].Title == title)
                {
                    var completed = _quests[i];
                    _quests.RemoveAt(i);
                    _completedQuests.Add(completed);
                    QuestChanged?.Invoke();
                    QuestCompleted?.Invoke(completed);
                    return true;
                }
            }
            return false;
        }

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("BoardSession").AddComponent<BoardSession>().Initialize();
        }

        private void Awake() => Initialize();

        // Idempotent, like HexBoard.Generate() — AddComponent reliably fires
        // Awake in Play mode, but not always from editor tooling, so
        // EnsureExists() also calls this directly rather than trusting Awake.
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            // Only legal in Play mode — editor tooling (self-tests, scene
            // scaffolding) that calls this outside Play mode skips it, since
            // there's no scene-load lifecycle to persist across there anyway.
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                // Uncapped frame rate on a scene full of 3D objects can pin a
                // low-end GPU and crash the editor. This is the one place
                // every scene's Start() reaches before anything else runs
                // (see BoardTestController/BattleController.Start(),
                // ItemAcquiredToast.Awake()), so cap it once here rather than
                // per-scene.
                Application.targetFrameRate = 60;

                // 1막 시작 퀘스트 (ActOneStory) — 예전 더미 퀘스트 대신
                ResetQuests();
            }
            BoardSeed = Random.Range(int.MinValue, int.MaxValue);
            if (string.IsNullOrEmpty(RunId)) RunId = System.Guid.NewGuid().ToString("N");
        }
    }
}
