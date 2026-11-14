using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Fantasia.Board;
using Fantasia.Combat;
using Fantasia.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Core
{
    // 자동 플레이 점검 (D29 검증 도구). 실행 파일을 `-autoplay -autoplay-out <폴더>`로 켜면 사람 대신
    // (`-autoplay-enemy <적 Id>`를 주면 전투 적 2마리를 그 적으로 강제) 보드 이동 → 조우 창(기습) → 전투(공격·집중·방어) → 결과 → 보드 복귀를 실제 게임 코드로 진행하고,
    // 단계마다 화면 스크린샷과 전투 로그(autoplay_log.txt)를 남긴 뒤 종료한다. 인자가 없으면 아무것도 안 한다.
    public partial class AutoPlayBot : MonoBehaviour
    {
        private string _outDir;
        private int _shot;
        private readonly StringBuilder _log = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-autoplay")) return;
            if (!Application.isEditor && !Debug.isDebugBuild && !Environment.GetCommandLineArgs().Contains("-autoplay-release")) return; // 배포 빌드는 명시할 때만 (QA)
            // 자동 플레이는 사용자 저장을 건드리지 않게 별도 파일 (매번 새로)
            SaveGame.FileName = "save_autoplay.json";
            if (SaveGame.HasSave) File.Delete(SaveGame.SavePath);
            // 강제 종료 재현: 앞선 실행이 남긴 저장 파일로 시작 (-autoplay-resume <파일>)
            var bootArgs = Environment.GetCommandLineArgs();
            int resumeIndex = Array.IndexOf(bootArgs, "-autoplay-resume");
            if (resumeIndex < 0) resumeIndex = Array.IndexOf(bootArgs, "-autoplay-full-resume"); // 통플레이를 저장 지점부터 이어서
            if (resumeIndex < 0) resumeIndex = Array.IndexOf(bootArgs, "-autoplay-qa-resume");   // QA: 대화 도중 꺼진 저장에서 이어하기
            if (resumeIndex >= 0 && resumeIndex + 1 < bootArgs.Length && File.Exists(bootArgs[resumeIndex + 1])) File.Copy(bootArgs[resumeIndex + 1], SaveGame.SavePath, true);
            var go = new GameObject("AutoPlayBot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPlayBot>();
            if (Environment.GetCommandLineArgs().Contains("-autoplay-spikes")) go.AddComponent<FrameSpikeLog>(); // 11/12 느린 프레임 기록
        }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autoplay-out");
            _outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "autoplay");
            Directory.CreateDirectory(_outDir);
            BattleController.Logged += line => Note("  " + line);
            int forceIndex = Array.IndexOf(args, "-autoplay-enemy");
            if (forceIndex >= 0 && forceIndex + 1 < args.Length) BoardTestController.DevForcedEnemyId = args[forceIndex + 1];
            int seedIndex = Array.IndexOf(args, "-autoplay-seed");
            if (seedIndex >= 0 && seedIndex + 1 < args.Length && int.TryParse(args[seedIndex + 1], out int seed)) { Seed = seed; GameRandom.Seed(seed); }
            if (args.Contains("-autoplay-temp-boss")) BoardTestController.DevAllowTempBosses = true;
            Application.runInBackground = true; // 자동 점검 중 다른 창을 써도 멈추지 않게 (플레이어 설정은 그대로: 창이 뒤로 가면 멈춤)
            Note($"자동 플레이 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 해상도 {Screen.width}x{Screen.height}{(Seed >= 0 ? $" · 시드 {Seed}" : "")}");

            // 저장 점검: 이 이름의 저장 지점이 처음 오면 그 순간의 저장 파일을 복사 (= 그때 게임이 꺼진 상태)
            int snapIndex = Array.IndexOf(args, "-autoplay-snap-at");
            string snapAt = snapIndex >= 0 && snapIndex + 1 < args.Length ? args[snapIndex + 1] : null;
            bool snapped = false;
            SaveGame.Saved += reason =>
            {
                if (snapAt == null || snapped || !reason.Contains(snapAt)) return;
                snapped = true;
                File.Copy(SaveGame.SavePath, Path.Combine(_outDir, "snapshot.json"), true);
                Note($"[스냅샷] '{reason}' 시점 저장 파일 복사");
            };

            // 이어하기 통합 점검: "-autoplay-snaps 이름#건너뛸수,이름#건너뛸수,..." — 그 저장 지점이 (건너뛸 수+1)번째 올 때 snapshot_번호.json으로 복사
            int snapsIndex = Array.IndexOf(args, "-autoplay-snaps");
            if (snapsIndex >= 0 && snapsIndex + 1 < args.Length)
            {
                var specs = args[snapsIndex + 1].Split(',').Select(s => s.Split('#')).Select(p => (name: p[0], skip: p.Length > 1 && int.TryParse(p[1], out int k) ? k : 0)).ToArray();
                var seen = new int[specs.Length];
                var done = new bool[specs.Length];
                SaveGame.Saved += reason =>
                {
                    for (int k = 0; k < specs.Length; k++)
                    {
                        if (done[k] || !reason.Contains(specs[k].name)) continue;
                        if (seen[k]++ < specs[k].skip) continue;
                        done[k] = true;
                        File.Copy(SaveGame.SavePath, Path.Combine(_outDir, $"snapshot_{k}.json"), true);
                        Note($"[스냅샷 {k}] '{reason}' (월드 라운드 {BoardSession.Instance?.World.WorldRound}) 저장 파일 복사");
                    }
                };
            }

            // 성능 점검 (-autoplay-perf): 프레임·메모리·저장 시간 기록. -autoplay-uncapped면 수직 동기화를 꺼서 여유 성능까지 잼
            PerfMonitor perf = null;
            if (args.Contains("-autoplay-perf"))
            {
                if (args.Contains("-autoplay-uncapped")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
                if (args.Contains("-autoplay-aa0")) QualitySettings.antiAliasing = 0; // 비교용: 안티에일리어싱 끔
                perf = PerfMonitor.Begin(_outDir);
            }

            // 효과음 점검 (-autoplay-sfx-beep): 소리 파일 대신 짧은 삐 소리를 끼워 실제 재생 경로까지 확인
            if (args.Contains("-autoplay-sfx-beep"))
                for (int b = 0; b < SfxIds.All.Length; b++)
                {
                    int rate = 44100, len = rate / 12;
                    float hz = 440f * Mathf.Pow(2f, b / 12f);
                    var data = new float[len];
                    for (int s = 0; s < len; s++) data[s] = Mathf.Sin(2f * Mathf.PI * hz * s / rate) * 0.3f * (1f - (float)s / len);
                    var clip = AudioClip.Create(SfxIds.All[b], len, 1, rate, false);
                    clip.SetData(data, 0);
                    SfxPlayer.DevInject(SfxIds.All[b], clip);
                }
            if (args.Contains("-autoplay-sfx-beep"))
            {
                // 버튼 클릭 소리: UGUIKit 버튼을 하나 만들어 눌러 봄 (봇은 버튼 대신 기능을 바로 부르므로)
                var probe = new GameObject("SfxClickProbe", typeof(RectTransform));
                UGUIKit.CreateButton(probe.transform, "Probe", Vector2.zero, Vector2.one, "", Color.gray, 10).onClick.Invoke();
                Note($"효과음 점검: 버튼 클릭 → UI_Click {(SfxPlayer.DevCalls.ContainsKey(SfxIds.UiClick) ? "불림" : "안 불림")}");
                Destroy(probe);
            }

            if (args.Contains("-autoplay-qa-resume")) yield return QaResumeRun();
            else if (args.Contains("-autoplay-qa-report")) yield return QaReportRun();
            else if (args.Contains("-autoplay-paneldrop")) yield return PanelDropRun();
            else if (args.Contains("-autoplay-uiextra")) yield return UiExtraRun();
            else if (args.Contains("-autoplay-vfx-qa")) yield return VfxQaRun(); // 11/13 이펙트 QA
            else if (args.Contains("-autoplay-defend-clip")) yield return DefendClipRun(); // 11/14 팀원 공유 방어 영상
            else if (args.Contains("-autoplay-qa")) yield return QaRun();
            else if (args.Contains("-autoplay-full-resume")) yield return FullRun(resume: true);
            else if (args.Contains("-autoplay-resume")) yield return ResumeRun();
            else if (args.Contains("-autoplay-menu")) yield return MenuRun();
            else if (args.Contains("-autoplay-full")) yield return FullRun(resume: false);
            else if (args.Contains("-autoplay-dicevideo")) yield return DiceVideoRun();
            else if (args.Contains("-autoplay-battlevideo")) yield return BattleVideoRun();
            else if (args.Contains("-autoplay-motions")) yield return MotionShotsRun();
            else if (args.Contains("-autoplay-soundvideo")) yield return SoundVideoRun();
            else if (args.Contains("-autoplay-musicloop")) yield return MusicLoopRun();
            else yield return Play();

            if (perf != null) Note("성능 요약:\n" + perf.Finish());
            Note(SfxSummary());
            File.WriteAllText(Path.Combine(_outDir, "autoplay_log.txt"), _log.ToString(), Encoding.UTF8);
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // ── 통플레이 점검 (-autoplay-full): 새 게임 → 런던 탐험(상점·일반 전투·회복·이벤트·피의 사냥) → 레벨 3 → 보스 → 보고 → 1막 클리어 ──
        // 사람처럼 판단: HP가 낮으면 회복 칸·거점, 피의 사냥이면 사신 쪽, 레벨이 오르면 보스, 처치 뒤엔 런던 거점으로 보고.
        private IEnumerator FullRun(bool resume)
        {
            float start = Time.realtimeSinceStartup;
            float limit = start + 40 * 60;
            int turns = 0, battles = 0, wins = 0, shopVisits = 0, events = 0;
            int turnDrops = 0; string lastTurnTitle = ""; // 턴 안내 판 나온 횟수
            var shotTitles = new System.Collections.Generic.HashSet<string>(); // 보고용 사진: 장면마다 1장
            string ending = "시간 초과";

            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            if (resume)
            {
                // 이어하기 통합 점검: 저장 파일(꺼진 시점)에서 메인 메뉴 [이어하기] → 끝까지
                Note($"이어하기: {menu.DevContinueInfo.Replace("\n", " / ")}");
                yield return Shot("full_resume_menu");
                menu.OnContinue();
                yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null || FindFirstObjectByType<BattleController>() != null, 10f, "이어하기 씬");
            }
            else
            {
                menu.DevOpenPartySelect();
                yield return new WaitForSeconds(0.3f);
                menu.OnDepart();
                if (Seed >= 0) GameRandom.Seed(Seed);
                yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            }
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            yield return Shot("full_start");
            yield return PerfProbe("보드");
            Note((resume ? "통플레이 이어하기 — 파티: " : "통플레이 시작 — 파티: ") + string.Join(", ", BoardSession.Instance.Party.Members.Select(m => m.Job.DisplayName)));

            while (Time.realtimeSinceStartup < limit)
            {
                yield return null;
                string turnTitle = BoardTopBar.DevTurnDropVisible ? BoardTopBar.DevTurnDropTitle : "";
                if (turnTitle != "" && turnTitle != lastTurnTitle)
                {
                    turnDrops++;
                    string tt = turnTitle;
                    if (!shotTitles.Contains("turn:" + tt))
                    {
                        shotTitles.Add("turn:" + tt);
                        yield return new WaitForSecondsRealtime(tt == "적 턴" ? 0.4f : 0.45f); // 판이 닿은 뒤
                        yield return Shot(tt == "아군 턴" ? "full_turn_ally" : "full_turn_enemy");
                    }
                }
                if (BoardSession.Instance != null && BoardSession.Instance.Hunt.Active && !shotTitles.Contains("hunt"))
                {
                    shotTitles.Add("hunt");
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot("full_blood_hunt");
                }
                lastTurnTitle = turnTitle;
                if (ActClearPanel.IsOpen)
                {
                    yield return new WaitForSecondsRealtime(2.2f);
                    yield return Shot("full_act_clear");
                    ending = "1막 클리어";
                    ActClearPanel.DevClose();
                    yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
                    break;
                }
                if (FindFirstObjectByType<MainMenuController>() != null) { ending = "메인 메뉴로 돌아감 (전멸 등)"; break; }

                if (SceneManager.GetActiveScene().name == "CombatTest")
                {
                    battles++;
                    // 받은 3D 모델 확인용 — 적 조합마다 전투 시작 화면 1장 (적 이름으로 구분)
                    yield return WaitFor(() => FindFirstObjectByType<BattleController>()?.DevEnemies?.Count > 0, 10f, "전투 준비");
                    var fieldBattle = FindFirstObjectByType<BattleController>();
                    string fieldKey = fieldBattle != null ? string.Join("_", fieldBattle.DevEnemies.Select(e => e.Enemy.Id.Split('.').Last()).Distinct()) : "unknown";
                    if (!shotTitles.Contains("field:" + fieldKey)) { shotTitles.Add("field:" + fieldKey); yield return new WaitForSecondsRealtime(1.5f); yield return Shot("full_field_" + fieldKey); if (fieldKey.Contains("abel") || battles == 1) yield return PerfProbe("전투 " + fieldKey); }
                    yield return RunBattle(shots: false);
                    var b = _lastBattle;
                    if (b == null || !b.DevFinished) { Note("실패: 전투가 끝나지 않음"); yield return Shot("full_battle_stuck"); ending = "전투 멈춤"; break; }
                    if (b.DevEnemies.All(e => !e.IsAlive)) wins++;
                    yield return new WaitForSecondsRealtime(2f); // 결과 판이 멈출 때까지
                    string rt = b.DevHud.DevResultTitle;
                    if (!shotTitles.Contains("result:" + rt)) { shotTitles.Add("result:" + rt); yield return Shot("full_result_" + (rt == "아벨 모로 격파" ? "boss" : rt == "전투 승리" ? "victory" : "other")); }
                    b.DevContinue();
                    yield return WaitFor(() => SceneManager.GetActiveScene().name != "CombatTest", 10f, "전투 뒤 씬 전환");
                    yield return new WaitForSeconds(1f);
                    NoteParty($"전투 {battles} 뒤");
                    continue;
                }

                var board = FindFirstObjectByType<BoardTestController>();
                if (board == null) continue;
                if (DialoguePanel.IsOpen && !_boxClickChecked && DialoguePanel.DevLineIndex == 0)
                {
                    // 11/12 팀장 지시: 대화 창 아무 곳이나 눌러도 다음 줄
                    _boxClickChecked = true;
                    DialoguePanel.DevClickBox();
                    yield return null;
                    Qa(!DialoguePanel.IsOpen || DialoguePanel.DevLineIndex == 1, $"대화 창 글 부분을 누르면 다음 줄 (줄 0 → {DialoguePanel.DevLineIndex})");
                    continue;
                }
                if (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSeconds(0.15f); continue; }
                if (ConfirmPanel.IsOpen) { Note("보스 레벨 경고 → 진입"); ConfirmPanel.DevChoose(true); continue; }
                if (EncounterPanel.IsOpen) { yield return new WaitForSeconds(0.4f); EncounterPanel.DevChoose(1); yield return new WaitForSeconds(0.8f); continue; }
                if (TreasurePanel.IsOpen) { yield return WaitFor(() => TreasurePanel.DevButtonVisible, 4f, "보물 창 버튼"); Note($"보물 창: {TreasurePanel.DevSummary}"); if (!_treasureShot) { _treasureShot = true; yield return Shot("board_treasure_panel"); } TreasurePanel.DevClose(); continue; }
                if (ShopPanel.IsOpen) { shopVisits++; yield return ShopVisit(); continue; }
                if (EventPanelOpen()) { events++; yield return EventVisit(); continue; }

                if (board.DevAwaitingSelection)
                {
                    board.DevMoveToward(FullRunTargets(board, turns));
                    yield return new WaitForSeconds(0.2f);
                    continue;
                }
                if (board.DevIsIdle)
                {
                    board.DevRoll();
                    yield return WaitFor(() => !board.DevDiceRolling, 5f, "주사위 멈춤");
                    if (shotTitles.Add("dice:" + board.DevLastFace)) { Note($"주사위 눈 {board.DevLastFace} → 이동력 {board.DevLastRoll}"); yield return Shot("full_dice_" + board.DevLastFace); } // 6면 모두 맞게 멈추는지
                    if (board.DevAwaitingSelection)
                    {
                        turns++;
                        if (turns % 10 == 0) NoteParty($"보드 {turns}턴 (월드 라운드 {board.DevWorldRound})");
                    }
                }
            }

            var s = BoardSession.Instance;
            Note($"통플레이 결과: {ending} · 보드 턴 {turns} · 월드 라운드 {s?.World.WorldRound} · 전투 {battles} (승리 {wins}) · 상점 방문 {shopVisits} · 이벤트 {events} · 턴 안내 판 {turnDrops}회"
                 + $" · 레벨 {string.Join("/", s?.Party?.Members.Select(m => m.Level) ?? new int[0])} · Cent {s?.Party?.Cent} · 피의 흔적 {s?.BloodTrail} · 영혼 수확 {s?.SoulHarvest}"
                 + $" · 실제 시간 {(Time.realtimeSinceStartup - start) / 60f:0.0}분");
            yield return Shot("full_end");
            Note("자동 플레이 완료");
        }

        // 다음 목표 칸들
        private static System.Collections.Generic.List<HexCoord> FullRunTargets(BoardTestController board, int turns)
        {
            var s = BoardSession.Instance;
            var map = board.DevBoard.Map;
            var hub = map.TileAt(ActOneStory.HubLabel).Coord;
            if (ActOneStory.NeedsReport(s)) return new System.Collections.Generic.List<HexCoord> { hub };
            if (s.Hunt.Active && s.Hunt.Reapers.Count > 0) return s.Hunt.Reapers.ToList();
            var tiles = board.DevBoard.Tiles.ToList();
            var alive = s.Party.AliveMembers.ToList();
            bool hurt = alive.Any(m => m.CurrentHP < m.MaxHP * 0.45f || (m.MaxMP > 0 && m.CurrentMP < m.MaxMP * 0.25f));
            if (hurt)
            {
                var heal = tiles.Where(t => t.MapKind == MapTileKind.Recovery && !t.IsUsed && map.RegionOf(t.Coordinate) == 0).Select(t => t.Coordinate).ToList();
                if (s.Party.Cent >= 90) heal.Add(hub);
                if (heal.Count > 0) return heal;
            }
            bool ready = s.Party.LivingMembers.All(m => m.Level >= 3) || turns > 90;
            var enemies = tiles.Where(t => t.IsEncounter && !t.IsCleared && t.Enemy != null && !t.IsBoss && map.RegionOf(t.Coordinate) == 0).Select(t => t.Coordinate).ToList();
            if (ready || enemies.Count == 0) return new System.Collections.Generic.List<HexCoord> { map.TileAt("M11").Coord };
            // 보물·회복 칸도 들름
            var extras = tiles.Where(t => (t.MapKind == MapTileKind.Treasure || t.MapKind == MapTileKind.Recovery) && !t.IsUsed && map.RegionOf(t.Coordinate) == 0).Select(t => t.Coordinate);
            return enemies.Concat(extras).ToList();
        }

        // 거점 상점: 의뢰 2개 수락(처음 한 번), 영약·해독제 조금 사고, 많이 다친 1명 숙박 (숙박은 턴을 끝냄 — 결정서 B2)
        private bool _shopShots;
        private bool _quickProbed;

        // 행동 중인 캐릭터 카드의 1번 빠른 칸 가운데를 실제 클릭처럼: 그 화면 자리의 UI를 위에서부터 찾고 맨 위에 클릭을 보냄
        private IEnumerator ProbeQuickSlot(BattleUnit actor)
        {
            var party = BoardSession.Instance.Party.Members.ToList();
            int idx = party.IndexOf(actor.Member);
            var card = GameObject.Find($"PartyCard{idx}");
            var slot = card != null ? card.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "QuickSlot0") : null;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (slot == null || es == null) { Note($"빠른 칸 점검: 칸 못 찾음 (카드 {idx}, 이벤트 시스템 {(es != null)})"); yield break; }
            var screen = RectTransformUtility.WorldToScreenPoint(null, slot.TransformPoint(slot.rect.center));
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(ped, hits);
            Note($"빠른 칸 점검: {actor.DisplayName} 1번 칸 ({screen.x:0},{screen.y:0}) 위 UI = " + string.Join(" > ", hits.Take(5).Select(h => $"{h.gameObject.name}[{h.gameObject.GetComponentInParent<Canvas>()?.rootCanvas.name}]")));
            var item = actor.Member.QuickSlots[0];
            int before = item != null ? BoardSession.Instance.Party.Inventory.CountOf(item) : -1;
            if (hits.Count > 0)
            {
                ped.pointerPressRaycast = ped.pointerCurrentRaycast = hits[0];
                var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(hits[0].gameObject);
                Note($"빠른 칸 점검: 클릭 받는 것 = {(handler != null ? handler.name : "없음")}");
                if (handler != null) UnityEngine.EventSystems.ExecuteEvents.Execute(handler, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            }
            yield return new WaitForSeconds(0.6f);
            // 아군 대상 아이템이면 사람처럼 자기 카드를 눌러 대상까지 고름
            if (item != null && BoardSession.Instance.Party.Inventory.CountOf(item) == before)
            {
                var cardRt = (RectTransform)card.transform;
                ped.position = RectTransformUtility.WorldToScreenPoint(null, cardRt.TransformPoint(cardRt.rect.center));
                hits.Clear(); es.RaycastAll(ped, hits);
                var h2 = hits.Count > 0 ? UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(hits[0].gameObject) : null;
                Note($"빠른 칸 점검: 대상 고르기 — 카드 클릭 받는 것 = {(h2 != null ? h2.name : "없음")}");
                if (h2 != null) { ped.pointerPressRaycast = ped.pointerCurrentRaycast = hits[0]; UnityEngine.EventSystems.ExecuteEvents.Execute(h2, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler); }
                yield return new WaitForSeconds(2.5f);
            }
            Note($"빠른 칸 점검: {item?.DisplayName ?? "빈 칸"} 개수 {before} → {(item != null ? BoardSession.Instance.Party.Inventory.CountOf(item) : -1)}");
        }
        private bool _treasureShot;
        private IEnumerator ShopVisit()
        {
            var ui = ShopPanel.Instance;
            var party = BoardSession.Instance.Party;
            int before = party.Cent;
            ui.DevSelectTab(6); // 의뢰
            for (int row = 0; row < ui.DevRowCount; row++) { ui.DevSelectRow(row); ui.DevAct(); }
            if (!_shopShots) { ui.DevSelectRow(0); yield return new WaitForSeconds(0.3f); yield return Shot("shop_quests"); }
            ui.DevSelectTab(0);
            for (int i = 0; i < 2 && party.Cent >= 90; i++) { ui.DevSelectRow(1); ui.DevAct(); } // 생명의 영약
            if (party.Cent >= 65) { ui.DevSelectRow(4); ui.DevAct(); }                            // 해독제
            yield return new WaitForSeconds(0.3f);
            bool rest = party.AliveMembers.Any(m => m.CurrentHP < m.MaxHP * 0.6f || (m.MaxMP > 0 && m.CurrentMP < m.MaxMP * 0.4f)) && party.Cent >= 30;
            ui.DevSelectTab(3);
            ui.DevSelectRow(0); // 숙박 — 가장 다친 동료가 기본 선택
            if (!_shopShots) { _shopShots = true; yield return new WaitForSeconds(0.3f); yield return Shot("shop_service_target"); }
            Note($"  상점: Cent {before} → {party.Cent}{(rest ? " · 숙박 1명" : "")}");
            if (rest) ui.DevAct();
            else ShopPanel.Close();
            yield return new WaitForSeconds(0.3f);
        }

        // 이벤트 창 — 리플렉션으로 부른다 (이벤트 칸 코드가 없는 빌드에서도 이 파일은 컴파일됨)
        private static Type EventPanelType => Type.GetType("Fantasia.UI.EventPanel");
        private static bool EventPanelOpen() => EventPanelType?.GetProperty("IsOpen")?.GetValue(null) is true;
        private static void EventCall(string method, params object[] a) => EventPanelType?.GetMethod(method)?.Invoke(null, a);

        private IEnumerator EventVisit()
        {
            yield return new WaitForSeconds(0.3f);
            EventCall("DevPickCard", 0);   // 점술마녀: 부
            EventCall("DevPickTarget", 0); // 분수대: 1번
            EventCall("DevConfirm");
            yield return new WaitForSeconds(1.2f);
            EventCall("DevLeave");
            yield return new WaitForSeconds(0.3f);
        }

        // 메뉴 점검 (-autoplay-menu): 메인 메뉴 표시(최근 전멸 기록 등) → 설정 화면 → 새 게임 → 저장 실패 일시정지·재시도
        private IEnumerator MenuRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            Note($"메인 메뉴: {menu.DevContinueInfo.Replace("\n", " / ")}");
            yield return Shot("menu");
            SettingsPanel.Open();
            yield return new WaitForSeconds(0.3f);
            float before = GameSettings.Current.master;
            SettingsPanel.DevStepVolume(0, -1);
            Note($"설정: 전체 볼륨 {before:0.0} → {GameSettings.Current.master:0.0} · AudioListener {AudioListener.volume:0.0} · 설정 파일 {(File.Exists(Path.Combine(Application.persistentDataPath, "settings.json")) ? "저장됨" : "없음")}");
            yield return Shot("settings");
            SettingsPanel.DevStepVolume(0, +1);
            SettingsPanel.DevClose();

            // 저장 실패 (보고서 11/3 A3): 임시 파일 자리에 폴더를 만들어 쓰기를 막는다 → 안내만 하고 계속 진행 → 풀면 다음 저장 지점에서 성공
            string blocker = SaveGame.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            menu.DevOpenPartySelect();
            yield return new WaitForSecondsRealtime(0.5f);
            menu.OnDepart(); // 새 게임 저장 → 실패
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSecondsRealtime(0.8f);
            Note($"저장 실패 중: 안내 \"{SaveFailNotice.DevLast}\" (화면 {SaveFailNotice.DevVisible}) · timeScale {Time.timeScale} · 실패 표시 {SaveGame.IsFailing}");
            yield return Shot("save_error");
            Directory.Delete(blocker);
            bool retried = SaveGame.Checkpoint("자동 점검 — 다음 저장 지점");
            Note($"풀고 다음 저장 → 성공 {retried} · 실패 표시 {SaveGame.IsFailing} · 저장 파일 {(SaveGame.HasSave ? "있음" : "없음")}");
            // 보드에서 저장 실패 → 알림 화면 촬영 (보고용) → 풀고 다시 저장
            if (DialoguePanel.IsOpen) { while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSecondsRealtime(0.15f); } }
            Directory.CreateDirectory(blocker);
            SaveGame.Checkpoint("자동 점검 — 보드 저장 실패");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("save_fail_board");
            Directory.Delete(blocker);
            Note($"보드 저장 실패 → 다시 저장 {SaveGame.Checkpoint("자동 점검 — 다시 저장")} · 실패 표시 {SaveGame.IsFailing}");
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("board_k12_notice");

            // Esc 게임 중 메뉴: 열기 → 멈춤 확인 → 메인 메뉴로 → 이어하기 정보
            PauseMenu.DevOpen();
            yield return new WaitForSecondsRealtime(0.4f);
            Note($"Esc 메뉴 {(PauseMenu.IsOpen ? "열림" : "안 열림")} · timeScale {Time.timeScale}");
            yield return Shot("pause_menu");
            PauseMenu.DevToMainMenu();
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            Note($"메인 메뉴로 나감 → {FindFirstObjectByType<MainMenuController>()?.DevContinueInfo.Replace("\n", " / ")} · timeScale {Time.timeScale}");
            yield return Shot("menu_after_pause");
            Note("자동 플레이 완료");
        }

        // 저장 점검 2단계: 메인 메뉴 이어하기 → 복원된 상태·다시 실행된 결과를 기록
        private IEnumerator ResumeRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            yield return Shot("resume_menu");
            var menu = FindFirstObjectByType<MainMenuController>();
            Note($"이어하기 정보: {menu.DevContinueInfo.Replace("\n", " / ")}");
            menu.OnContinue();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null || FindFirstObjectByType<BattleController>() != null, 10f, "이어하기 씬");
            yield return new WaitForSeconds(1.5f);
            var board = FindFirstObjectByType<BoardTestController>();
            var battle = FindFirstObjectByType<BattleController>();
            if (board != null)
            {
                Note($"이어하기 → 보드 · 월드 라운드 {board.DevWorldRound} · 위치 {board.DevPosition} · 칸 선택 대기 {board.DevAwaitingSelection} · 주사위 {board.DevLastRoll}");
                yield return Shot("resume_board");
            }
            else if (battle != null)
            {
                // 확정된 행동이 다시 실행되는 것을 기다림 (로그에 결과가 남는다)
                float end = Time.time + 12f;
                while (Time.time < end && !battle.DevFinished && !battle.DevAwaitingPlayer) { if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance(); yield return null; }
                yield return Shot("resume_battle");
                Note($"이어하기 → 전투 · 라운드 {battle.DevRound} · 아군 HP {string.Join("/", battle.DevAllies.Select(a => a.CurrentHP))} · 적 HP {string.Join("/", battle.DevEnemies.Select(e => e.CurrentHP))}");
            }
            Note("자동 플레이 완료");
        }

        private IEnumerator Play()
        {
            // ── 0. 메인 메뉴 → 파티 구성 → 출발 (메뉴 씬이 빌드 첫 씬일 때) ──
            var menu = FindFirstObjectByType<MainMenuController>();
            if (menu != null)
            {
                yield return new WaitForSeconds(1f);
                yield return Shot("main_menu");
                menu.DevOpenPartySelect();
                yield return new WaitForSeconds(0.8f);
                yield return Shot("party_select");
                string before = menu.DevPartyNames;
                menu.DevCycle(0, +1); // 1번 칸 직업을 바꾸면 그 직업이 있던 칸과 자리 교환
                Note($"파티 구성: {before} → 1번 ▶ → {menu.DevPartyNames}");
                yield return new WaitForSeconds(0.5f);
                yield return Shot("party_select_swapped");
                menu.DevCycle(0, -1);
                menu.DevSetName("홈즈"); // 플레이어 이름 입력 창 (UI 솔루션 Q4)
                yield return new WaitForSeconds(0.4f);
                yield return Shot("name_input");
                menu.OnDepart();
                if (Seed >= 0) GameRandom.Seed(Seed); // 새 게임이 난수를 새로 정하므로 같은 시드로 다시
                Note($"출발: {menu.DevPartyNames}");
            }

            // ── 1. 보드 ──
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 씬 로드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            yield return Shot("board_start");
            // 11/14 탐정 수첩: 모든 쪽을 열어 찍음 (책갈피·사진·글 정렬 확인), 닫힘 확인
            if (Environment.GetCommandLineArgs().Contains("-autoplay-notebook") && NotebookPanel.Instance != null)
            {
                for (int i = 0; i < NotebookPanel.PageCount; i++) { NotebookPanel.DevOpenPage(i); yield return new WaitForSeconds(0.35f); yield return Shot($"notebook_{i:00}"); }
                Qa(NotebookPanel.IsOpen, $"탐정 수첩: {NotebookPanel.PageCount}쪽 열림");
                NotebookPanel.Toggle(); yield return new WaitForSeconds(0.3f);
                Qa(!NotebookPanel.IsOpen, "탐정 수첩: 닫힘");
            }
            // 11/12 팀장 결정 1번: 구조물 있는 밟는 칸 — 말이 서는 자리가 구조물 밖인지, 구조물이 칸 폭(1.6m) 안인지
            {
                var walk = Fantasia.Board.HexTile.WithProps.Where(t => t != null && t.StandFront > 0f).ToList();
                int inside = walk.Count(t => { var b = t.PropBounds(); b.Expand(-0.05f); var p = t.transform.position + Vector3.back * t.StandFront; p.y = b.min.y + 0.4f; return b.Contains(p); });
                float widest = walk.Count > 0 ? walk.Max(t => t.DevPropWidth) : 0f;
                Qa(walk.Count > 0 && inside == 0 && widest <= Fantasia.Board.HexTile.WalkablePropMaxWidth + 0.01f, $"구조물 칸 {walk.Count}개: 말이 구조물 안에 서는 칸 {inside}개 · 가장 넓은 구조물 {widest:0.00}m");
            }
            // 11/12 시점 사진 (-camera-preview): 구조물 칸에 선 모습 + 집 뒤로 지나간 모습 (반투명 확인). 끝나면 원래 자리로.
            if (Environment.GetCommandLineArgs().Contains("-camera-preview"))
            {
                var home = board.DevPosition;
                int cm = Fantasia.Board.BoardCameraRig.Preview;
                foreach (var (kind, label) in new[] { (Fantasia.Board.MapTileKind.Hub, "거점"), (Fantasia.Board.MapTileKind.Recovery, "우물"), (Fantasia.Board.MapTileKind.Event, "이벤트"), (Fantasia.Board.MapTileKind.Exit, "출구") })
                {
                    var tile = Fantasia.Board.HexTile.WithProps.FirstOrDefault(t => t != null && t.MapKind == kind && t.StandFront > 0f && t.PropVisible);
                    if (tile == null) continue;
                    board.DevTeleport(tile.Coordinate);
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot($"구조물칸_{label}_시점{cm}");
                }
                // 집 뒤 칸: 막힌 집 칸 바로 뒤(+z)의 빈 칸
                var all = UnityEngine.Object.FindObjectsByType<Fantasia.Board.HexTile>(FindObjectsSortMode.None);
                var homePos = board.DevBoard.CoordToWorld(home);
                var behind = all.Where(t => !t.IsBlocked && t.StandFront == 0f && t.MapKind == Fantasia.Board.MapTileKind.Walkable).OrderBy(t => (t.transform.position - homePos).sqrMagnitude)
                    .FirstOrDefault(t => Fantasia.Board.HexTile.WithProps.Any(h => h != null && h.IsBlocked && h.PropBounds().size.y > 1f
                        && Mathf.Abs(h.transform.position.x - t.transform.position.x) < 1f && t.transform.position.z - h.transform.position.z is > 1.2f and < 1.8f));
                if (behind != null)
                {
                    board.DevTeleport(behind.Coordinate);
                    yield return new WaitForSeconds(1.2f);
                    Note($"  (점검) 집 뒤 칸 — 반투명 구조물 {Fantasia.Board.BoardOccluderFade.DevFadedCount}개");
                    yield return Shot($"구조물칸_집뒤_시점{cm}");
                }
                board.DevTeleport(home);
                yield return new WaitForSeconds(1f);
            }
            ApplyStartOverrides();
            NoteParty("보드 시작");
            var args = Environment.GetCommandLineArgs();
            int startIndex = Array.IndexOf(args, "-autoplay-start");
            if (startIndex >= 0 && startIndex + 1 < args.Length) { board.DevTeleport(args[startIndex + 1]); Note($"시작 위치 → {args[startIndex + 1]}"); yield return new WaitForSeconds(0.5f); }
            bool bossRun = args.Contains("-autoplay-boss");
            bool huntRun = args.Contains("-autoplay-hunt");
            bool shotHunt = false, shotSky = false;

            // ── 11/12 이동 QA (-autoplay-move-qa, 보통 -boardcam 0~3과 같이): 집 뒤 칸들을 차례로 돌며 프레임·반투명·서는 자리·카메라 점검. 스크린샷 없음 ──
            if (args.Contains("-autoplay-move-qa"))
            {
                var mq = gameObject.AddComponent<BoardMoveQa>();
                mq.Board = board;
                var tiles = UnityEngine.Object.FindObjectsByType<Fantasia.Board.HexTile>(FindObjectsSortMode.None);
                var tall = Fantasia.Board.HexTile.WithProps.Where(h => h != null && h.IsBlocked && h.PropBounds().size.y > 0.9f).ToList();
                // 집 뒤(+z, 카메라 반대쪽) 빈 칸 — 말이 가려지기 쉬운 곳
                var behind = tiles.Where(t => !t.IsBlocked && !t.IsEncounter && t.MapKind == Fantasia.Board.MapTileKind.Walkable
                    && tall.Any(h => Mathf.Abs(h.transform.position.x - t.transform.position.x) < 1f && t.transform.position.z - h.transform.position.z is > 1.2f and < 1.8f)).ToList();
                var order = new System.Collections.Generic.List<Fantasia.Board.HexTile>();
                var from = board.DevToken.position;
                while (behind.Count > 0 && order.Count < 12)
                {
                    var next = behind.OrderBy(t => (t.transform.position - from).sqrMagnitude).First();
                    order.Add(next); behind.Remove(next); from = next.transform.position;
                }
                Note($"이동 QA: 시점 {Fantasia.Board.BoardCameraRig.Preview} · 집 뒤 목표 칸 {order.Count}개");
                if (args.Contains("-autoplay-move-shot"))
                {
                    // 11/12 기획 검수 자료: 같은 자리에서 반투명 켬/끔 비교 사진 (집 뒤 칸 2곳)
                    var fade = Camera.main.GetComponent<Fantasia.Board.BoardOccluderFade>();
                    for (int k = 0; k < Math.Min(2, order.Count) && fade != null; k++)
                    {
                        board.DevTeleport(order[k].Coordinate);
                        yield return new WaitForSeconds(1.5f);
                        yield return Shot($"반투명_켬_{k}");
                        fade.enabled = false;
                        foreach (var h in Fantasia.Board.HexTile.WithProps) if (h != null) h.SetPropAlpha(1f);
                        yield return new WaitForSeconds(0.3f);
                        yield return Shot($"반투명_끔_{k}");
                        fade.enabled = true;
                    }
                }
                int turnsMq = 0, reached = 0, stuck = 0, target = 0;
                while (turnsMq < 40 && target < order.Count && !EncounterPanel.IsOpen)
                {
                    float until = Time.time + 15f;
                    while (Time.time < until && !((board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen))
                    {
                        if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
                        if (EventPanel.IsOpen) EventPanel.DevLeave(); // 이동 QA와 상관없는 이벤트 창은 닫고 계속
                        if (TreasurePanel.IsOpen) TreasurePanel.DevClose();
                        if (ConfirmPanel.IsOpen) ConfirmPanel.DevChoose(false);
                        if (board.DevAwaitingSelection && !board.DevMoving && !EventPanel.IsOpen) board.DevMoveTowardAvoiding(new[] { order[Math.Min(target, order.Count - 1)].Coordinate }); // 이벤트 추가 이동도 목표 쪽
                        yield return new WaitForSeconds(0.15f);
                    }
                    if (EncounterPanel.IsOpen || !board.DevIsIdle) { Note($"  멈춤: 이벤트 {EventPanel.IsOpen} 대화 {DialoguePanel.IsOpen} 선택대기 {board.DevAwaitingSelection}"); break; }
                    turnsMq++;
                    board.DevRoll();
                    yield return WaitFor(() => !board.DevDiceRolling, 5f, "주사위 멈춤");
                    var goal = order[target].Coordinate;
                    board.DevMoveTowardAvoiding(new[] { goal });
                    if (_walkShots < 2 && args.Contains("-autoplay-move-shot")) // 11/13 N9 걷기 모션 확인: 걷는 도중 사진
                    {
                        float walkUntil = Time.time + 3f;
                        while (Time.time < walkUntil && !board.DevMoving) yield return null;
                        if (board.DevMoving) { yield return new WaitForSeconds(0.35f); yield return Shot($"이동_걷기_{_walkShots++}"); }
                    }
                    if (_fadeShots < 3 && args.Contains("-autoplay-move-shot")) // 11/12 기획 검수 자료용: 건물이 반투명해진 순간 사진
                    {
                        float fadeUntil = Time.time + 4f;
                        while (Time.time < fadeUntil && Fantasia.Board.BoardOccluderFade.DevFadedCount == 0 && !board.DevIsIdle) yield return null;
                        if (Fantasia.Board.BoardOccluderFade.DevFadedCount > 0) { yield return new WaitForSeconds(0.3f); yield return Shot($"이동_반투명_{_fadeShots++}"); }
                    }
                    yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen || board.DevAwaitingSelection || ConfirmPanel.IsOpen || EventPanel.IsOpen, 15f, "이동");
                    if (!board.DevIsIdle && !EncounterPanel.IsOpen && !EventPanel.IsOpen) Note($"  상태: 이동 {board.DevMoving} 선택대기 {board.DevAwaitingSelection} 대화 {DialoguePanel.IsOpen} 확인 {ConfirmPanel.IsOpen} 이벤트 {EventPanel.IsOpen} 상점 {Fantasia.UI.ShopPanel.IsOpen} 가방 {InventoryPanel.IsOpen} 명소 {LandmarkInfoPanel.IsOpen} 사건 {CaseLogPanel.IsOpen} 위치 {board.DevPosition}");
                    if (ConfirmPanel.IsOpen) ConfirmPanel.DevChoose(false);
                    if (board.DevPosition.Equals(goal)) { reached++; target++; stuck = 0; }
                    else if (++stuck >= 4) { target++; stuck = 0; } // 4턴 동안 못 가면 다음 목표
                }
                Note($"이동 QA: {turnsMq}턴 · 목표 도착 {reached}/{order.Count} · 멈춤 {mq.Stops}번");
                Qa(mq.MovingMs.Count > 0, mq.Summary(out bool frameOk));
                Qa(frameOk, "이동 중 33ms 넘는 프레임 1% 이하");
                Qa(mq.LateFade == 0, $"말을 가리는 구조물이 0.35초 안에 반투명 (늦은 구조물 {mq.LateFade}개)");
                Qa(mq.StuckFade == 0, $"안 가리는데 반투명으로 남은 구조물 없음 ({mq.StuckFade}개)");
                Qa(mq.TokenInside == 0, $"멈춘 자리가 구조물 안 아님 ({mq.TokenInside}번)");
                Qa(mq.CamInside == 0, $"카메라가 구조물 안에 안 들어감 ({mq.CamInside}프레임)");
                Qa(mq.MaxYawStep < 90f, $"카메라가 빠르게 안 돎 (최대 초당 {mq.MaxYawStep:0}°, 기준 90°)");
                Note($"  반투명이 생긴 프레임 {mq.FadedFrames}개");
                Note("자동 플레이 완료");
                yield break;
            }

            // ── 이벤트 칸 점검 (-autoplay-event): 점술마녀(M02) 회복 카드 → 분수대(S10) ──
            if (args.Contains("-autoplay-event"))
            {
                var evParty = BoardSession.Instance.Party;
                evParty.Members[1].TakeDamage(60);
                board.DevArriveAt("M02");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "점술마녀 창");
                yield return new WaitForSeconds(0.4f);
                yield return Shot("event_witch");
                EventPanel.DevPickCard(1); // 회복
                EventPanel.DevPickTarget(1);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("event_witch_selected");
                int evCent = evParty.Cent, evHp = evParty.Members[1].CurrentHP;
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(1.2f);
                yield return Shot("event_witch_result");
                Note($"점술마녀 회복 → Cent {evCent} → {evParty.Cent} · 역병 의사 HP {evHp} → {evParty.Members[1].CurrentHP}");
                EventPanel.DevLeave();
                yield return WaitFor(() => board.DevIsIdle || board.DevAwaitingSelection, 15f, "점술 뒤 진행");
                Note($"점술 뒤: 추가 이동 선택 {board.DevAwaitingSelection}");

                // 같은 칸 재방문 → 3라운드 대기 표시
                yield return WaitFor(() => board.DevIsIdle, 15f, "턴 진행");
                yield return WaitFor(() => board.DevIsIdle && !board.DevAwaitingSelection, 15f, "대기");
                board.DevArriveAt("M02");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "점술마녀 재방문");
                yield return new WaitForSeconds(0.3f);
                yield return Shot("event_witch_cooldown");
                EventPanel.DevLeave();
                yield return WaitFor(() => !EventPanel.IsOpen, 3f, "닫기");
                yield return WaitFor(() => board.DevIsIdle || board.DevAwaitingSelection, 15f, "진행");

                board.DevArriveAt("S10");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "분수대 창");
                EventPanel.DevPickTarget(2);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("event_fountain");
                int lp = evParty.Members[2].CurrentLP;
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(1.2f);
                yield return Shot("event_fountain_result");
                Note($"분수대 → 저격수 LP {lp} → {evParty.Members[2].CurrentLP}");
                EventPanel.DevLeave();
                yield return new WaitForSeconds(0.3f);
                board.DevArriveAt("S10");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "분수대 다시");
                int lpAgain = evParty.Members[2].CurrentLP;
                EventPanel.DevPickTarget(2);
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(0.5f);
                Note($"분수대 재방문: LP {lpAgain} → {evParty.Members[2].CurrentLP} (늘면 안 됨)");
                EventPanel.DevLeave();

                // 십자가 석상 (배치 칸 기획 대기 — 베르사유 이벤트 칸 A15·A21을 석상으로 보고 점검)
                var doc = evParty.Members[1];
                int maxFocus0 = doc.MaxFocus;
                board.DevOpenStatueAt("A15");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "석상 창");
                EventPanel.DevPickPrayer(0);   // 집중의 기도
                EventPanel.DevPickTarget(1);   // 역병 의사
                yield return new WaitForSeconds(0.3f);
                yield return Shot("statue_select");
                EventPanel.DevLeave();         // 취소 → 횟수 안 씀
                yield return new WaitForSeconds(0.3f);
                board.DevOpenStatueAt("A15");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "석상 다시");
                EventPanel.DevPickPrayer(0);
                EventPanel.DevPickTarget(1);
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(1.2f);
                yield return Shot("statue_result");
                Note($"석상 A15 (취소 후 다시): {doc.Job.DisplayName} {Fantasia.Data.PrayerRules.Name(doc.Prayer)} · 최대 집중력 {maxFocus0} → {doc.MaxFocus}");
                EventPanel.DevLeave();
                yield return new WaitForSeconds(0.3f);
                board.DevOpenStatueAt("A15");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "석상 재방문");
                EventPanel.DevPickPrayer(2);
                EventPanel.DevPickTarget(1);
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(0.5f);
                Note($"석상 A15 재방문: {Fantasia.Data.PrayerRules.Name(doc.Prayer)} (사용한 석상은 그대로여야 함)");
                EventPanel.DevLeave();
                yield return new WaitForSeconds(0.3f);
                board.DevOpenStatueAt("A21");
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "다른 석상");
                EventPanel.DevPickPrayer(2);   // 생명의 기도로 교체
                EventPanel.DevPickTarget(1);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("statue_replace");
                int hp0 = doc.MaxHP;
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(1.2f);
                Note($"석상 A21 교체: {Fantasia.Data.PrayerRules.Name(doc.Prayer)} · 최대 집중력 {doc.MaxFocus} · 최대 HP {hp0} → {doc.MaxHP}");
                EventPanel.DevLeave();
                yield return new WaitForSeconds(0.3f);
                board.DevLoadLastSave();
                yield return new WaitForSeconds(2f);
                var loaded = BoardSession.Instance.Party.Members[1];
                Note($"불러오기 뒤: {loaded.Job.DisplayName} {Fantasia.Data.PrayerRules.Name(loaded.Prayer)} · 최대 HP {loaded.MaxHP} · 분수대 사용 {(BoardSession.Instance.UsedMapTiles.Count)}칸 기록");
                Note("자동 플레이 완료");
                yield break;
            }

            // ── D27 저장 점검 (-autoplay-save): 상점에서 사고 숙박 → 다음 아군 턴 자동 저장 → 값 바꾸기 → 불러오기 → 비교 ──
            if (args.Contains("-autoplay-save"))
            {
                var saveParty = BoardSession.Instance.Party;
                board.DevOpenShop();
                yield return WaitFor(() => ShopPanel.IsOpen, 3f, "상점 열기");
                ShopPanel.Instance.DevSelectRow(1);
                ShopPanel.Instance.DevAct(); // 생명의 영약 1개
                saveParty.AddCent(100);
                int saveRound = board.DevWorldRound;
                ShopPanel.Instance.DevSelectTab(3); ShopPanel.Instance.DevSelectRow(0);
                ShopPanel.Instance.DevAct(); // 숙박 → 아군 턴 종료 → 적 턴 → 다음 아군 턴 시작에 자동 저장
                yield return WaitFor(() => board.DevWorldRound > saveRound && board.DevIsIdle, 10f, "숙박 후 턴 진행");
                yield return new WaitForSeconds(0.5f);
                var s = BoardSession.Instance;
                var london = Fantasia.Data.ItemCatalog.Current.ShopAtHub("S05");
                var elixirEntry = london.Entries.First(x => x.DisplayName == "생명의 영약");
                string Snapshot() => $"라운드 {s.World.WorldRound} · 위치 {board.DevPosition} · Cent {s.Party.Cent} · G {s.BloodTrail} · S {s.SoulHarvest} · " +
                                     $"HP {string.Join("/", s.Party.Members.Select(m => m.CurrentHP))} · 영약 {s.Party.Inventory.Consumables.Sum(c => c.Count)}개 보유 · 상점 영약 재고 {Fantasia.Items.ShopRules.StockOf(s.Shop, london, elixirEntry)}";
                string saved = Snapshot();
                Note($"저장 파일 {(SaveGame.HasSave ? "있음" : "없음")}: {SaveGame.SavePath}");
                Note("저장 시점: " + saved);
                // 저장 뒤에 진행이 바뀐 것처럼
                s.Party.AddCent(999);
                s.BloodTrail = 77;
                s.Party.Members[0].TakeDamage(30);
                Note("바꾼 뒤: " + Snapshot());
                var oldBoard = board;
                board.DevLoadLastSave();
                yield return WaitFor(() => { var b = FindFirstObjectByType<BoardTestController>(); return b != null && b != oldBoard && b.DevIsIdle; }, 10f, "불러오기 후 보드");
                board = FindFirstObjectByType<BoardTestController>();
                yield return new WaitForSeconds(0.8f);
                string loaded = Snapshot();
                Note("불러온 뒤: " + loaded);
                Note(loaded == saved ? "저장 점검: 일치" : "저장 점검: 불일치!");
                yield return Shot("save_after_load");
                Note("자동 플레이 완료");
                yield break;
            }

            // ── 기도 전투 점검 (-autoplay-prayer): 명탐정 피·역병 의사 부·저격수 그림자 ──
            // 1) P04 기습 실패 강제 → 그림자 면역(정상 속도순) · 부 Cent +20% (40 → 48)
            // 2) 아벨 모로(인간형) → 피의 기도 피해 ×1.1
            // 3) HP 1로 N15 → 전멸이면 부의 기도 약탈 면역
            if (args.Contains("-autoplay-prayer"))
            {
                var pp = BoardSession.Instance.Party;
                pp.Members[0].SetPrayer(Fantasia.Data.PrayerKind.Blood);
                pp.Members[1].SetPrayer(Fantasia.Data.PrayerKind.Wealth);
                pp.Members[2].SetPrayer(Fantasia.Data.PrayerKind.Shadow);
                Note($"기도: {string.Join(", ", pp.Members.Select(m => $"{m.Job.DisplayName} {Fantasia.Data.PrayerRules.Name(m.Prayer)}"))}");

                BoardTestController.DevForceOpening = (int)Fantasia.World.EncounterOutcome.AmbushFailure;
                int cent = pp.Cent;
                yield return PrayerFight("P04", "1) P04 기습 실패 + 그림자·부");
                Note($"1) Cent {cent} → {BoardSession.Instance.Party.Cent} (확정 보상 40 × 1.2 = 48 기대)");
                BoardTestController.DevForceOpening = -1;

                yield return PrayerFight("M11", "2) 아벨 모로 + 피의 기도");

                yield return PrayerFight("N15", "3) 그림자 사냥개 (망령형 — 피의 기도 미적용)");

                // 4) 전투 시작 직후 아군 전원 쓰러뜨려 전멸 → 부의 기도 있으면 약탈 0
                void Wipe()
                {
                    foreach (var m in BoardSession.Instance.Party.AliveMembers.ToList()) m.TakeDamage(m.CurrentHP);
                    FindFirstObjectByType<BattleController>()?.DevCheckEnd();
                }
                cent = BoardSession.Instance.Party.Cent;
                yield return PrayerFight("Q01", "4) 전멸 + 부의 기도", Wipe);
                Note($"4) Cent {cent} → {BoardSession.Instance.Party.Cent} (부의 기도 — 그대로여야 함)");
                // 5) 대조: 부의 기도 없이 전멸 → 약탈 발생
                BoardSession.Instance.Party.Members[1].SetPrayer(Fantasia.Data.PrayerKind.None);
                cent = BoardSession.Instance.Party.Cent;
                yield return PrayerFight("U01", "5) 전멸 + 기도 없음 (대조)", Wipe);
                Note($"5) Cent {cent} → {BoardSession.Instance.Party.Cent} (약탈로 줄어야 함)");
                Note("자동 플레이 완료");
                yield break;
            }

            // ── 회복·보물 칸 점검 (-autoplay-tiles): 가득 찬 파티 → 회복 칸 안 씀, 다친 뒤 [지나가기] → [이용], 보물 J06 ──
            if (args.Contains("-autoplay-tiles"))
            {
                // 11/13 보드 장식 가까이 (거점 둘레 시장 구역)
                var rig = Camera.main != null ? Camera.main.GetComponent<BoardCameraRig>() : null;
                if (rig != null) { float d0 = rig.Distance; rig.Distance = rig.MinDistance + 1f; yield return new WaitForSeconds(2f); yield return Shot("board_close"); rig.Distance = d0; yield return new WaitForSeconds(1.5f); }
                Note($"보드 장식: 가로등 장면 {HexBoard.DevLampScenes}곳 · 건물 밑동 {HexBoard.DevFoundations}곳 · {HexBoard.DevYardInfo}");
                // 11/14 팀장 1안 런던 안개: 멀리서 보면 안 열린 지역·지도 바깥이 안개로 가려짐
                Note($"런던 안개: 숨긴 칸 {HexBoard.DevHiddenTiles} · 안개 층 {HexBoard.DevFogLayers} · 안개 경계 가로등 {HexBoard.DevEdgeLamps}");
                Qa(HexBoard.DevEdgeLamps >= 3, "런던 안개: 경계 길가 가로등 3개 이상");
                Qa(HexBoard.DevHiddenTiles > 100 && HexBoard.DevFogLayers == 3, "런던 안개: 안 열린 지역 칸 숨김 + 안개 층 3장");
                if (rig != null) { float d2 = rig.Distance; rig.Distance = rig.MaxDistance; yield return new WaitForSeconds(2.5f); yield return Shot("board_far_fog"); rig.Distance = d2; yield return new WaitForSeconds(1.5f); }
                var hb = FindFirstObjectByType<HexBoard>();
                if (rig != null && hb != null)
                {
                    // 전체 내려다보기 (점검용 사진만 — 게임 카메라는 이렇게 못 감)
                    var cam = Camera.main; rig.enabled = false;
                    var vb = hb.VisibleBounds; var c = vb.center; float span = Mathf.Max(vb.size.x, vb.size.z);
                    cam.transform.position = c + new Vector3(0f, span * 0.95f, -span * 0.55f); cam.transform.LookAt(c);
                    yield return new WaitForSeconds(1.5f); yield return Shot("board_overview_fog");
                    if (Camera.main != null) Note("마당 집 화면 자리(전체 사진): " + string.Join(" ", HexBoard.DevYardTiles.Take(30).Select(p => { var s = Camera.main.WorldToScreenPoint(p); return $"({s.x:0},{Screen.height - s.y:0})"; })));
                    cam.transform.position = c + new Vector3(vb.extents.x * 0.9f, 9f, -vb.extents.z * 0.2f - 9f); cam.transform.LookAt(c + new Vector3(vb.extents.x * 1.1f, 0f, 0f));
                    yield return new WaitForSeconds(1.5f); yield return Shot("board_edge_fog");
                    // 11/14 팀장: 돌담 — 게임 시점(지금 카메라 각도·11m)으로 파티에서 가까운 돌담 2곳
                    Note($"돌담 조각 {HexBoard.DevWallSegments} · 돌담 칸 {HexBoard.DevWallSpots.Count}");
                    Qa(HexBoard.DevWallSegments > 0, "돌담: 길 쪽 모서리를 따라 세움");
                    var tokenPos = board.transform.position; var tk = GameObject.Find("PartyToken"); if (tk != null) tokenPos = tk.transform.position;
                    var spots = HexBoard.DevWallSpots.OrderBy(p => (p - (rig.Target != null ? rig.Target.position : tokenPos)).sqrMagnitude).Take(3).ToList();
                    for (int wi = 0; wi < spots.Count; wi++)
                    {
                        cam.transform.rotation = Quaternion.Euler(40f, 0f, 0f); cam.transform.position = spots[wi] - cam.transform.forward * 11f;
                        yield return new WaitForSeconds(0.8f); yield return Shot("board_wall_" + wi);
                    }
                    rig.enabled = true; rig.SnapToTarget(); yield return new WaitForSeconds(1f);
                }
                // 11/14 기획 Q4 야영 텐트: 서고 → 다음 이동 시작에 치워짐
                // 야영은 거점 밖 안전 칸에서 씀 → 먼저 조용한 길 칸으로 한 번 이동
                yield return WaitFor(() => board.DevIsIdle, 10f, "굴림 대기");
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 6f, "칸 선택");
                if (board.DevAwaitingSelection) board.DevMoveToQuietTile();
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "이동");
                if (EncounterPanel.IsOpen) EncounterPanel.DevChoose(3);
                yield return new WaitForSeconds(1f);
                // 실제 사용 경로: 가방에서 안개 야영 세트 사용 → 아군 턴 종료 신호 → 텐트
                var campDef = Fantasia.Data.ItemCatalog.Current != null ? Fantasia.Data.ItemCatalog.Current.Consumable("consumable.fog_camp_set") : null;
                if (campDef != null) { BoardSession.Instance.Party.Inventory.AddConsumable(campDef); Note("야영 세트 사용: " + InventoryPanel.UseWorldItem(campDef, BoardSession.Instance.Party.AliveMembers.First())); }
                else { Note("야영 세트 데이터 없음 → 직접 텐트"); board.DevPitCampTent(); }
                yield return new WaitForSeconds(0.5f);
                Qa(board.DevTentVisible, "야영 텐트: 야영 성공 뒤 지금 칸에 섬");
                if (rig != null) { float d1 = rig.Distance; rig.Distance = rig.MinDistance; yield return new WaitForSeconds(2f); yield return Shot("board_camp_tent"); rig.Distance = d1; yield return new WaitForSeconds(1f); }
                yield return WaitFor(() => board.DevIsIdle && BoardSession.Instance.World.Phase == Fantasia.World.WorldPhase.AllyTurn, 40f, "적 턴 지나 다음 아군 턴");
                Qa(board.DevTentVisible, "야영 텐트: 적 턴이 지나도 이동 전까지 남아 있음");
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 6f, "칸 선택");
                if (board.DevAwaitingSelection) { board.DevMoveToQuietTile(); yield return new WaitForSeconds(0.3f); }
                Qa(!board.DevTentVisible, "야영 텐트: 다음 이동이 시작되면 치워짐");
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "이동");
                if (EncounterPanel.IsOpen) EncounterPanel.DevChoose(3); // 후퇴
                // 11/13 보드 가로등 밤 확인: 저녁(3R)·새벽(4R) 사진 뒤 아침(5R)으로
                var clock = BoardSession.Instance.World;
                for (int r = clock.WorldRound; r < 3; r++) { clock.EndAllyTurn(); clock.EndEnemyTurn(); }
                yield return new WaitForSeconds(3f); yield return Shot("board_evening");
                // 11/14 팀장 3차: 저녁 안개 가장자리 — 경계 가로등 불빛이 안개에 번지는 모습
                if (rig != null && hb != null)
                {
                    var cam2 = Camera.main; rig.enabled = false; var vb2 = hb.VisibleBounds; var c2 = vb2.center;
                    cam2.transform.position = c2 + new Vector3(vb2.extents.x * 0.9f, 9f, -vb2.extents.z * 0.2f - 9f); cam2.transform.LookAt(c2 + new Vector3(vb2.extents.x * 1.1f, 0f, 0f));
                    yield return new WaitForSeconds(1.5f); yield return Shot("board_evening_edge_fog");
                    foreach (var hr in HexBoard.DevHalos.Take(3)) if (hr != null) Note($"번짐 {hr.transform.position} 켜짐 {hr.gameObject.activeInHierarchy} 보임 {hr.isVisible} 색 {hr.sharedMaterial.GetColor("_TintColor")} 크기 {hr.transform.lossyScale.x:F2} 셰이더 {hr.sharedMaterial.shader.name}");
                    cam2.transform.position = c2 + new Vector3(0f, Mathf.Max(vb2.size.x, vb2.size.z) * 0.95f, -Mathf.Max(vb2.size.x, vb2.size.z) * 0.55f); cam2.transform.LookAt(c2);
                    yield return new WaitForSeconds(1f); yield return Shot("board_evening_overview_fog");
                    rig.enabled = true; rig.SnapToTarget(); yield return new WaitForSeconds(1f);
                }
                clock.EndAllyTurn(); clock.EndEnemyTurn();
                yield return new WaitForSeconds(3f); yield return Shot("board_dawn");
                Note($"보드 가로등 장면 {HexBoard.DevLampScenes}곳 · 가로등: {Fantasia.Combat.StreetLampGlow.DevCount}개 · 켜짐 {Fantasia.Combat.StreetLampGlow.DevLitCount} · 진짜 빛 {Fantasia.Combat.StreetLampGlow.DevRealLights}");
                clock.EndAllyTurn(); clock.EndEnemyTurn();
                yield return new WaitForSeconds(2.5f);
                var tp = BoardSession.Instance.Party;
                string State() => string.Join(", ", tp.AliveMembers.Select(m => $"{m.Job.DisplayName} HP {m.CurrentHP}/{m.MaxHP} MP {m.CurrentMP}/{m.MaxMP} 집중 {m.CurrentFocus}"));
                board.DevArriveAt("S16");
                yield return new WaitForSeconds(1f);
                Note($"회복 칸 S16 (가득 참): 창 {(ConfirmPanel.IsOpen ? "열림" : "안 열림")} · {board.DevResultMessage}");
                foreach (var m in tp.AliveMembers) { m.TakeDamage(m.MaxHP / 2); m.TrySpendMP(m.MaxMP / 2); m.TrySpendFocus(2); }
                Note($"피해 뒤: {State()}");
                board.DevArriveAt("S16");
                yield return WaitFor(() => ConfirmPanel.IsOpen, 5f, "회복 칸 선택");
                yield return Shot("recovery_offer");
                ConfirmPanel.DevChoose(false);
                yield return new WaitForSeconds(0.5f);
                Note($"[지나가기]: {board.DevResultMessage}");
                board.DevArriveAt("S16");
                yield return WaitFor(() => ConfirmPanel.IsOpen, 5f, "회복 칸 다시");
                ConfirmPanel.DevChoose(true);
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "우물 선택 창");
                EventPanel.DevPickTarget(1);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("recovery_pick");
                EventPanel.DevLeave(); // 취소 → 횟수 안 씀
                yield return new WaitForSeconds(0.5f);
                Note($"[이용] 뒤 취소: {board.DevResultMessage} → {State()}");
                board.DevArriveAt("S16");
                yield return WaitFor(() => ConfirmPanel.IsOpen, 5f, "회복 칸 세 번째");
                ConfirmPanel.DevChoose(true);
                yield return WaitFor(() => EventPanel.IsOpen, 5f, "우물 선택 창 다시");
                EventPanel.DevPickTarget(1);
                EventPanel.DevConfirm();
                yield return new WaitForSeconds(1.2f);
                EventPanel.DevLeave();
                yield return new WaitForSeconds(0.5f);
                Note($"[회복] 역병 의사: {board.DevResultMessage} → {State()}");
                yield return Shot("recovery_used");
                board.DevArriveAt("S16");
                yield return new WaitForSeconds(1f);
                Note($"다시 도착: 창 {(ConfirmPanel.IsOpen ? "열림" : "안 열림")}");
                int cent = tp.Cent;
                board.DevArriveAt("J06");
                yield return new WaitForSeconds(1f);
                yield return WaitFor(() => TreasurePanel.DevButtonVisible, 4f, "보물 창");
                Qa(TreasurePanel.IsOpen, $"보물 칸 도착 → 보상 창 ({TreasurePanel.DevSummary})");
                yield return Shot("treasure_panel");
                TreasurePanel.DevClose();
                Note($"보물 J06: {board.DevResultMessage} · Cent {cent} → {tp.Cent}");
                yield return Shot("treasure_j06");
                Note("자동 플레이 완료");
                yield break;
            }

            // ── D20 상점 점검 (-autoplay-shop): 런던 거점에서 탭별 화면·구입·판매·숙박 ──
            if (args.Contains("-autoplay-shop"))
            {
                var shopParty = BoardSession.Instance.Party;
                board.DevOpenShop();
                yield return WaitFor(() => ShopPanel.IsOpen, 3f, "상점 열기");
                var shopUi = ShopPanel.Instance;
                shopUi.DevSelectRow(1);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_supplies");
                int shopCent = shopParty.Cent;
                shopUi.DevAct(); shopUi.DevAct(); // 생명의 영약 2개
                Note($"상점: 생명의 영약 2개 구입 → Cent {shopCent} → {shopParty.Cent}");
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_after_buy");
                shopUi.DevSelectTab(1); shopUi.DevSelectRow(1);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_weapons");
                Note($"상점 무기 탭 {shopUi.DevRowCount}종 표시");
                shopUi.DevSelectTab(2);
                Note($"상점 방어구 탭 {shopUi.DevRowCount}종 표시");
                shopUi.DevSelectTab(5); shopUi.DevSelectRow(0);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_sell");
                shopCent = shopParty.Cent;
                shopUi.DevAct();
                Note($"상점: 1개 판매 → Cent {shopCent} → {shopParty.Cent}");
                shopUi.DevSelectTab(3); shopUi.DevSelectRow(0);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("shop_services");
                shopParty.AddCent(100); // 점검용: 숙박비(30×3) 확보
                int shopRound = board.DevWorldRound;
                shopUi.DevAct(); // 숙박 → 아군 턴 종료
                yield return WaitFor(() => board.DevWorldRound > shopRound && board.DevIsIdle, 10f, "숙박 후 턴 진행");
                Note($"상점: 숙박 이용 → 상점 닫힘 {!ShopPanel.IsOpen}, 월드 라운드 {shopRound} → {board.DevWorldRound}, Cent {shopParty.Cent}");
                yield return Shot("shop_after_inn");
                Note("자동 플레이 완료");
                yield break;
            }

            for (int turn = 1; turn <= 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen, 10f, "이동 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                if (turn == 1) { yield return new WaitForSeconds(0.35f); yield return Shot("board_dice_rolling"); }
                yield return WaitFor(() => !board.DevDiceRolling, 5f, "주사위 멈춤");
                Note($"보드 {turn}턴: 월드 라운드 {board.DevWorldRound} 아군 턴 — 주사위 굴림");
                if (turn == 1)
                {
                    // 11/12 주사위 개선 점검: 틀 안에서 굴러 들어옴 · 한 프레임에 크게 안 돎 · 1.3초 뒤 오른쪽으로 옮겨 칸을 안 가림
                    var dice = board.DevDice;
                    if (dice.DevVisible || dice.DevMaxStepDeg > 0f)
                    {
                        Qa(!dice.DevOutOfFrame, "주사위가 그림 틀 안에서만 굴러감 (위에서 갑자기 안 나옴)");
                        Qa(dice.DevMaxStepDeg < 22f, $"주사위 한 프레임 최대 회전 {dice.DevMaxStepDeg:0.0}° (22° 미만)");
                    }
                    yield return Shot("board_roll_highlight");
                    yield return WaitFor(() => board.DevDice.DevDock >= 1f, BoardDice.HoldSeconds + 2f, "주사위 오른쪽으로");
                    var c = dice.DevScreenCenter;
                    Qa(dice.DevDock >= 1f && c.x > Screen.width * 0.85f && c.y > Screen.height * 0.25f && c.y < Screen.height * 0.6f,
                        $"주사위가 {BoardDice.HoldSeconds}초 뒤 오른쪽으로 감 (화면 {c.x / Screen.width:0.00}, {c.y / Screen.height:0.00}) · \"{dice.DevLabel}\"");
                    yield return Shot("board_dice_docked");
                }
                bool camShot = turn == 1 && Environment.GetCommandLineArgs().Contains("-camera-preview");
                if (huntRun)
                {
                    // D24 점검: 피의 사냥 전에는 적 칸을 피해 다니고, 시작되면 사신 쪽으로
                    if (BoardSession.Instance.Hunt.Active)
                    {
                        if (!shotHunt) { shotHunt = true; yield return Shot("board_blood_hunt"); }
                        board.DevMoveTowardReaper();
                    }
                    else board.DevMoveAvoidingEncounters();
                }
                else board.DevMoveTowardEncounter(bossRun);
                if (camShot) { yield return new WaitForSeconds(0.9f); Note($"  (점검) 이동 중 반투명 구조물 {Fantasia.Board.BoardOccluderFade.DevFadedCount}개"); yield return Shot($"이동시점_{Fantasia.Board.BoardCameraRig.Preview}_이동중"); }
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen || board.DevAwaitingSelection || ConfirmPanel.IsOpen, 15f, "이동");
                if (ConfirmPanel.IsOpen)
                {
                    // 1막 보스 권장 레벨 경고 → 진입
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("boss_level_warning");
                    Note("보스 권장 레벨 경고 → 진입");
                    ConfirmPanel.DevChoose(true);
                    yield return WaitFor(() => EncounterPanel.IsOpen, 5f, "경고 뒤 조우 창");
                    break;
                }
                if (BoardSession.Instance.SoulHarvest >= Fantasia.World.BloodHuntRules.RedSkyThreshold && !shotSky)
                {
                    shotSky = true;
                    yield return new WaitForSeconds(3.5f);
                    yield return Shot("board_red_sky");
                }
                // 이벤트 칸 추가 이동이 생기면 그것도 인카운터 쪽으로.
                if (board.DevAwaitingSelection && !EncounterPanel.IsOpen)
                {
                    Note("  이벤트 칸 추가 이동");
                    board.DevMoveTowardEncounter(bossRun);
                    yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "추가 이동");
                }
            }
            if (!EncounterPanel.IsOpen) { Note("실패: 20턴 안에 인카운터에 도착하지 못함"); yield break; }

            // ── 2. 조우 창 → 기습 ──
            yield return new WaitForSeconds(0.6f);
            yield return Shot("encounter_panel");
            // 11/11 다른 종류 적 2마리면 두 모델 (점검용으로 섞어 그린 뒤 원래대로)
            EncounterPanel.DevPreviewMixed(true);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("encounter_two_kinds");
            // 11/12 연성 독거미 구도 점검 (혼자 · 쥐와 함께)
            EncounterPanel.DevPreviewGroup("연성독거미유체");
            yield return new WaitForSeconds(0.4f);
            yield return Shot("encounter_spider");
            EncounterPanel.DevPreviewGroup("역병쥐", "연성독거미유체");
            yield return new WaitForSeconds(0.4f);
            yield return Shot("encounter_three_kinds");
            Note($"조우 창 3종류 칸 수: {EncounterPanel.DevModelCount} (기대 3)");
            EncounterPanel.DevPreviewGroup("역병쥐", "연성독거미유체", "가고일유체");
            yield return new WaitForSeconds(0.4f);
            Note($"조우 창 4종류 칸 수: {EncounterPanel.DevModelCount} (기대 3)");
            EncounterPanel.DevPreviewMixed(false);
            yield return new WaitForSeconds(0.1f);
            Note("조우 창 열림 → 기습 선택");
            EncounterPanel.DevChoose(1);
            yield return new WaitForSeconds(0.55f);
            yield return Shot("encounter_ambush_roll");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "CombatTest", 10f, "전투 씬 전환");

            // ── 3. 전투 ──
            yield return RunBattle(shots: true);
            var battle = _lastBattle;
            if (battle == null || !battle.DevFinished) yield break;
            bool bossBattle = _lastBattleBoss;
            if (bossBattle && args.Contains("-autoplay-batch")) { yield return Shot("battle_result"); Note("자동 플레이 완료"); yield break; }

            // ── 4. 결과 → 보드 복귀 ──
            yield return new WaitForSeconds(0.8f);
            yield return Shot("battle_result");
            battle.DevContinue();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "BoardTest" && FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 복귀");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("board_after_battle");
            NoteParty("보드 복귀 후");
            var hexBoard = FindAnyObjectByType<HexBoard>();
            if (hexBoard != null && hexBoard.UsesWorldMap)
            {
                var exit = hexBoard.Map.TileAt("K12").Coord;
                Note($"런던 출구 K12: {(hexBoard.IsExitOpen(exit) ? "열림" : "잠김")} · 퀘스트 물건: {string.Join(", ", BoardSession.Instance.KeyItems)}");

                // 1막 마무리: 사건 기록(증거품) → 런던 거점에서 제임스 보고 → 1막 완료·K12 해금
                if (ActOneStory.NeedsReport(BoardSession.Instance))
                {
                    Note($"진행 퀘스트: {string.Join(", ", BoardSession.Instance.Quests.Select(q => q.Title))}");
                    CaseLogPanel.DevOpen(2);
                    yield return new WaitForSeconds(0.4f);
                    yield return Shot("case_log_evidence");
                    CaseLogPanel.DevClose();
                    board = FindFirstObjectByType<BoardTestController>();
                    yield return WaitFor(() => board.DevIsIdle, 15f, "보고 전 대기");
                    board.DevReportToJames();
                    yield return WaitFor(() => DialoguePanel.IsOpen, 3f, "제임스 대화");
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("james_report");
                    while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSeconds(0.2f); }
                    yield return new WaitForSeconds(0.5f);
                    Note($"제임스 보고 → K12 {(hexBoard.IsExitOpen(exit) ? "열림" : "잠김")} · 회차 상태 {BoardSession.Instance.Run} · 진행: {string.Join(", ", BoardSession.Instance.Quests.Select(q => q.Title))}");
                    // 1막 클리어 결과 화면 → 메인 메뉴 → 이어하기 = 결과 화면
                    yield return WaitFor(() => ActClearPanel.IsOpen, 3f, "클리어 결과 화면");
                    yield return new WaitForSecondsRealtime(2.2f);
                    yield return Shot("act1_clear");
                    ActClearPanel.DevClose();
                    yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
                    yield return new WaitForSeconds(1f);
                    var clearMenu = FindFirstObjectByType<MainMenuController>();
                    Note($"클리어 뒤 메뉴: {clearMenu.DevContinueInfo.Replace("\n", " / ")}");
                    yield return Shot("menu_after_clear");
                    clearMenu.OnContinue();
                    yield return new WaitForSeconds(0.5f);
                    Note($"클리어 뒤 이어하기 → 결과 화면 {(ActClearPanel.IsOpen ? "열림" : "안 열림")} · Cent {BoardSession.Instance.Party?.Cent}");
                    yield return Shot("continue_after_clear");
                    Note("자동 플레이 완료");
                    yield break;
                }
            }

            // ── 5. 인벤토리·장비 창 (D17) ──
            var party = BoardSession.Instance.Party;
            Note($"인벤토리: 장비 {party.Inventory.Gear.Count}개, 소모품 {string.Join(", ", party.Inventory.Consumables.Select(s => $"{s.Definition.DisplayName}×{s.Count}"))}, 보관함 {BoardSession.Instance.PendingLoot.Count}");
            if (party.Inventory.Gear.Count == 0 && Fantasia.Data.ItemCatalog.Current != null)
            {
                // 화면 확인용: 전리품에서 장비가 안 나왔으면 희귀 코트 하나 넣어 본다
                var coat = Fantasia.Data.ItemCatalog.Current.Armors.FirstOrDefault();
                if (coat != null) party.Inventory.AddGear(new Fantasia.Data.GearItem(coat, Fantasia.Data.EquipmentGrade.Rare));
                Note("  (점검용 희귀 장비 1개 추가)");
            }
            InventoryPanel.Open(party, party.Members[0]);
            InventoryPanel.Instance.DevSelectFirst(0);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("inventory_gear_compare");
            InventoryPanel.Instance.DevEquipSelected();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("inventory_after_equip");
            Note($"  장착 후 {party.Members[0].Job.DisplayName}: 몸통 {party.Members[0].GetEquipped(Fantasia.Data.GearSlot.Body)?.DisplayName ?? "없음"} · 물리 방어 {party.Members[0].FinalPhysicalDefense} · 최대 HP {party.Members[0].MaxHP}");
            InventoryPanel.Instance.DevSelectFirst(1);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("inventory_consumables");
            InventoryPanel.Close();
            Note("자동 플레이 완료");
        }

        // 사람이 할 법한 스킬 사용 — 쓸 수 있는(비용·쿨다운 OK) 스킬 중 상황에 맞는 것 하나. 없으면 (null, null).
        // 전투 1회 자동 진행 (일반·보스 공통). shots = 화면 캡처 여부. 결과는 _lastBattle·_lastBattleBoss
        private BattleController _lastBattle;
        private bool _lastBattleBoss;

        // 기도 전투 점검: 그 칸에 도착 → 대화·경고·조우(싸우기) 처리 → 전투 → 보드(또는 메뉴)로
        private IEnumerator PrayerFight(string label, string title, Action beforeBattle = null)
        {
            var board = FindFirstObjectByType<BoardTestController>();
            if (board == null) { Note($"{title}: 보드 없음"); yield break; }
            Note($"── {title} ──");
            board.DevArriveAt(label);
            float until = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != "CombatTest" && Time.realtimeSinceStartup < until)
            {
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
                if (ConfirmPanel.IsOpen) ConfirmPanel.DevChoose(true);
                if (EncounterPanel.IsOpen) EncounterPanel.DevChoose(0);
                yield return new WaitForSeconds(0.3f);
            }
            if (SceneManager.GetActiveScene().name != "CombatTest") { Note($"{title}: 전투로 안 넘어감"); yield break; }
            if (beforeBattle != null) { yield return new WaitForSeconds(1.5f); beforeBattle(); }
            yield return RunBattle(shots: false);
            var b = _lastBattle;
            if (b == null || !b.DevFinished) { Note($"{title}: 전투가 끝나지 않음"); yield break; }
            Note($"{title}: {(b.DevEnemies.All(e => !e.IsAlive) ? "승리" : "패배/이탈")}");
            yield return new WaitForSeconds(0.6f);
            b.DevContinue();
            yield return WaitFor(() => SceneManager.GetActiveScene().name != "CombatTest", 10f, "전투 뒤 씬 전환");
            yield return new WaitForSeconds(1f);
            until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && FindFirstObjectByType<BoardTestController>() is { } bd && !bd.DevIsIdle)
            {
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
                yield return new WaitForSeconds(0.2f);
            }
        }

        // 11/12 N6~N8 연결 확인 (첫 전투 첫 차례에 한 번): 아군 = 회피·승리, 아벨 모로 = 예고·봉합
        private bool _motionsChecked, _bossMotionsChecked, _cardPickChecked;
        private int _fadeShots, _walkShots;
        private void CheckMotionsWired(Fantasia.Combat.BattleController battle)
        {
            if (!_motionsChecked)
            {
                _motionsChecked = true;
                var allies = battle.DevAllies.Select(u => Fantasia.UI.CharacterMotion.Of(battle.DevModelOf(u))).Where(m => m != null).ToList();
                int ok = allies.Count(m => m.Has("Dodge") && m.Has("Victory"));
                Qa(allies.Count > 0 && ok == allies.Count, $"아군 회피·승리 동작 연결 ({ok}/{allies.Count})");
            }
            var boss = battle.DevEnemies.FirstOrDefault(e => e.Boss != null);
            if (boss != null && !_bossMotionsChecked)
            {
                _bossMotionsChecked = true;
                var bm = Fantasia.UI.CharacterMotion.Of(battle.DevModelOf(boss));
                Qa(bm != null && bm.Has("Announce") && bm.Has("SelfHeal"), "아벨 모로 예고·봉합 동작 연결");
            }
        }

        // 11/12 팀장 지적: 방패 표시가 캐릭터 이동을 따라갔다가 제자리로 바로 안 돌아옴.
        // 보호막을 준 뒤 6초 동안(적이 와서 때리고 돌아가는 동안) 매 프레임 끝에 "방패 ↔ 지금 머리 위" 거리를 잼.
        private IEnumerator SampleShieldMarks(Fantasia.Combat.BattleController battle)
        {
            float end = Time.time + 6f, worst = 0f;
            int frames = 0;
            while (Time.time < end && battle != null && !battle.DevFinished)
            {
                yield return new WaitForEndOfFrame();
                worst = Mathf.Max(worst, battle.DevShieldMarkError);
                frames++;
            }
            Qa(worst < 0.05f, $"방패 표시가 캐릭터를 따라감 (프레임 {frames}개 · 가장 크게 어긋남 {worst:0.000}m)");
        }

        private IEnumerator RunBattle(bool shots)
        {
            var args = Environment.GetCommandLineArgs();
            _lastBattle = null;
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투 관리자");
            var battle = FindFirstObjectByType<BattleController>();
            _lastBattle = battle;
            bool shotFirstTurn = false, shotRoll = false, shotEnemy = false, defended = false, shotSkillMenu = false, shotSkill = false, shotRevive = false, usedItem = false, defendDemo = false, sniperShield = false, shieldNoted = false, bossShield = false, bossShieldShot = false; int bossAbsorbBefore = 0; // defendDemo: 11/11 보호막 촬영
            bool shotBanner = false, shotIntro = false, shotTelegraph = false, focusZeroed = false;
            bool kit = args.Contains("-autoplay-kit"), forceInterrupt = args.Contains("-autoplay-interrupt"), focusZero = args.Contains("-autoplay-focus0"), noInterrupt = args.Contains("-autoplay-no-interrupt"); // 11/12: 봉합을 그냥 두고 자가 회복 동작(N6) 확인
            int downs = 0, healsUsed = 0, interrupts = 0;
            // 속도 점검 (-autoplay-ampoule): 역병 의사가 신속의 앰플을 자신에게 (효과가 끝나면 다시) — 다음 라운드 ↑ 화면 촬영
            bool ampouleRun = args.Contains("-autoplay-ampoule");
            int ampoulesUsed = 0, ampouleRound = -1;
            bool shotArrows = false;
            var wasDowned = new System.Collections.Generic.HashSet<BattleUnit>();
            int focusBefore = battle.DevAllies.Sum(a => a.Member.CurrentFocus);
            float deadline = Time.time + 180f;

            while (!battle.DevFinished && Time.time < deadline)
            {
                // 보스 등장 연출: 배너 → 전투 전 대화 (Space와 같은 다음 줄)
                if (!shotBanner && battle.DevHud.DevBannerVisible) { shotBanner = true; yield return new WaitForSeconds(0.3f); if (shots) yield return Shot("boss_banner"); }
                if (DialoguePanel.IsOpen)
                {
                    if (!shotIntro) { shotIntro = true; yield return new WaitForSeconds(0.3f); if (shots) yield return Shot("boss_intro_dialogue"); }
                    DialoguePanel.DevAdvance();
                    yield return new WaitForSeconds(0.15f);
                    continue;
                }
                foreach (var a in battle.DevAllies) if (a.Member.IsDowned && wasDowned.Add(a)) downs++; else if (!a.Member.IsDowned) wasDowned.Remove(a);
                if (defendDemo && !sniperShield && battle.DevCurrent != null && !battle.DevCurrent.IsAlly) { sniperShield = true; battle.DevShieldAll(Fantasia.Combat.AttackResolver.DefendShield); Note("  (점검) 적 차례 시작 — 아군 모두 보호막 8"); StartCoroutine(SampleShieldMarks(battle)); } // 11/11 보호막 흡수 확인
                // 11/12 팀장 확인: 아벨 모로가 보호막 8 낀 아군을 때리면 "보호막 -8" + 남은 피해만 숫자로
                if (!bossShield && battle.DevCurrent != null && battle.DevCurrent.Boss != null) { bossShield = true; bossAbsorbBefore = battle.DevAbsorbed; battle.DevShieldAll(Fantasia.Combat.AttackResolver.DefendShield); Note("  (점검) 아벨 모로 차례 — 아군 모두 보호막 8"); }
                if (bossShield && !bossShieldShot && battle.DevAbsorbed > bossAbsorbBefore) { bossShieldShot = true; Note($"  아벨 모로 → 보호막 막음 {battle.DevAbsorbed - bossAbsorbBefore} · HP 피해 {battle.DevLastHpDamage}"); yield return new WaitForSeconds(0.2f); if (shots) yield return Shot("boss_shield_hit"); }
                if (sniperShield && !shieldNoted && battle.DevAbsorbed > 0) { shieldNoted = true; Note($"  보호막 흡수 확인: 막은 피해 {battle.DevAbsorbed}"); yield return new WaitForSeconds(0.3f); if (shots) yield return Shot("battle_shield_absorbed"); }
                var telegraphBoss = battle.DevEnemies.FirstOrDefault(e => e.Boss != null && e.Boss.HasPending && e.IsAlive);
                if (telegraphBoss != null && focusZero && !focusZeroed)
                {
                    // 조건 C: 예고 시점에 아군 집중력 전원 0
                    focusZeroed = true;
                    foreach (var a in battle.DevAllies) a.Member.TrySpendFocus(a.Member.CurrentFocus);
                    Note("  (점검: 예고 시점 아군 집중력 전원 0)");
                }
                if (telegraphBoss != null && !shotTelegraph && battle.DevAwaitingPlayer)
                {
                    shotTelegraph = true;
                    yield return new WaitForSeconds(0.4f);
                    if (shots) yield return Shot("boss_telegraph");
                    Note($"  예고 UI: {battle.DevHud.DevTelegraphText(telegraphBoss)?.Replace("\n", " / ")} · 봉합 방해 버튼 {(battle.DevHud.DevInterruptVisible ? "보임" : "없음")}");
                }

                // 앰플 쓴 다음 라운드 시작: 타임라인 ↑/↓·속도 표시 촬영 (화살표는 2.5초만 보임)
                if (ampouleRun && !shotArrows && ampouleRound > 0 && battle.DevRound > ampouleRound && battle.DevAwaitingPlayer)
                {
                    shotArrows = true;
                    Note($"  [R{battle.DevRound}] 앰플 반영 순서: {string.Join(" > ", battle.DevAllies.Concat(battle.DevEnemies).Where(u => u.InBattle).OrderByDescending(u => u.Speed).Select(u => $"{u.DisplayName}({u.Speed})"))}");
                    if (shots) yield return Shot("battle_speed_arrows");
                }
                if (battle.DevAwaitingPlayer)
                {
                    yield return new WaitForSeconds(0.3f);
                    if (!shotFirstTurn) { shotFirstTurn = true; if (shots) yield return Shot("battle_first_turn"); CheckMotionsWired(battle); }
                    // 촬영용 (-autoplay-wipe): 아군 전원을 쓰러뜨려 패배·게임 오버 화면을 띄운다
                    if (Environment.GetCommandLineArgs().Contains("-autoplay-wipe"))
                    {
                        foreach (var m in BoardSession.Instance.Party.AliveMembers.ToList()) m.TakeDamage(m.CurrentHP);
                        battle.DevCheckEnd();
                        Note("촬영용: 아군 전원 쓰러뜨림");
                        continue;
                    }

                    var actor = battle.DevCurrent;
                    // 11/14 팀장 QA "카드 1~4번 칸을 눌러도 아이템이 안 써짐": 실제 클릭처럼 그 자리의 맨 위 UI를 찾아 누름
                    if (Environment.GetCommandLineArgs().Contains("-autoplay-quickslot") && !_quickProbed)
                    {
                        _quickProbed = true;
                        yield return ProbeQuickSlot(actor);
                        if (shots) yield return Shot("battle_quickslot_click");
                        if (!battle.DevAwaitingPlayer) continue;
                    }
                    // 스킬을 쓸 상황이면 스킬(D11), 아니면 기본 공격 — 집중력이 남으면 추가 집중을 켠다.
                    var (skill, target) = ChooseSkill(battle, actor);
                    var downed = battle.DevAllies.FirstOrDefault(a => a.Member.IsDowned && a.Member.CurrentLP > 0);
                    // 다친 동료가 있으면 회복 소모품 한 번 (D17) — 플레이테스트 준비물(-autoplay-kit)이면 HP 50% 아래마다
                    var potionTarget = battle.DevAllies.Where(a => a.InBattle).OrderBy(a => (float)a.CurrentHP / a.MaxHP).FirstOrDefault();
                    var potion = BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition)
                        .FirstOrDefault(d => d.Effect == Fantasia.Data.ConsumableEffect.RestoreHP);
                    var poisoned = battle.DevAllies.Where(a => a.InBattle).FirstOrDefault(a => a.Statuses.All.Any(s => s.Effect == Fantasia.Data.StatusEffectType.Poison && s.Stacks >= 2));
                    var antidote = BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition).FirstOrDefault(d => d.Id == Fantasia.Data.ConsumableIds.Antidote);
                    if (noInterrupt && telegraphBoss != null && skill != null && target == telegraphBoss) skill = null; // 저지 기술도 안 씀
                    bool breakerReady = skill != null && telegraphBoss != null && target == telegraphBoss && skill.Coefficient >= telegraphBoss.Boss.Pending.CancelMinCoefficient;
                    if (!noInterrupt && telegraphBoss != null && battle.DevHud.DevInterruptVisible && (forceInterrupt || !breakerReady))
                    {
                        // 봉합 방해: 저지 기술이 없거나(집중력·쿨다운) 점검으로 강제할 때
                        interrupts++;
                        Note($"  {actor.DisplayName} → 봉합 방해 (집중력 {actor.Member.CurrentFocus})");
                        battle.DevHud.DevPressInterrupt();
                        yield return new WaitForSeconds(0.5f);
                        if (shots) yield return Shot("boss_interrupt");
                    }
                    else if (ampouleRun && actor.Member.Job.Id == Fantasia.Data.JobId.PlagueDoctor
                             && !actor.Statuses.Has(Fantasia.Data.StatusEffectType.SpeedUp)
                             && BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition).FirstOrDefault(d => d.Id == "consumable.swift_ampoule") is { } ampoule)
                    {
                        ampoulesUsed++;
                        ampouleRound = battle.DevRound;
                        Note($"  [R{battle.DevRound}] 신속의 앰플 → {actor.DisplayName} (속도 {actor.Speed})");
                        battle.DevUseItem(ampoule, actor);
                        yield return new WaitForSeconds(0.5f);
                        Note($"    → 속도 {actor.Speed} · 이번 라운드 순서 그대로");
                        if (shots && ampoulesUsed == 1) yield return Shot("battle_ampoule_used");
                    }
                    else if (kit && antidote != null && poisoned != null && Fantasia.Items.ConsumableRules.CanUseOn(antidote, poisoned.Member, out _))
                    {
                        healsUsed++;
                        Note($"  해독제 → {poisoned.DisplayName}");
                        battle.DevUseItem(antidote, poisoned);
                    }
                    else if (kit && potion != null && potionTarget != null && (float)potionTarget.CurrentHP / potionTarget.MaxHP < 0.5f
                             && Fantasia.Items.ConsumableRules.CanUseOn(potion, potionTarget.Member, out _))
                    {
                        healsUsed++;
                        Note($"  {potion.DisplayName} → {potionTarget.DisplayName} (HP {potionTarget.CurrentHP}/{potionTarget.MaxHP})");
                        battle.DevUseItem(potion, potionTarget);
                    }
                    else if (battle.DevCanRevive && downed != null && !Environment.GetCommandLineArgs().Contains("-autoplay-no-revive"))
                    {
                        // 쓰러진 동료가 있으면 먼저 부활 (D21)
                        battle.DevRevive(downed);
                        if (!shotRevive)
                        {
                            shotRevive = true;
                            yield return new WaitForSeconds(0.4f);
                            if (shots) yield return Shot("battle_revive");
                        }
                    }
                    else if (!usedItem && potion != null && potionTarget != null && (float)potionTarget.CurrentHP / potionTarget.MaxHP < 0.8f
                             && Fantasia.Items.ConsumableRules.CanUseOn(potion, potionTarget.Member, out _))
                    {
                        usedItem = true;
                        healsUsed++;
                        battle.DevHud.DevOpenItems();
                        yield return new WaitForSeconds(0.2f);
                        if (shots) yield return Shot("battle_item_menu");
                        Note($"  소모품 사용: {potion.DisplayName} → {potionTarget.DisplayName} (HP {potionTarget.CurrentHP}/{potionTarget.MaxHP})");
                        if (!_cardPickChecked)
                        {
                            // 11/12 아군 대상은 아래 캐릭터 카드를 눌러 고름 (행동 창이 전장 모델을 가려서)
                            _cardPickChecked = true;
                            int countBefore = BoardSession.Instance.Party.Inventory.CountOf(potion);
                            int hpBefore = potionTarget.CurrentHP;
                            battle.DevPressItem(potion);
                            yield return null; yield return null;
                            int valid = PartyHudPanel.DevTargetPickValidCount;
                            Qa(battle.DevChoosingTarget && PartyHudPanel.DevTargetPickOn && valid >= 1, $"아군 대상 고르기 중 카드에 \"대상\" 덮개 (고를 수 있는 카드 {valid}장)");
                            if (shots) yield return Shot("battle_target_cards");
                            bool clicked = PartyHudPanel.DevClickTargetCard(potionTarget.Member);
                            yield return WaitFor(() => potionTarget.CurrentHP > hpBefore, 4f, "카드로 고른 아이템 효과");
                            Qa(clicked && BoardSession.Instance.Party.Inventory.CountOf(potion) == countBefore - 1 && potionTarget.CurrentHP > hpBefore,
                               $"카드를 눌러 {potionTarget.DisplayName}에게 {potion.DisplayName} 사용 (HP {hpBefore} → {potionTarget.CurrentHP})");
                            Qa(!PartyHudPanel.DevTargetPickOn, "고른 뒤 카드 덮개 사라짐");
                        }
                        else battle.DevUseItem(potion, potionTarget);
                        yield return new WaitForSeconds(0.4f);
                        if (shots) yield return Shot("battle_item_used");
                    }
                    else if (skill != null)
                    {
                        if (!shotSkillMenu)
                        {
                            shotSkillMenu = true;
                            battle.DevHud.DevOpenSkills();
                            yield return new WaitForSeconds(0.2f);
                            if (shots) yield return Shot("battle_skill_menu");
                            // 11/11 고른 스킬 빨간 테두리 → 같은 스킬 다시 눌러 취소 (대상 고르는 스킬일 때만)
                            if (shots && (skill.Target == Fantasia.Data.SkillTarget.SingleEnemy || skill.Target == Fantasia.Data.SkillTarget.SingleAlly))
                            {
                                battle.DevPressSkill(skill);
                                yield return new WaitForSeconds(0.2f);
                                yield return Shot("battle_skill_chosen");
                                Qa(battle.DevChoosingTarget, "스킬 고르면 대상 고르기");
                                battle.DevPressSkill(skill);
                                yield return new WaitForSeconds(0.2f);
                                yield return Shot("battle_skill_cancelled");
                                Qa(!battle.DevChoosingTarget && battle.DevAwaitingPlayer, "같은 스킬 다시 누르면 취소");
                                battle.DevPressSkill(skill);
                                battle.DevDefendButton();
                                Qa(!battle.DevChoosingTarget && battle.DevCurrent != actor, "대상 고르는 중 방어 = 고르던 스킬 취소 후 본인 방어");
                                yield return new WaitForSeconds(0.5f);
                                yield return Shot("battle_defend_shield");
                                defendDemo = true;
                                continue;
                            }
                        }
                        if (skill.Effect == Fantasia.Data.SkillEffect.Heal || skill.Effect == Fantasia.Data.SkillEffect.Cleanse) healsUsed++;
                        bool bossNow = battle.DevEnemies.Any(e => e.InBattle && e.Boss != null) && !(target != null && target.Boss != null && target.Boss.HasPending);
                        battle.DevUseSkill(skill, target, focus: actor.Member.CurrentFocus > skill.FocusCost + (bossNow ? 1 : 0) ? 1 : 0);
                        if (!shotSkill)
                        {
                            shotSkill = true;
                            yield return new WaitForSeconds(0.6f);
                            if (shots) yield return Shot($"battle_skill_{skill.Id.Split('.').Last()}");
                        }
                    }
                    else if (!defended && actor.Member.Job.Id == Fantasia.Data.JobId.PlagueDoctor && actor.Member.CurrentMP < 12)
                    {
                        defended = true;
                        battle.DevDefend();
                    }
                    else
                    {
                        // 보스전에서는 집중력 1을 저지용 기술 몫으로 남긴다
                        bool bossFight = battle.DevEnemies.Any(e => e.InBattle && e.Boss != null);
                        if (!actor.CanBasicAttack) battle.DevDefend(); // MP가 없어 기본 공격도 못 하면 방어
                        else battle.DevAttack(focus: actor.Member.CurrentFocus > (bossFight ? 1 : 0) ? 1 : 0);
                        if (!shotRoll)
                        {
                            shotRoll = true;
                            yield return new WaitForSeconds(0.5f);
                            if (shots) yield return Shot("battle_player_roll");
                            yield return new WaitForSeconds(1.2f);
                            if (shots) yield return Shot("battle_after_hit");
                        }
                    }
                }
                else if (!shotEnemy && battle.DevCurrent != null && !battle.DevCurrent.IsAlly)
                {
                    shotEnemy = true;
                    yield return new WaitForSeconds(0.9f);
                    if (shots) yield return Shot("battle_enemy_turn");
                }
                yield return null;
            }
            if (!battle.DevFinished) { Note("실패: 전투가 180초 안에 끝나지 않음"); if (shots) yield return Shot("battle_timeout"); yield break; }

            // 보스 처치 후 대화 → 보상 창
            bool bossBattle = battle.DevEnemies.Any(e => e.Boss != null);
            // 11/12 N8: 승리 동작(약 1.9초) 뒤에 대화·결과 창 → 둘 중 하나가 뜰 때까지
            bool wonBattle = battle.DevEnemies.All(e => !e.IsAlive);
            yield return WaitFor(() => DialoguePanel.IsOpen || battle.DevHud.DevResultButtonsVisible || battle.DevHud.DevResultDropPlaying, 6f, "승리 동작 뒤 대화·결과 창");
            if (wonBattle) { Fantasia.UI.CharacterMotion.DevPlayed.TryGetValue("Victory", out int vic); Qa(vic > 0, $"승리하면 아군 승리 동작 (재생 {vic}번)"); }
            Note($"  동작 재생: {string.Join(" · ", new[] { "Dodge", "Victory", "Announce", "SelfHeal", "Walk" }.Select(k => $"{k} {(Fantasia.UI.CharacterMotion.DevPlayed.TryGetValue(k, out int c) ? c : 0)}"))}");
            bool shotOutro = false;
            while (DialoguePanel.IsOpen)
            {
                if (!shotOutro) { shotOutro = true; if (shots) yield return Shot("boss_after_dialogue"); }
                DialoguePanel.DevAdvance();
                yield return new WaitForSeconds(0.15f);
            }
            if (bossBattle)
            {
                // 플레이테스트 기록 (Boss_Abel_v1.0 "개발팀에서 함께 남길 결과")
                bool won = battle.DevEnemies.All(e => !e.IsAlive);
                Note($"보스전 결과: {(won ? "승리" : "패배")} · 라운드 {battle.DevRound}{(ampouleRun ? $" · 앰플 {ampoulesUsed}" : "")} · 다운 {downs} · LP 소모 {battle.DevAllies.Sum(a => a.Member.Job.StartingLP - a.Member.CurrentLP)}"
                     + $" · 회복/해독 {healsUsed} · 봉합 방해 {interrupts} · 레벨 {string.Join("/", battle.DevAllies.Select(a => a.Member.Level))}"
                     + $" · 남은 HP {string.Join("/", battle.DevAllies.Select(a => $"{a.CurrentHP}"))} · 남은 집중력 {string.Join("/", battle.DevAllies.Select(a => a.Member.CurrentFocus))} (시작 합 {focusBefore})"
                     + $" · 시드 {Seed}");
            }
            _lastBattleBoss = bossBattle;
        }

        private static (Fantasia.Data.SkillDefinition, BattleUnit) ChooseSkill(BattleController battle, BattleUnit actor)
        {

            var enemies = battle.DevEnemies.Where(e => e.InBattle).ToList();
            var allies = battle.DevAllies.Where(a => a.InBattle).ToList();
            if (enemies.Count == 0) return (null, null);
            var evasive = enemies.FirstOrDefault(e => e.HasEvasionState);
            // 보스가 후속 행동을 예고했으면 1.2배 이상 공격 기술로 저지 (D14 금지된 봉합)
            var announcing = enemies.FirstOrDefault(e => e.Boss != null && e.Boss.HasPending);
            if (announcing != null)
            {
                var breaker = actor.ActiveSkills().FirstOrDefault(s => s.Effect == Fantasia.Data.SkillEffect.Attack && s.Target == Fantasia.Data.SkillTarget.SingleEnemy
                    && s.Coefficient >= announcing.Boss.Pending.CancelMinCoefficient && actor.CannotUseReason(s) == null);
                if (breaker != null) return (breaker, announcing);
            }
            // 예고가 곧 나올 보스(임계치 미발동, HP 75% 이하)가 있으면 저지용 기술은 아껴 둔다
            bool bossFight = enemies.Any(e => e.Boss != null);
            bool saveBreakers = enemies.Any(e => e.Boss != null && e.Enemy.Thresholds.Length > 0 && e.Boss.FiredThresholds.Count == 0
                                                 && e.CurrentHP <= e.MaxHP * 0.75f);
            Fantasia.Data.SkillDefinition Usable(string id) =>
                actor.ActiveSkills().FirstOrDefault(s => s.Id == id && actor.CannotUseReason(s) == null && !(saveBreakers && s.Coefficient >= 1.2f)
                    && !(bossFight && s.Coefficient < 1.2f && actor.Member.CurrentFocus - s.FocusCost < 1));
            var weakest = enemies.OrderBy(e => e.CurrentHP).First();

            switch (actor.Member.Job.Id)
            {
                case Fantasia.Data.JobId.Detective:
                    if (evasive != null && Usable("skill.detective.lift_the_veil") is { } veil) return (veil, evasive);
                    if (Usable("skill.detective.pinpoint_breach") is { } breach && !weakest.HasEvasionState) return (breach, weakest);
                    if (Usable("skill.detective.expose_weakness") is { } expose && !weakest.Statuses.Has(Fantasia.Data.StatusEffectType.DefenseDown)) return (expose, weakest);
                    break;
                case Fantasia.Data.JobId.PlagueDoctor:
                    var hurt = allies.OrderBy(a => (float)a.CurrentHP / a.MaxHP).First();
                    if ((float)hurt.CurrentHP / hurt.MaxHP < 0.75f && Usable("skill.plague_doctor.emergency_suture") is { } suture) return (suture, hurt);
                    var afflicted = allies.FirstOrDefault(a => a.Statuses.Has(Fantasia.Data.StatusEffectType.Poison) || a.Statuses.Has(Fantasia.Data.StatusEffectType.Confusion));
                    if (afflicted != null && Usable("skill.plague_doctor.purge_plague") is { } purge) return (purge, afflicted);
                    if (enemies.Count >= 2 && Usable("skill.plague_doctor.black_miasma") is { } miasma) return (miasma, null);
                    break;
                case Fantasia.Data.JobId.Sniper:
                    if (!actor.Statuses.Has(Fantasia.Data.StatusEffectType.AccuracyUp) && Usable("skill.sniper.zero_in") is { } zero && actor.Member.CurrentFocus >= 2) return (zero, actor);
                    if (Usable("skill.sniper.piercing_round") is { } pierce && !weakest.HasEvasionState) return (pierce, weakest);
                    if (Usable("skill.sniper.suppressing_fire") is { } suppress && !weakest.HasEvasionState) return (suppress, weakest);
                    break;
            }
            return (null, null);
        }

        private void NoteParty(string label)
        {
            var party = BoardSession.Instance != null ? BoardSession.Instance.Party : null;
            if (party == null) { Note($"{label}: 파티 없음"); return; }
            Note($"{label}: " + string.Join(", ", party.Members.Select(m =>
                $"{m.Job.DisplayName}{(m.IsPermanentlyDead ? "(사망)" : m.IsDowned ? "(쓰러짐)" : "")} HP {m.CurrentHP}/{m.MaxHP} MP {m.CurrentMP}/{m.MaxMP} 집중 {m.CurrentFocus}/{m.MaxFocus} LP {m.CurrentLP}"))
                + $" · Cent {party.Cent} · 위치 {BoardSession.Instance.PlayerPosition.Q},{BoardSession.Instance.PlayerPosition.R}");
        }

        // 패배·부활 점검용 시작 상태: -autoplay-hp N (전원 HP를 N으로), -autoplay-lp N (전원 LP를 N 이하로).
        private void ApplyStartOverrides()
        {
            var party = BoardSession.Instance != null ? BoardSession.Instance.Party : null;
            if (party == null) return;
            var args = Environment.GetCommandLineArgs();
            int hpIndex = Array.IndexOf(args, "-autoplay-hp");
            if (hpIndex >= 0 && hpIndex + 1 < args.Length && int.TryParse(args[hpIndex + 1], out int hp))
                foreach (var m in party.Members) m.TakeDamage(Mathf.Max(0, m.CurrentHP - hp));
            int lpIndex = Array.IndexOf(args, "-autoplay-lp");
            if (lpIndex >= 0 && lpIndex + 1 < args.Length && int.TryParse(args[lpIndex + 1], out int lp))
                foreach (var m in party.Members) while (m.CurrentLP > lp && m.TrySpendLP()) { }
            if (hpIndex >= 0 || lpIndex >= 0) Note($"시작 상태 조정: HP {(hpIndex >= 0 ? args[hpIndex + 1] : "-")} · LP {(lpIndex >= 0 ? args[lpIndex + 1] : "-")}");
            // D24·D25 점검: 피의 흔적·영혼 수확 시작값
            int trailIndex = Array.IndexOf(args, "-autoplay-trail");
            if (trailIndex >= 0 && trailIndex + 1 < args.Length && int.TryParse(args[trailIndex + 1], out int trail)) BoardSession.Instance.BloodTrail = trail;
            int soulIndex = Array.IndexOf(args, "-autoplay-soul");
            if (soulIndex >= 0 && soulIndex + 1 < args.Length && int.TryParse(args[soulIndex + 1], out int soul)) BoardSession.Instance.SoulHarvest = soul;
            if (trailIndex >= 0 || soulIndex >= 0) Note($"시작 G {BoardSession.Instance.BloodTrail} · S {BoardSession.Instance.SoulHarvest}");

            // 보스 플레이테스트 (Boss_Abel_v1.0 3쪽): 레벨, 준비물(일반 HP 회복약 3·해독제 3·집중의 향초 1), HP·MP 최대·집중력 3
            int levelIndex = Array.IndexOf(args, "-autoplay-level");
            if (levelIndex >= 0 && levelIndex + 1 < args.Length && int.TryParse(args[levelIndex + 1], out int level))
            {
                foreach (var m in party.Members)
                {
                    while (m.Level < level) m.GrantExperience(m.ExperienceToNextLevel);
                    m.Heal(m.MaxHP);
                    m.RestoreMP(m.MaxMP);
                    m.RestoreFocus(m.MaxFocus);
                }
                Note($"시작 레벨 {level}: {string.Join(", ", party.Members.Select(m => $"{m.Job.DisplayName} Lv{m.Level} HP {m.CurrentHP} 집중 {m.CurrentFocus}"))}");
            }
            if (args.Contains("-autoplay-kit") && Fantasia.Data.ItemCatalog.Current is { } catalog)
            {
                party.Inventory.AddConsumable(catalog.Consumable(Fantasia.Data.ConsumableIds.LifeElixir), 3);
                party.Inventory.AddConsumable(catalog.Consumable(Fantasia.Data.ConsumableIds.Antidote), 3);
                party.Inventory.AddConsumable(catalog.Consumable(Fantasia.Data.ConsumableIds.FocusCandle), 1);
                Note("준비물: 생명의 영약 3 · 해독제 3 · 집중의 향초 1");
                if (args.Contains("-autoplay-ampoule") && catalog.Consumable("consumable.swift_ampoule") is { } amp) { party.Inventory.AddConsumable(amp, 2); Note("준비물 추가: 신속의 앰플 2"); }
            }
            if (args.Contains("-autoplay-fast")) Time.timeScale = 4f; // 반복 점검용 4배속
        }

        // 같은 시드 비교용 (-autoplay-seed N)
        private static int Seed = -1;

        // ── 결정서 반영 QA (-autoplay-qa): 항목마다 [QA 통과]/[QA 실패] 한 줄 + 필요한 화면만 촬영 ──
        private int _qaPass, _qaFail;
        private void Qa(bool ok, string what)
        {
            if (ok) _qaPass++; else _qaFail++;
            Note($"[QA {(ok ? "통과" : "실패")}] {what}");
        }

        // 성능 기록 (2026-11-06 최적화 비교용): 화면 갱신 제한을 잠시 풀고 3초 동안 평균 프레임,
        // 그리기 호출·삼각형 수(가능한 빌드에서만), 장면의 메시 삼각형 합을 남긴다.
        private static long TriCount(Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return n; } // 읽기 불가 메시도 됨

        private IEnumerator PerfProbe(string where)
        {
            int vsync = QualitySettings.vSyncCount, target = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            var dc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
            var tr = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            yield return new WaitForSecondsRealtime(0.5f);
            int frames = 0; long dcSum = 0, trSum = 0; float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 3f) { yield return null; frames++; dcSum += dc.LastValue; trSum += tr.LastValue; }
            float sec = Time.realtimeSinceStartup - start;
            long sceneTris = 0;
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) if (mf.sharedMesh != null && mf.GetComponent<Renderer>() is { enabled: true } r && r.gameObject.activeInHierarchy) sceneTris += TriCount(mf.sharedMesh);
            foreach (var sm in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) if (sm.sharedMesh != null && sm.gameObject.activeInHierarchy) sceneTris += TriCount(sm.sharedMesh);
            string gpu = dc.Valid && dcSum > 0 ? $" · 그리기 호출 {dcSum / frames} · 화면 삼각형 {trSum / frames / 1000}k" : " · 그리기 호출 기록 없음(배포 빌드)";
            Note($"[성능] {where}: 평균 {frames / sec:0} FPS ({sec * 1000f / frames:0.0}ms){gpu} · 장면 메시 삼각형 {sceneTris / 1000}k");
            dc.Dispose(); tr.Dispose();
            QualitySettings.vSyncCount = vsync; Application.targetFrameRate = target;
        }

        // 전투에서 아군이 행동을 고를 차례가 될 때까지 (대사는 넘김)
        private static IEnumerator WaitAllyTurn(BattleController b, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until && !b.DevAwaitingPlayer && !b.DevFinished) { if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance(); yield return null; }
        }

        private IEnumerator QaRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            if (Seed >= 0) GameRandom.Seed(Seed);
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return WaitFor(() => DialoguePanel.IsOpen, 3f, "시작 대화");
            int lines = 0;
            if (DialoguePanel.IsOpen && DialoguePanel.DevLineIndex == 0)
            {
                // 11/12 팀장 지시: 대화 창 아무 곳(글 부분)을 눌러도 다음 줄 — 첫 줄은 창 몸통 클릭으로 넘김
                DialoguePanel.DevClickBox();
                yield return null;
                Qa(!DialoguePanel.IsOpen || DialoguePanel.DevLineIndex == 1, $"대화 창 글 부분을 누르면 다음 줄 (줄 0 → {DialoguePanel.DevLineIndex})");
                lines++;
                yield return new WaitForSeconds(0.15f);
            }
            while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); lines++; yield return new WaitForSeconds(0.15f); }
            var board = FindFirstObjectByType<BoardTestController>();
            var s = BoardSession.Instance;
            var party = s.Party;
            yield return WaitFor(() => board.DevIsIdle, 10f, "보드 대기");
            Qa(lines == 8, $"시작 대화 8줄 (실제 {lines})");
            Qa(s.StoryFlags.Contains(ActOneStory.FlagIntroDone), "시작 대화 본 기록 저장");
            var doctor = party.Members.FirstOrDefault(m => m.Job.Id == Fantasia.Data.JobId.PlagueDoctor);
            Qa(doctor != null && doctor.MaxMP == 140 && doctor.CurrentMP == 140, $"역병 의사 MP 140/140 (실제 {doctor?.CurrentMP}/{doctor?.MaxMP})");
            Qa(s.Quests.Any(q => q.Objective == ActOneStory.QuestCaseObjective), "메인 의뢰 목표 문구");

            // 상점: 제목·무기 3종·의뢰 탭
            board.DevOpenShop();
            yield return WaitFor(() => ShopPanel.IsOpen, 3f, "상점");
            var ui = ShopPanel.Instance;
            Qa(ui.DevTitle == "그리니치 거점", $"상점 제목 (실제 {ui.DevTitle})");
            ui.DevSelectTab(1);
            ui.DevSelectRow(0);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_shop_weapons");
            Qa(ui.DevRowCount == 3, $"런던 무기 진열 3종 (실제 {ui.DevRowCount})");
            // 11/12 받은 모델로 찍은 아이템 아이콘 확인 (보급품 · 방어구 탭)
            ui.DevSelectTab(0);
            ui.DevSelectRow(0);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_shop_supplies");
            ui.DevSelectTab(2);
            ui.DevSelectRow(0);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_shop_armor");
            ui.DevSelectTab(6);
            Qa(ui.DevRowCount == 2, $"의뢰 탭 2개 (실제 {ui.DevRowCount})");
            ui.DevSelectRow(0);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_quest_before_accept");
            var q1 = SideQuests.All[0];
            ui.DevAct();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_quest_accepted");
            Qa(SideQuests.IsAccepted(s, q1) && !SideQuests.IsDone(s, q1), "의뢰 1 수락 → 진행 중 0/2");
            Qa(s.Quests.Any(q => q.Title == q1.Title), "의뢰 1 사건 기록에 올라감");

            // 서비스: 명탐정만 다치게 → 숙박 기본 대상 = 명탐정, 1명분 30 Cent, 그 사람만 회복, 턴 끝
            var hurt = party.Members[0];
            hurt.TakeDamage(60);
            var before = party.Members.Select(m => (m.CurrentHP, m.CurrentMP, m.CurrentFocus)).ToList();
            ui.DevSelectTab(3);
            ui.DevSelectRow(0);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_service_target");
            Qa(ui.DevServiceTarget == hurt, $"숙박 기본 대상 = 가장 다친 {hurt.Job.DisplayName} (실제 {ui.DevServiceTarget?.Job.DisplayName})");
            int cent = party.Cent;
            ui.DevAct();
            int expectHp = Mathf.Min(hurt.MaxHP, before[0].CurrentHP + (hurt.MaxHP + 1) / 2);
            Qa(cent - party.Cent == 30, $"숙박 1명 30 Cent (실제 {cent - party.Cent})");
            Qa(hurt.CurrentHP == expectHp, $"{hurt.Job.DisplayName} HP {before[0].CurrentHP} → {hurt.CurrentHP} (기대 {expectHp})");
            Qa(Enumerable.Range(1, party.Members.Count - 1).All(i => party.Members[i].CurrentHP == before[i].CurrentHP && party.Members[i].CurrentFocus == before[i].CurrentFocus), "다른 동료는 그대로");
            Qa(!ShopPanel.IsOpen, "숙박 뒤 상점 닫힘 (아군 턴 끝)");

            // 거점 [상점 열기] 버튼: 적 턴 뒤 다시 아군 턴 대기일 때
            yield return new WaitForSeconds(1f);
            yield return WaitFor(() => board.DevIsIdle && !board.DevAwaitingSelection, 20f, "다음 아군 턴");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("qa_reopen_button");
            Qa(ShopPanel.DevReopenVisible, "거점에서 [상점 열기] 버튼 보임");
            board.DevOpenShop();
            yield return WaitFor(() => ShopPanel.IsOpen, 3f, "상점 다시");
            yield return null;
            Qa(ShopPanel.IsOpen && !ShopPanel.DevReopenVisible, "상점 열면 버튼 숨김");
            ShopPanel.Close();
            yield return new WaitForSeconds(0.2f);

            // 사건 기록 (Q)
            CaseLogPanel.Toggle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("qa_case_log");
            CaseLogPanel.Toggle();

            // 의뢰 완료: P04는 이긴 것으로 두고 Q01을 실제로 싸움 → 보드로 돌아오면 알림 + 완료 대사
            WorldMapData.TryParseLabel("P04", out var p04);
            s.ClearedEncounters.Add(p04);
            int centBeforeQuest = party.Cent;
            board.DevArriveAt("Q01");
            yield return WaitFor(() => EncounterPanel.IsOpen, 5f, "Q01 조우");
            EncounterPanel.DevChoose(1);
            Fantasia.Combat.BattleController.DevShowcaseLoot = true; // 11/12 결과 창 아이템 띠 점검: 전설 무기·에픽 방어구·소모품
            yield return RunBattle(shots: false);
            var b = _lastBattle;
            bool won = b != null && b.DevFinished && b.DevEnemies.All(e => !e.IsAlive);
            Qa(won, "Q01 전투 승리");
            int battleCent = party.Cent - centBeforeQuest;
            ItemAcquiredToast.DevLast = null;
            yield return new WaitForSecondsRealtime(0.3f);
            if (b != null)
            {
                Qa(b.DevHud.DevResultDropPlaying && b.DevHud.DevResultTitle == "전투 승리", $"승리 판 \"{b.DevHud.DevResultTitle}\" · 요약 \"{b.DevHud.DevResultSummary}\"");
                Qa(!b.DevHud.DevResultButtonsVisible, "판이 멈추기 전 버튼 숨김");
                Qa(b.DevHud.DevResultItemCount == 3, $"결과 창에 얻은 아이템 그림·등급 색 띠 ({b.DevHud.DevResultItemCount}칸)");
                yield return new WaitForSecondsRealtime(1.9f);
                Qa(b.DevHud.DevResultButtonsVisible, "판이 멈춘 뒤 버튼 보임");
                yield return Shot("qa_victory_panel");
                // 11/12 팀장 지적: 방어 방패가 결과 창 위에 남던 것 — 결과 창이 뜨면 0개, 그 뒤 보호막이 생겨도 안 그림
                Qa(b.DevHud.DevShieldMarkCount == 0, $"결과 창 위 방패 표시 없음 (실제 {b.DevHud.DevShieldMarkCount})");
                b.DevShieldAll(Fantasia.Combat.AttackResolver.DefendShield);
                Qa(b.DevHud.DevShieldMarkCount == 0, $"결과 창 뒤 보호막 생겨도 방패 표시 안 함 (실제 {b.DevHud.DevShieldMarkCount})");
                // 11/12 팀장 확인: 보호막 8에 아벨 모로 10 → 보호막 -8, 피해 2 (HP가 낮아도 같음, 피해가 작으면 다 막음)
                var s1 = Fantasia.Combat.BattleController.ShieldSplit(10, 8, 100);
                var s2 = Fantasia.Combat.BattleController.ShieldSplit(10, 8, 5);
                var s3 = Fantasia.Combat.BattleController.ShieldSplit(6, 8, 100);
                Qa(s1 == (8, 2) && s2 == (8, 2) && s3 == (6, 0), $"보호막 나누기 10/8→{s1} · HP5일 때→{s2} · 6/8→{s3} (기대 (8,2)·(8,2)·(6,0))");
            }
            b?.DevContinue();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null && DialoguePanel.IsOpen, 10f, "의뢰 완료 대사");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_quest_complete");
            Qa(SideQuests.IsDone(s, q1), "의뢰 1 완료");
            Qa(ItemAcquiredToast.DevLast != null && ItemAcquiredToast.DevLast.Contains("의뢰 완료"), $"완료 알림 (실제 {ItemAcquiredToast.DevLast})");
            Qa(party.Cent - centBeforeQuest - battleCent == SideQuests.RewardCent, $"의뢰 보상 {SideQuests.RewardCent} Cent (실제 {party.Cent - centBeforeQuest - battleCent})");
            while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSeconds(0.15f); }
            board = FindFirstObjectByType<BoardTestController>();
            yield return WaitFor(() => board.DevIsIdle, 15f, "보드 대기");
            Qa(s.CompletedQuests.Any(q => q.Title == q1.Title), "사건 기록 완료 목록에 의뢰 1");

            // 피의 흔적 알림: 50 넘김 → 사냥 끝(0·유예 2) → 다시 50 넘김
            s.BloodTrail = 45; board.DevRefreshTopBar();
            ItemAcquiredToast.DevLast = null;
            s.BloodTrail = 55; board.DevRefreshTopBar();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_blood_50_first");
            Qa(ItemAcquiredToast.DevLast != null && ItemAcquiredToast.DevLast.Contains("50"), $"피의 흔적 50 첫 알림 (실제 {ItemAcquiredToast.DevLast})");
            s.BloodTrail = 85; board.DevRefreshTopBar();
            s.BloodTrail = 0; s.Hunt.GraceRounds = 2; board.DevRefreshTopBar();
            yield return new WaitForSeconds(2.5f);
            yield return Shot("qa_blood_grace");
            Qa(BoardTopBar.DevBloodText.Contains("유예 2라운드"), $"사냥 뒤 유예 표시 (실제 {BoardTopBar.DevBloodText})");
            s.Hunt.GraceRounds = 0;
            ItemAcquiredToast.DevLast = null;
            s.BloodTrail = 55; board.DevRefreshTopBar();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_blood_50_again");
            Qa(ItemAcquiredToast.DevLast != null && ItemAcquiredToast.DevLast.Contains("50"), $"0으로 돌아간 뒤 50 알림 다시 (실제 {ItemAcquiredToast.DevLast})");
            s.BloodTrail = 0; board.DevRefreshTopBar();

            // 11/11 UI 순서 규칙 1: 알림 두 개를 연달아 띄워도 둘 다 차례로 보임 (덮어쓰기 없음)
            yield return WaitFor(() => ItemAcquiredToast.DevShowing == null && ItemAcquiredToast.DevQueued == 0, 15f, "앞 알림 끝");
            ItemAcquiredToast.Show("QA 알림 첫째");
            ItemAcquiredToast.Show("QA 알림 둘째");
            yield return WaitFor(() => ItemAcquiredToast.DevShowing == "QA 알림 첫째", 3f, "첫째 알림");
            Qa(ItemAcquiredToast.DevShowing == "QA 알림 첫째" && ItemAcquiredToast.DevQueued == 1, $"알림 줄: 첫째 먼저 (보임 {ItemAcquiredToast.DevShowing} · 대기 {ItemAcquiredToast.DevQueued})");
            yield return Shot("qa_toast_queue_1");
            yield return WaitFor(() => ItemAcquiredToast.DevShowing == "QA 알림 둘째", 4f, "둘째 알림");
            Qa(ItemAcquiredToast.DevShowing == "QA 알림 둘째", $"알림 줄: 둘째가 이어서 (보임 {ItemAcquiredToast.DevShowing})");
            yield return Shot("qa_toast_queue_2");

            // 11/12 아이템 그림·등급 색: 그림 없는 무기도 종류 아이콘 · 전설은 빨간 글 "[전설] ○○ — 장비 획득"
            var cat = Fantasia.Data.ItemCatalog.Current;
            var plainWeapon = cat != null ? cat.Weapons.FirstOrDefault(w => w != null && w.Icon == null) : null;
            if (plainWeapon != null)
            {
                int catIcons = cat.Weapons.Concat<object>(cat.Armors).Concat(cat.Consumables).Count(o => o != null && ItemLook.IconOf(o) != null);
                int all = cat.Weapons.Length + cat.Armors.Length + cat.Consumables.Length;
                Qa(catIcons == all, $"모든 아이템에 그림 (자체 또는 종류 아이콘) {catIcons}/{all}");
            }

            // 2026-11-06 변경 확인: ① 집중을 여러 개 골라 쓰기 ② 한 명이 퇴각에 성공하면 파티 전원 퇴각
            board.DevArriveAt("O07");
            yield return WaitFor(() => EncounterPanel.IsOpen, 5f, "O07 조우 (집중·퇴각)");
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투 (집중·퇴각)");
            var fb = FindFirstObjectByType<BattleController>();
            yield return WaitAllyTurn(fb, 30f);
            if (fb.DevAwaitingPlayer)
            {
                var actor = fb.DevCurrent;
                actor.Member.RestoreFocus(actor.Member.MaxFocus);
                int slotsN = actor.BasicAttack(0).SlotCount;
                int want = Mathf.Min(2, slotsN);
                fb.DevHud.DevSetFocus(want);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("qa_focus_picker");
                Qa(fb.DevHud.FocusCount == want, $"집중 개수 고르기 {want}개 (실제 {fb.DevHud.FocusCount})");
                int focusBefore = actor.Member.CurrentFocus;
                fb.DevAttack(want);
                yield return new WaitForSeconds(0.1f * want + 0.3f * (slotsN - want) + 0.35f);
                yield return Shot("qa_focus_roll");
                Qa(focusBefore - actor.Member.CurrentFocus == want, $"집중 {want}개 소모 (실제 {focusBefore - actor.Member.CurrentFocus}) · 슬롯 {slotsN}개 중 {want}개 확정");
            }
            else Qa(false, "집중 확인: 아군 차례가 오지 않음");

            bool retreatChecked = false;
            for (int tries = 0; tries < 12 && !fb.DevFinished; tries++)
            {
                yield return WaitAllyTurn(fb, 30f);
                if (fb.DevFinished || !fb.DevAwaitingPlayer) break;
                foreach (var m in party.AliveMembers) m.Heal(m.MaxHP); // 확인 도중 전멸하지 않게
                int inBattle = fb.DevAllies.Count(a => a.InBattle);
                string who = fb.DevCurrent.DisplayName;
                fb.DevRetreat();
                yield return new WaitForSeconds(2.2f);
                int retreated = fb.DevAllies.Count(a => a.HasRetreated);
                if (retreated == 0) continue;
                retreatChecked = true;
                Qa(retreated == inBattle, $"{who} 퇴각 성공 1번에 생존 아군 전원 이탈 ({retreated}/{inBattle}, 시도 {tries + 1}번째)");
                break;
            }
            if (!retreatChecked) Qa(false, "퇴각 확인: 12번 안에 성공하지 못함");
            yield return WaitFor(() => fb.DevFinished, 10f, "퇴각 결과");
            yield return new WaitForSecondsRealtime(2.2f);
            yield return Shot("qa_retreat_result");
            Qa(fb.DevHud.DevResultTitle == "퇴각", $"결과 판 \"퇴각\" (실제 {fb.DevHud.DevResultTitle})");
            fb.DevContinue();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 복귀");
            board = FindFirstObjectByType<BoardTestController>();
            yield return WaitFor(() => board.DevIsIdle, 15f, "보드 대기");

            // 게임 오버: LP를 모두 쓴 상태에서 전멸 → "조사대 전멸" + [새 게임] [메인 메뉴]
            foreach (var m in party.Members) while (m.TrySpendLP()) { }
            board.DevArriveAt("O07");
            yield return WaitFor(() => EncounterPanel.IsOpen, 5f, "O07 조우");
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            var battle = FindFirstObjectByType<BattleController>();
            float until = Time.time + 20f;
            while (Time.time < until && !battle.DevAwaitingPlayer) { if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance(); yield return null; }
            foreach (var m in party.AliveMembers.ToList()) m.TakeDamage(m.CurrentHP);
            battle.DevCheckEnd();
            yield return WaitFor(() => battle.DevFinished, 10f, "전멸 판정");
            yield return new WaitForSecondsRealtime(2.2f); // 판이 멈추고 버튼이 나올 때까지
            yield return Shot("qa_game_over");
            Qa(battle.DevHud.DevResultTitle == ActOneStory.GameOverTitle, $"게임 오버 제목 (실제 {battle.DevHud.DevResultTitle})");
            Qa(battle.DevHud.DevResultButtons == "새 게임 / 메인 메뉴", $"게임 오버 버튼 (실제 {battle.DevHud.DevResultButtons})");
            battle.DevContinue(); // [새 게임]
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            menu = FindFirstObjectByType<MainMenuController>();
            yield return Shot("qa_after_new_game");
            Qa(menu != null && menu.DevPartySelectOpen, "[새 게임] → 파티 구성 화면 바로 열림");

            Note($"QA 결과: 통과 {_qaPass} · 실패 {_qaFail}");
            Note("자동 플레이 완료");
        }

        // 보고 대화 QA (-autoplay-qa-report): 보스 처치 상태로 거점 옆 칸 → 주사위·이동으로 S05 도착 → 보고 대화가 열린 순간 저장 복사
        // (= 대화 도중 꺼진 상태, qa_report_save.json) → 끝까지 진행해 1막 클리어 화면 촬영
        private IEnumerator QaReportRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            var s = BoardSession.Instance;
            yield return WaitFor(() => board.DevIsIdle, 10f, "보드 대기");
            ActOneStory.MarkBossDefeated(s);
            s.StoryFlags.Add(ActOneStory.FlagAfterDialogueDone);
            var map = board.DevBoard.Map;
            var hub = map.TileAt(ActOneStory.HubLabel).Coord;
            var next = map.PassableNeighbors(hub).First(c => map.Tiles[c].Kind != MapTileKind.Enemy);
            board.DevTeleport(map.Tiles[next].Label);
            yield return new WaitForSeconds(0.3f);
            board.DevRoll();
            yield return WaitFor(() => board.DevAwaitingSelection, 5f, "칸 선택");
            board.DevMoveToward(new System.Collections.Generic.List<HexCoord> { hub });
            yield return WaitFor(() => DialoguePanel.IsOpen, 15f, "보고 대화");
            Qa(DialoguePanel.IsOpen && board.DevPosition.Equals(hub), "S05 도착 → 제임스 보고 대화");
            File.Copy(SaveGame.SavePath, Path.Combine(_outDir, "qa_report_save.json"), true);
            Note("보고 대화 도중 저장 복사 → qa_report_save.json");
            yield return new WaitForSeconds(0.3f);
            yield return Shot("qa_report_dialogue");
            while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSeconds(0.2f); }
            yield return WaitFor(() => ActClearPanel.IsOpen, 10f, "1막 클리어");
            yield return new WaitForSecondsRealtime(2.2f);
            yield return Shot("qa_report_clear");
            Qa(ActClearPanel.IsOpen, "보고 끝 → 1막 클리어 화면");
            Qa(ActClearPanel.DevButtonVisible, "1막 클리어 판 멈춘 뒤 버튼");
            Note($"QA 결과: 통과 {_qaPass} · 실패 {_qaFail}");
            Note("자동 플레이 완료");
        }

        // 효과음 호출 요약: 불린 ID·횟수, 안 불린 ID, 실제 재생 수 (파일이 없으면 0)
        private static string SfxSummary()
        {
            var called = SfxIds.All.Where(id => SfxPlayer.DevCalls.ContainsKey(id)).Select(id => $"{id} {SfxPlayer.DevCalls[id]}").ToArray();
            var missing = SfxIds.All.Where(id => !SfxPlayer.DevCalls.ContainsKey(id)).ToArray();
            return $"효과음: 불린 {called.Length}/{SfxIds.All.Length}종 · 실제 재생 {SfxPlayer.DevAudible}회 · 볼륨 {SfxPlayer.VolumeScale:0.00}\n  불림: {string.Join(", ", called)}\n  안 불림: {(missing.Length == 0 ? "없음" : string.Join(", ", missing))}"
                   + $"\n이펙트: {(Fantasia.Combat.BattleVfx.DevPlayed.Count == 0 ? "없음" : string.Join(", ", Fantasia.Combat.BattleVfx.DevPlayed.Select(p => $"{p.Key} {p.Value}")))}"
                   + $" / 디버프 둘레 효과 켜짐 {Fantasia.Combat.StatusAura.DevShownTotal}번 (한때 최대 {Fantasia.Combat.StatusAura.DevPeak}개)"; // 11/13
        }

        // UI 전수 점검 보충 (-autoplay-uiextra): 다른 모드에서 안 찍히는 작은 UI — 획득 알림 · 칸 툴팁 · 랜드마크 정보 창
        private IEnumerator UiExtraRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            yield return WaitFor(() => board.DevIsIdle, 10f, "보드 대기");
            yield return new WaitForSeconds(2f);

            ItemAcquiredToast.Show("생명의 영약 획득", UITheme.Current != null ? UITheme.Current.CentIcon : null);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("toast_item");
            yield return new WaitForSeconds(3f);

            board.enabled = false; // 마우스가 칸 위에 없으면 판이 매 프레임 툴팁을 숨김
            TileTooltip.Show("<b>J06 · 골목</b>\n역병 쥐 ×2 · 연성 독거미(유체) ×1", new Vector2(Screen.width * 0.45f, Screen.height * 0.6f));
            yield return new WaitForSeconds(0.3f);
            yield return Shot("tile_tooltip");
            TileTooltip.Hide();
            board.enabled = true;

            if (board.PlaceholderLandmarks.Length > 0)
            {
                LandmarkInfoPanel.Show(board.PlaceholderLandmarks[0]);
                yield return new WaitForSeconds(0.4f);
                yield return Shot("landmark_info");
            }
            Note("uiextra 끝");
        }

        // 판 떨어지는 연출 점검 (-autoplay-paneldrop): 보드 위에 우리 캔버스 설정(1280×720 기준)으로 승리·패배·턴 안내 판을 띄움.
        // 떨어지는 중·닿는 순간·출렁임·멈춤을 촬영하고, 소리·프레임 시간·시점을 기록 (Docs/Art/PanelDrop_1028)
        private IEnumerator PanelDropRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            yield return WaitFor(() => board.DevIsIdle, 10f, "보드 대기");
            yield return new WaitForSeconds(2f); // 아군 턴 배너가 사라질 때까지

            var canvasGO = new GameObject("PanelDropTest");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            UGUIKit.ConfigureScaler(scaler);
            yield return null;
            Note($"화면 {Screen.width}x{Screen.height} · UI 배율 {canvas.scaleFactor:0.00} (기준 1280×720) · AudioListener {FindFirstObjectByType<AudioListener>() != null}");

            var cases = new (string prefab, float width, string title, string body)[]
            {
                ("VictoryPanel", 700f, "승리", "적을 모두 쓰러뜨렸다.\n180 Cent · 생명의 영약 · 경험치 +100"),
                ("DefeatPanel", 700f, "패배", "파티가 쓰러졌다. 약탈 36 Cent.\n직전 안전 타일에서 다시 일어났다."),
                ("HangingPanel", 560f, "아군 턴", "명탐정이(가) 파티를 이끈다"),
            };
            bool noShot = Environment.GetCommandLineArgs().Contains("-autoplay-noshot"); // 렉 측정용: 촬영 없이
            float[] shotTimes = { 0.3f, 0.62f, 0.8f, 1.1f, 2.2f };
            string[] shotNames = { "falling", "landing", "bounce", "swing", "rest" };
            foreach (var c in cases)
            {
                var prefab = Resources.Load<GameObject>("PanelDrop/" + c.prefab);
                Qa(prefab != null, $"{c.prefab} 불러오기");
                if (prefab == null) continue;
                var holder = new GameObject(c.prefab + "_Holder", typeof(RectTransform));
                holder.transform.SetParent(canvasGO.transform, false);
                var hrt = (RectTransform)holder.transform;
                hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
                holder.SetActive(false);
                var go = Instantiate(prefab, holder.transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(c.width, c.width * 1024f / 1536f);
                rt.anchoredPosition = new Vector2(0f, 30f);
                var drop = go.GetComponent<HangingPanelDrop>();
                Qa(drop != null && drop.artwork != null && drop.chainRattle != null && drop.panelStop != null, $"{c.prefab} 그림·소리 연결");
                var title = UGUIKit.CreateText(drop.ContentRoot, "Title", new Vector2(0f, 0.5f), new Vector2(1f, 1f), c.title, 34, TextAnchor.MiddleCenter);
                title.color = new Color(0.95f, 0.85f, 0.55f);
                title.fontStyle = FontStyle.Bold;
                var body = UGUIKit.CreateText(drop.ContentRoot, "Body", new Vector2(0f, 0f), new Vector2(1f, 0.5f), c.body, 15, TextAnchor.MiddleCenter);
                body.color = new Color(0.95f, 0.92f, 0.86f);

                float landedAt = -1f, finishedAt = -1f, start = 0f;
                bool soundAtLanding = false;
                drop.onLanded.AddListener(() => { landedAt = Time.unscaledTime - start; var a = go.GetComponent<AudioSource>(); soundAtLanding = a != null && a.isPlaying; });
                drop.onFinished.AddListener(() => finishedAt = Time.unscaledTime - start);
                float maxDt = 0f;
                string firstDt = "";
                int frames = 0, next = 0;
                start = Time.unscaledTime;
                holder.SetActive(true);
                while (Time.unscaledTime - start < 2.4f)
                {
                    yield return null;
                    frames++;
                    maxDt = Mathf.Max(maxDt, Time.unscaledDeltaTime);
                    if (frames <= 3) firstDt += $"{Time.unscaledDeltaTime * 1000f:0}ms ";
                    if (next < shotTimes.Length && Time.unscaledTime - start >= shotTimes[next])
                    {
                        if (!noShot) yield return Shot($"paneldrop_{c.prefab}_{shotNames[next]}");
                        if (shotNames[next] == "rest") { var corners = new Vector3[4]; var br = drop.ContentRoot.parent as RectTransform; br.GetWorldCorners(corners); var raw = br.GetComponent<UnityEngine.UI.RawImage>(); Note($"  진단: 캔버스 {canvas.isActiveAndEnabled} · 판 활성 {br.gameObject.activeInHierarchy} · 화면 위치 {corners[0]} ~ {corners[2]} · 그림 {raw?.texture?.width}x{raw?.texture?.height} uv {raw?.uvRect} 색 {raw?.color} · 글자 {title.isActiveAndEnabled} \"{title.text}\" 위치 {title.rectTransform.position} · 루트 크기 {rt.rect.size}"); }
                        next++;
                    }
                }
                Note($"{c.prefab}: 가로 {c.width} (화면 {c.width * canvas.scaleFactor:0}px, 원본 1536px의 {c.width * canvas.scaleFactor / 1536f:0.00}배) · 닿음 {landedAt:0.00}초 · 멈춤 {finishedAt:0.00}초 · 닿을 때 소리 {soundAtLanding} · 프레임 {frames} · 가장 긴 프레임 {maxDt * 1000f:0}ms{(noShot ? "" : " (촬영 포함)")} · 처음 3프레임 {firstDt}");
                Qa(landedAt > 0f && finishedAt > landedAt, $"{c.prefab} 떨어짐 → 멈춤까지 끝남");
                Qa(soundAtLanding, $"{c.prefab} 소리 재생");
                Destroy(holder);
                yield return new WaitForSeconds(0.3f);
            }
            Note($"QA 결과: 통과 {_qaPass} · 실패 {_qaFail}");
            Note("자동 플레이 완료");
        }

        // 이어하기 QA (-autoplay-qa-resume): 저장 파일에서 이어하기 → 대사가 다시 나오는지
        private IEnumerator QaResumeRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            Note($"이어하기 정보: {menu.DevContinueInfo.Replace("\n", " / ")}");
            menu.OnContinue();
            yield return WaitFor(() => DialoguePanel.IsOpen, 15f, "이어하기 뒤 대화");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("qa_resume_dialogue");
            Qa(DialoguePanel.IsOpen, "이어하기 뒤 대화 다시 나옴");
            int lines = 0;
            while (DialoguePanel.IsOpen && lines < 20) { DialoguePanel.DevAdvance(); lines++; yield return new WaitForSeconds(0.2f); }
            yield return new WaitForSeconds(1.5f);
            var s = BoardSession.Instance;
            Note($"대화 {lines}줄 · 처치 후 대화 기록 {s.StoryFlags.Contains(ActOneStory.FlagAfterDialogueDone)} · 보고 {s.StoryFlags.Contains(ActOneStory.FlagReported)} · 클리어 창 {ActClearPanel.IsOpen}");
            if (ActClearPanel.IsOpen) yield return Shot("qa_resume_clear");
            Note($"QA 결과: 통과 {_qaPass} · 실패 {_qaFail}");
            Note("자동 플레이 완료");
        }

        // 새 게임 시작 대화 (1막 기획 결정서 05) — 첫 줄을 찍고 끝까지 넘김
        // ── 주사위 굴림 영상 (-autoplay-dicevideo, 2026-11-06 팀 공유용) ──
        // 새 게임 → 보드에서 Space 굴림 → 칸 이동을 10초 동안 30fps로 한 장씩 저장 (<out>/frames/0000.jpg ...).
        // 게임 시간을 장마다 1/30초로 고정해서 저장이 느려도 영상 속도는 실제와 같다. MP4로 묶기: Editor/FramesToVideo.
        private IEnumerator DiceVideoRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            yield return WaitFor(() => board.DevIsIdle && !BoardTopBar.DevTurnDropVisible, 20f, "굴림 대기");
            yield return new WaitForSeconds(0.5f);

            string dir = Path.Combine(_outDir, "frames");
            Directory.CreateDirectory(dir);
            const int Fps = 30, Seconds = 10;
            bool recording = true;
            int frame = 0;
            Time.captureFramerate = Fps;
            StartCoroutine(RecordFrames(dir, Fps * Seconds, () => frame++, () => recording = false));

            yield return new WaitForSeconds(0.8f);
            for (int roll = 1; recording; roll++)
            {
                board.DevRoll();
                Note($"영상: {roll}번째 굴림 ({frame}장째)");
                yield return WaitFor(() => !board.DevDiceRolling, 5f, "주사위 멈춤");
                Note($"영상: 주사위 눈 {board.DevLastFace}");
                yield return new WaitForSeconds(1.5f); // 나온 눈 + 갈 칸 보여 주기
                if (!recording) break;
                board.DevMoveToQuietTile();
                // 다음 굴림이 가능해질 때까지 (이동·적 턴·턴 안내 판). 대화가 뜨면 넘김
                while (recording && !(board.DevIsIdle && !BoardTopBar.DevTurnDropVisible))
                {
                    if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
                    if (EncounterPanel.IsOpen) Note("영상: 조우 창 뜸 (적 턴에 적이 다가옴)");
                    yield return null;
                }
                yield return new WaitForSeconds(0.4f);
            }
            Time.captureFramerate = 0;
            Note($"영상 프레임 {frame}장 저장 → {dir}");
        }

        // ── 소리 영상 (-autoplay-soundvideo, 2026-11-07 사운드 22종 팀 공유용) ──
        // 조우가 나는 턴(주사위·발소리·조우)만 남기고 → 전투(음악·판정·명중) 몇 초. 화면 30fps + 게임 소리를 같이 저장.
        // AudioRenderer: 게임 시간(captureFramerate)에 맞춰 소리를 뽑아 화면과 어긋나지 않음. <out>/frames + <out>/audio.wav → Editor/FramesToVideo -wav.
        private IEnumerator SoundVideoRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();

            const int Fps = 30;
            string dir = Path.Combine(_outDir, "frames"), seg = Path.Combine(_outDir, "seg");
            Directory.CreateDirectory(dir);
            int channels = AudioSettings.speakerMode switch
            {
                AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
                AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, _ => 2,
            };
            int rate = AudioSettings.outputSampleRate;
            var kept = new System.Collections.Generic.List<float>();
            var segAudio = new System.Collections.Generic.List<float>();
            int keptFrames = 0, segFrames = 0;
            bool recording = false, running = true;

            IEnumerator Capture()
            {
                while (running)
                {
                    yield return new WaitForEndOfFrame();
                    int n = AudioRenderer.GetSampleCountForCaptureFrame();
                    var buf = new Unity.Collections.NativeArray<float>(n * channels, Unity.Collections.Allocator.Temp);
                    AudioRenderer.Render(buf); // 녹화 안 할 때도 매 장 비워 줘야 밀리지 않음
                    if (recording)
                    {
                        segAudio.AddRange(buf);
                        var tex = ScreenCapture.CaptureScreenshotAsTexture();
                        File.WriteAllBytes(Path.Combine(seg, $"{segFrames++:0000}.jpg"), tex.EncodeToJPG(92));
                        Destroy(tex);
                    }
                    buf.Dispose();
                }
            }
            void BeginSegment()
            {
                if (Directory.Exists(seg)) Directory.Delete(seg, true);
                Directory.CreateDirectory(seg);
                segAudio.Clear(); segFrames = 0; recording = true;
            }
            void KeepSegment()
            {
                recording = false;
                for (int i = 0; i < segFrames; i++)
                    File.Move(Path.Combine(seg, $"{i:0000}.jpg"), Path.Combine(dir, $"{keptFrames++:0000}.jpg"));
                kept.AddRange(segAudio);
            }

            Time.captureFramerate = Fps;
            AudioRenderer.Start();
            StartCoroutine(Capture());

            // 1) 조우가 나는 턴 찾기 — 턴마다 녹화하고, 조우가 안 나면 버림
            for (int turn = 0; turn < 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen, 20f, "굴림 대기");
                if (EncounterPanel.IsOpen) break;
                BeginSegment();
                yield return new WaitForSeconds(0.3f);
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 5f, "칸 선택");
                yield return new WaitForSeconds(0.4f);
                board.DevMoveTowardEncounter();
                yield return WaitFor(() => EncounterPanel.IsOpen || (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible), 15f, "이동");
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
                if (EncounterPanel.IsOpen) { Note($"소리 영상: {turn + 1}번째 턴에 조우"); break; }
                recording = false;
            }
            if (!recording) BeginSegment(); // 굴림 전에 이미 조우 창이 뜬 경우
            yield return new WaitForSeconds(1.2f); // 조우 창 소리
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            KeepSegment();
            float boardSeconds = keptFrames / (float)Fps;

            // 2) 전투 — 화면이 자리 잡을 때까지는 버리고, 그 뒤 몇 초
            BeginSegment();
            yield return new WaitForSeconds(1f);
            recording = false;
            BeginSegment();
            var fight = StartCoroutine(RunBattle(false));
            float battleSeconds = Mathf.Max(6f, 13f - boardSeconds);
            yield return new WaitForSeconds(battleSeconds);
            KeepSegment();
            StopCoroutine(fight);
            running = false;
            yield return null;
            AudioRenderer.Stop();
            Time.captureFramerate = 0;
            if (Directory.Exists(seg)) Directory.Delete(seg, true);

            WriteWav(Path.Combine(_outDir, "audio.wav"), kept, channels, rate);
            float peak = kept.Count > 0 ? kept.Max(Mathf.Abs) : 0f;
            Note($"소리 영상: 판 {boardSeconds:0.0}초 + 전투 {battleSeconds:0.0}초 = 프레임 {keptFrames}장, 소리 {kept.Count / channels / (float)rate:0.0}초 ({rate}Hz {channels}ch, 최대 {peak:0.00})");
            Note("효과음 불림: " + string.Join(", ", SfxPlayer.DevCalls.Select(p => $"{p.Key} {p.Value}")));
        }

        private static void WriteWav(string path, System.Collections.Generic.List<float> samples, int channels, int rate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Count * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
            w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float s in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }

        // ── 음악 반복 이음새 (-autoplay-musicloop [-musicloop-id BGM_Menu], 2026-11-10) ──
        // 곡을 처음부터 틀고 게임 시간으로 (곡 길이 + 3초) 소리를 뽑아, 반복 지점 앞뒤에 무음이 끼는지 잰다.
        // <out>/musicloop_seam.wav = 반복 지점 앞뒤 4초씩 (귀로 들어 보기용).
        private IEnumerator MusicLoopRun()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-musicloop-id");
            string id = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : MusicIds.Menu;
            float loopSec = id == MusicIds.Battle ? 96f : id == MusicIds.BossAbel ? 102.858f : id == MusicIds.FieldLondon ? 112.941f : 90f;
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");

            const int Fps = 50;
            int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            int rate = AudioSettings.outputSampleRate;
            var all = new System.Collections.Generic.List<float>((int)((loopSec + 4f) * rate * channels));
            Time.captureFramerate = Fps;
            AudioRenderer.Start();
            MusicPlayer.Stop(0f);
            yield return null;
            int startIndex = -1; // 곡 소리가 처음 나는 지점
            MusicPlayer.Play(id, 0f);
            float until = Time.time + loopSec + 3f;
            while (Time.time < until)
            {
                yield return new WaitForEndOfFrame();
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                var buf = new Unity.Collections.NativeArray<float>(n * channels, Unity.Collections.Allocator.Temp);
                AudioRenderer.Render(buf);
                all.AddRange(buf);
                buf.Dispose();
            }
            AudioRenderer.Stop();
            Time.captureFramerate = 0;

            const float Silence = 0.001f;
            int frames = all.Count / channels;
            bool Quiet(int f) { for (int c = 0; c < channels; c++) if (Mathf.Abs(all[f * channels + c]) >= Silence) return false; return true; }
            for (int f = 0; f < frames; f++) if (!Quiet(f)) { startIndex = f; break; }
            if (startIndex < 0) { Note($"음악 반복: {id} 소리 없음 (실패)"); yield break; }
            int seam = startIndex + Mathf.RoundToInt(loopSec * rate);
            // 반복 지점 ±1초 안에서 가장 긴 무음
            int longest = 0, run = 0, at = 0;
            for (int f = Mathf.Max(startIndex, seam - rate); f < Mathf.Min(frames, seam + rate); f++)
            {
                if (Quiet(f)) { run++; if (run > longest) { longest = run; at = f; } }
                else run = 0;
            }
            float ms = longest * 1000f / rate;
            Note($"음악 반복: {id} 곡 시작 {startIndex / (float)rate:0.000}초 · 반복 지점 {seam / (float)rate:0.000}초 · 지점 ±1초 안 가장 긴 무음 {ms:0.0}ms (위치 {at / (float)rate:0.000}초) → {(ms < 5f ? "통과" : "실패")}");
            var seg = all.Skip(Mathf.Max(0, seam - 4 * rate) * channels).Take(8 * rate * channels).ToList();
            WriteWav(Path.Combine(_outDir, "musicloop_seam.wav"), seg, channels, rate);
        }

        // ── 모션 점검 촬영 (-autoplay-motions, 2026-11-11): 전투에서 행동마다 진행 중 화면을 여러 장 (디자인 모션 요청서 자료) ──
        // 명탐정 공격 → 강화 스킬 / 역병 의사 회복 스킬 → 아이템 / 저격수 공격 → 방어 / 쓰러짐·부활 / 적 공격 1번.
        // 끝: 기본은 퇴각, -motions-win 이면 적 HP를 1로 만들어 승리 순간.
        private IEnumerator MotionShotsRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            for (int turn = 0; turn < 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen, 20f, "굴림 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 5f, "칸 선택");
                board.DevMoveTowardEncounter();
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "이동");
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
            }
            yield return new WaitForSeconds(0.4f);
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            var battle = FindFirstObjectByType<BattleController>();
            yield return new WaitForSeconds(1.5f);

            bool win = Environment.GetCommandLineArgs().Contains("-motions-win");
            StartCoroutine(WatchFirstEnemyAttack(battle));
            var done = new System.Collections.Generic.HashSet<string>();
            for (int guard = 0; guard < 40 && !battle.DevFinished; guard++)
            {
                yield return WaitFor(() => battle.DevAwaitingPlayer || battle.DevFinished, 40f, "아군 차례");
                if (battle.DevFinished) break;
                yield return new WaitForSeconds(0.5f);
                var actor = battle.DevCurrent;
                var job = actor.Member.Job.Id;
                var skills = actor.Member.Job.Skills ?? new Fantasia.Data.SkillDefinition[0];
                var enemy = battle.DevEnemies.FirstOrDefault(e => e.InBattle);
                string step = null;

                if (job == Fantasia.Data.JobId.Detective && done.Add("atk_detective")) { step = "attack"; battle.DevAttack(0); yield return BurstOnStep(battle, actor, "m_attack_detective"); }
                else if (job == Fantasia.Data.JobId.Sniper && done.Add("atk_sniper")) { step = "attack"; battle.DevAttack(0); yield return BurstOnStep(battle, actor, "m_attack_sniper"); }
                else if (job == Fantasia.Data.JobId.PlagueDoctor && done.Add("heal")
                         && skills.FirstOrDefault(s => s.Effect == Fantasia.Data.SkillEffect.Heal) is { } heal)
                {
                    step = "heal";
                    actor.TakeDamage(20); // 회복할 거리를 만듦
                    yield return Shot("m_heal_before");
                    battle.DevUseSkill(heal, actor, 0);
                    yield return BurstTimed("m_heal", 0.8f, 1.6f, 2.4f);
                }
                // 11/13 팀장 지적: 회복기를 다른 아군에게 쓸 때 모습이 이상함 → 다른 아군 회복을 촘촘히 촬영
                else if (job == Fantasia.Data.JobId.PlagueDoctor && done.Add("heal_other")
                         && skills.FirstOrDefault(s => s.Effect == Fantasia.Data.SkillEffect.Heal) is { } healOther
                         && battle.DevAllies.FirstOrDefault(a => a != actor && a.IsAlive) is { } mate)
                {
                    step = "heal_other";
                    mate.TakeDamage(20);
                    actor.RestoreCooldown(healOther, 0); // 앞 회복 촬영의 재사용 대기를 풀어 바로 쓰게
                    Note($"다른 아군 회복: {actor.DisplayName} → {mate.DisplayName}");
                    yield return Shot("m_healother_before");
                    battle.DevUseSkill(healOther, mate, 0);
                    yield return BurstTimed("m_healother", 0.2f, 0.5f, 0.8f, 1.1f, 1.4f, 1.8f, 2.2f, 2.8f);
                }
                else if (job == Fantasia.Data.JobId.Detective && done.Add("buff")
                         && skills.FirstOrDefault(s => s.Effect == Fantasia.Data.SkillEffect.ApplyStatus) is { } buff)
                {
                    step = "buff";
                    var target = buff.Target == Fantasia.Data.SkillTarget.SingleEnemy || buff.Target == Fantasia.Data.SkillTarget.AllEnemies ? enemy : actor;
                    Note($"강화 스킬 촬영: {buff.DisplayName} → {target?.DisplayName}");
                    battle.DevUseSkill(buff, target, 0);
                    yield return BurstTimed("m_buff", 0.8f, 1.6f, 2.4f);
                }
                else if (job == Fantasia.Data.JobId.PlagueDoctor && done.Add("item")
                         && BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition).FirstOrDefault(d => d.Effect == Fantasia.Data.ConsumableEffect.RestoreHP) is { } potion)
                {
                    step = "item";
                    actor.TakeDamage(20);
                    Note($"아이템 촬영: {potion.DisplayName} → {actor.DisplayName}");
                    battle.DevUseItem(potion, actor);
                    yield return BurstTimed("m_item", 0.2f, 0.6f, 1.2f);
                }
                else if (job == Fantasia.Data.JobId.Sniper && done.Add("defend")) { step = "defend"; battle.DevDefend(); yield return BurstTimed("m_defend", 0.15f, 0.35f, 0.7f); }
                else if (done.Add("revive"))
                {
                    step = "revive";
                    var victim = battle.DevAllies.FirstOrDefault(a => a.InBattle && a != actor);
                    battle.DevKnockDownAlly(victim);
                    yield return new WaitForSeconds(2.3f);
                    yield return Shot("m_revive_down");
                    battle.DevRevive(victim);
                    yield return BurstTimed("m_revive", 0.2f, 0.7f, 1.3f) /* 11/11 일어서기 동작 1.5초 */;
                }
                else if (!win)
                {
                    step = "retreat";
                    battle.DevRetreat();
                    yield return WaitFor(() => battle.DevAllies.Any(a => a.HasRetreated) || battle.DevAwaitingPlayer, 12f, "퇴각 판정");
                    if (battle.DevAllies.Any(a => a.HasRetreated)) { yield return BurstTimed("m_retreat", 0.1f, 0.5f, 0.85f) /* 11/13 돌기 0.2 + 걸어서 물러남 0.8초 */; break; }
                    continue;
                }
                else
                {
                    step = "win";
                    foreach (var e in battle.DevEnemies.Where(e => e.InBattle)) battle.DevSetEnemyHP1(e);
                    bool last = battle.DevEnemies.Count(e => e.InBattle) == 1;
                    battle.DevAttack(0);
                    if (last)
                    {
                        yield return WaitFor(() => !battle.DevEnemies.Any(e => e.IsAlive), 10f, "마지막 적 처치");
                        yield return BurstTimed("m_win", 0.3f, 1.0f, 1.7f);
                        break;
                    }
                }
                yield return new WaitForSeconds(0.6f);
                // 쓸 수 없는 행동이었으면(비용·쿨다운) 기본 공격으로 넘김
                if (battle.DevAwaitingPlayer && battle.DevCurrent == actor) { Note($"  {step} 못 씀 → 기본 공격"); battle.DevAttack(0); }
            }
            Note("모션 점검 촬영 끝");
        }

        // 반 걸음이 시작되는 순간(모델이 움직이기 시작)부터 0.05·0.3·0.55초에 찍음 — 굴림 창 길이와 상관없이 공격 동작에 맞춤
        private IEnumerator BurstOnStep(BattleController battle, BattleUnit unit, string name)
        {
            var model = battle.DevModelOf(unit);
            if (model == null) yield break;
            var home = model.transform.position;
            yield return WaitFor(() => (model.transform.position - home).sqrMagnitude > 0.0025f, 8f, "공격 시작");
            yield return BurstTimed(name, 0.05f, 0.3f, 0.55f);
        }

        private IEnumerator BurstTimed(string name, params float[] times)
        {
            float t0 = Time.time;
            for (int i = 0; i < times.Length; i++)
            {
                while (Time.time - t0 < times[i]) yield return null;
                yield return Shot($"{name}_{i + 1}");
            }
        }

        private IEnumerator WatchFirstEnemyAttack(BattleController battle)
        {
            var models = battle.DevEnemies.Select(e => battle.DevModelOf(e)).Where(m => m != null).ToList();
            var homes = models.Select(m => m.transform.position).ToList();
            for (float t = 0f; t < 120f && !battle.DevFinished; t += Time.deltaTime)
            {
                for (int i = 0; i < models.Count; i++)
                    if (models[i] != null && (models[i].transform.position - homes[i]).sqrMagnitude > 0.0025f)
                    {
                        yield return BurstTimed("m_enemy_attack", 0.05f, 0.3f, 0.55f);
                        yield break;
                    }
                yield return null;
            }
        }

        // ── 전투 움직임 영상 (-autoplay-battlevideo, 2026-11-07): 새 게임 → 가까운 적 칸 → 전투 20초 녹화 ──
        // (받은 Idle·Attack·Hit·Death가 실제 전투에서 나오는지 팀 공유용)
        private IEnumerator BattleVideoRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            for (int turn = 0; turn < 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen, 20f, "굴림 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 5f, "칸 선택");
                board.DevMoveTowardEncounter();
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "이동");
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
            }
            yield return new WaitForSeconds(0.4f);
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            yield return new WaitForSeconds(1.5f); // 전투 화면이 자리 잡을 때까지

            string dir = Path.Combine(_outDir, "frames");
            Directory.CreateDirectory(dir);
            const int Fps = 30, Seconds = 20;
            bool recording = true;
            int frame = 0;
            Time.captureFramerate = Fps;
            StartCoroutine(RecordFrames(dir, Fps * Seconds, () => frame++, () => recording = false));
            var fight = StartCoroutine(RunBattle(false));
            while (recording) yield return null;
            StopCoroutine(fight);
            Time.captureFramerate = 0;
            Note($"영상 프레임 {frame}장 저장 → {dir}");
        }

        private IEnumerator RecordFrames(string dir, int count, Action counted, Action done)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"{i:0000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
                counted();
            }
            done();
        }

        private IEnumerator SkipIntro()
        {
            yield return WaitFor(() => DialoguePanel.IsOpen, 3f, "시작 대화");
            if (!DialoguePanel.IsOpen) yield break;
            yield return Shot("intro_dialogue");
            int lines = 0;
            if (DialoguePanel.IsOpen && DialoguePanel.DevLineIndex == 0)
            {
                // 11/12 팀장 지시: 대화 창 아무 곳(글 부분)을 눌러도 다음 줄
                _boxClickChecked = true;
                DialoguePanel.DevClickBox();
                yield return null;
                Qa(!DialoguePanel.IsOpen || DialoguePanel.DevLineIndex == 1, $"대화 창 글 부분을 누르면 다음 줄 (줄 0 → {DialoguePanel.DevLineIndex})");
                lines++;
                yield return new WaitForSeconds(0.15f);
            }
            while (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); lines++; yield return new WaitForSeconds(0.15f); }
            Note($"시작 대화 {lines}번 넘김");
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string what)
        {
            float end = Time.time + timeout;
            while (!condition() && Time.time < end) yield return null;
            if (!condition()) Note($"시간 초과: {what}");
        }

        private bool _boxClickChecked;

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_outDir, $"{++_shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            FrameSpikeLog.LastShotFrame = Time.frameCount;
            Note($"스크린샷 {Path.GetFileName(file)}");
            foreach (var line in TextCoverCheck.Run()) Note($"[글자] {name}: {line}"); // 11/11 글자 가림 점검
            yield return null;
        }

        private void Note(string line)
        {
            _log.AppendLine(line);
            Debug.Log("[AutoPlay] " + line);
        }
    }
}
