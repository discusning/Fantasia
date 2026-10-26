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
    public class AutoPlayBot : MonoBehaviour
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
            if (resumeIndex >= 0 && resumeIndex + 1 < bootArgs.Length && File.Exists(bootArgs[resumeIndex + 1])) File.Copy(bootArgs[resumeIndex + 1], SaveGame.SavePath, true);
            var go = new GameObject("AutoPlayBot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPlayBot>();
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

            if (args.Contains("-autoplay-full-resume")) yield return FullRun(resume: true);
            else if (args.Contains("-autoplay-resume")) yield return ResumeRun();
            else if (args.Contains("-autoplay-menu")) yield return MenuRun();
            else if (args.Contains("-autoplay-full")) yield return FullRun(resume: false);
            else yield return Play();

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
            yield return Shot("full_start");
            Note((resume ? "통플레이 이어하기 — 파티: " : "통플레이 시작 — 파티: ") + string.Join(", ", BoardSession.Instance.Party.Members.Select(m => m.Job.DisplayName)));

            while (Time.realtimeSinceStartup < limit)
            {
                yield return null;
                if (ActClearPanel.IsOpen)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot("full_act_clear");
                    ending = "1막 클리어";
                    ActClearPanel.DevClose();
                    yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
                    break;
                }
                if (FindFirstObjectByType<MainMenuController>() != null) { ending = "메인 메뉴로 돌아감 (전멸 등)"; break; }
                if (SaveErrorPanel.IsOpen) { Note("저장 실패 창 — 재시도"); SaveErrorPanel.DevRetry(); yield return new WaitForSecondsRealtime(0.5f); continue; }

                if (SceneManager.GetActiveScene().name == "CombatTest")
                {
                    battles++;
                    yield return RunBattle(shots: false);
                    var b = _lastBattle;
                    if (b == null || !b.DevFinished) { Note("실패: 전투가 끝나지 않음"); yield return Shot("full_battle_stuck"); ending = "전투 멈춤"; break; }
                    if (b.DevEnemies.All(e => !e.IsAlive)) wins++;
                    yield return new WaitForSeconds(0.6f);
                    b.DevContinue();
                    yield return WaitFor(() => SceneManager.GetActiveScene().name != "CombatTest", 10f, "전투 뒤 씬 전환");
                    yield return new WaitForSeconds(1f);
                    NoteParty($"전투 {battles} 뒤");
                    continue;
                }

                var board = FindFirstObjectByType<BoardTestController>();
                if (board == null) continue;
                if (DialoguePanel.IsOpen) { DialoguePanel.DevAdvance(); yield return new WaitForSeconds(0.15f); continue; }
                if (ConfirmPanel.IsOpen) { Note("보스 레벨 경고 → 진입"); ConfirmPanel.DevChoose(true); continue; }
                if (EncounterPanel.IsOpen) { yield return new WaitForSeconds(0.4f); EncounterPanel.DevChoose(1); yield return new WaitForSeconds(0.8f); continue; }
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
                    yield return null;
                    if (board.DevAwaitingSelection)
                    {
                        turns++;
                        if (turns % 10 == 0) NoteParty($"보드 {turns}턴 (월드 라운드 {board.DevWorldRound})");
                    }
                }
            }

            var s = BoardSession.Instance;
            Note($"통플레이 결과: {ending} · 보드 턴 {turns} · 월드 라운드 {s?.World.WorldRound} · 전투 {battles} (승리 {wins}) · 상점 방문 {shopVisits} · 이벤트 {events}"
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

        // 거점 상점: 영약·해독제 조금 사고, 많이 다쳤으면 숙박 (숙박은 턴을 끝냄)
        private IEnumerator ShopVisit()
        {
            var ui = ShopPanel.Instance;
            var party = BoardSession.Instance.Party;
            int before = party.Cent;
            ui.DevSelectTab(0);
            for (int i = 0; i < 2 && party.Cent >= 90; i++) { ui.DevSelectRow(1); ui.DevAct(); } // 생명의 영약
            if (party.Cent >= 65) { ui.DevSelectRow(4); ui.DevAct(); }                            // 해독제
            yield return new WaitForSeconds(0.3f);
            bool rest = party.AliveMembers.Any(m => m.CurrentHP < m.MaxHP * 0.6f || (m.MaxMP > 0 && m.CurrentMP < m.MaxMP * 0.4f)) && party.Cent >= 30 * party.AliveMembers.Count();
            Note($"  상점: Cent {before} → {party.Cent}{(rest ? " · 숙박" : "")}");
            if (rest) { ui.DevSelectTab(3); ui.DevSelectRow(0); ui.DevAct(); }
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

            // 저장 실패: 임시 파일 자리에 폴더를 만들어 쓰기를 막는다 → 경고·일시정지 → 막힌 것 풀고 재시도 → 재개
            string blocker = SaveGame.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            menu.DevOpenPartySelect();
            yield return new WaitForSecondsRealtime(0.5f);
            menu.OnDepart(); // 새 게임 저장 → 실패
            yield return new WaitForSecondsRealtime(0.8f);
            Note($"저장 실패 창 {(SaveErrorPanel.IsOpen ? "열림" : "안 열림")} · timeScale {Time.timeScale}");
            yield return Shot("save_error");
            SaveErrorPanel.DevRetry(); // 아직 막힘 → 계속 일시정지
            yield return new WaitForSecondsRealtime(0.3f);
            Note($"막힌 채 재시도 → 창 {(SaveErrorPanel.IsOpen ? "유지" : "닫힘")} · timeScale {Time.timeScale}");
            Directory.Delete(blocker);
            SaveErrorPanel.DevRetry();
            yield return new WaitForSecondsRealtime(0.5f);
            Note($"풀고 재시도 → 창 {(SaveErrorPanel.IsOpen ? "유지" : "닫힘")} · timeScale {Time.timeScale} · 저장 파일 {(SaveGame.HasSave ? "있음" : "없음")}");
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
                menu.OnDepart();
                if (Seed >= 0) GameRandom.Seed(Seed); // 새 게임이 난수를 새로 정하므로 같은 시드로 다시
                Note($"출발: {menu.DevPartyNames}");
            }

            // ── 1. 보드 ──
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 씬 로드");
            yield return new WaitForSeconds(1f);
            var board = FindFirstObjectByType<BoardTestController>();
            yield return Shot("board_start");
            ApplyStartOverrides();
            NoteParty("보드 시작");
            var args = Environment.GetCommandLineArgs();
            int startIndex = Array.IndexOf(args, "-autoplay-start");
            if (startIndex >= 0 && startIndex + 1 < args.Length) { board.DevTeleport(args[startIndex + 1]); Note($"시작 위치 → {args[startIndex + 1]}"); yield return new WaitForSeconds(0.5f); }
            bool bossRun = args.Contains("-autoplay-boss");
            bool huntRun = args.Contains("-autoplay-hunt");
            bool shotHunt = false, shotSky = false;

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
                yield return new WaitForSeconds(0.5f);
                Note($"[이용]: {board.DevResultMessage} → {State()}");
                yield return Shot("recovery_used");
                board.DevArriveAt("S16");
                yield return new WaitForSeconds(1f);
                Note($"다시 도착: 창 {(ConfirmPanel.IsOpen ? "열림" : "안 열림")}");
                int cent = tp.Cent;
                board.DevArriveAt("J06");
                yield return new WaitForSeconds(1f);
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
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 10f, "이동 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                Note($"보드 {turn}턴: 월드 라운드 {board.DevWorldRound} 아군 턴 — 주사위 굴림");
                if (turn == 1) yield return Shot("board_roll_highlight");
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

        private IEnumerator RunBattle(bool shots)
        {
            var args = Environment.GetCommandLineArgs();
            _lastBattle = null;
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투 관리자");
            var battle = FindFirstObjectByType<BattleController>();
            _lastBattle = battle;
            bool shotFirstTurn = false, shotRoll = false, shotEnemy = false, defended = false, shotSkillMenu = false, shotSkill = false, shotRevive = false, usedItem = false;
            bool shotBanner = false, shotIntro = false, shotTelegraph = false, focusZeroed = false;
            bool kit = args.Contains("-autoplay-kit"), forceInterrupt = args.Contains("-autoplay-interrupt"), focusZero = args.Contains("-autoplay-focus0");
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
                    if (!shotFirstTurn) { shotFirstTurn = true; if (shots) yield return Shot("battle_first_turn"); }
                    // 촬영용 (-autoplay-wipe): 아군 전원을 쓰러뜨려 패배·게임 오버 화면을 띄운다
                    if (Environment.GetCommandLineArgs().Contains("-autoplay-wipe"))
                    {
                        foreach (var m in BoardSession.Instance.Party.AliveMembers.ToList()) m.TakeDamage(m.CurrentHP);
                        battle.DevCheckEnd();
                        Note("촬영용: 아군 전원 쓰러뜨림");
                        continue;
                    }

                    var actor = battle.DevCurrent;
                    // 스킬을 쓸 상황이면 스킬(D11), 아니면 기본 공격 — 집중력이 남으면 추가 집중을 켠다.
                    var (skill, target) = ChooseSkill(battle, actor);
                    var downed = battle.DevAllies.FirstOrDefault(a => a.Member.IsDowned && a.Member.CurrentLP > 0);
                    // 다친 동료가 있으면 회복 소모품 한 번 (D17) — 플레이테스트 준비물(-autoplay-kit)이면 HP 50% 아래마다
                    var potionTarget = battle.DevAllies.Where(a => a.InBattle).OrderBy(a => (float)a.CurrentHP / a.MaxHP).FirstOrDefault();
                    var potion = BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition)
                        .FirstOrDefault(d => d.Effect == Fantasia.Data.ConsumableEffect.RestoreHP);
                    var poisoned = battle.DevAllies.Where(a => a.InBattle).FirstOrDefault(a => a.Statuses.All.Any(s => s.Effect == Fantasia.Data.StatusEffectType.Poison && s.Stacks >= 2));
                    var antidote = BoardSession.Instance.Party.Inventory.Consumables.Select(s => s.Definition).FirstOrDefault(d => d.Id == Fantasia.Data.ConsumableIds.Antidote);
                    bool breakerReady = skill != null && telegraphBoss != null && target == telegraphBoss && skill.Coefficient >= telegraphBoss.Boss.Pending.CancelMinCoefficient;
                    if (telegraphBoss != null && battle.DevHud.DevInterruptVisible && (forceInterrupt || !breakerReady))
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
                        battle.DevUseItem(potion, potionTarget);
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
                        }
                        if (skill.Effect == Fantasia.Data.SkillEffect.Heal || skill.Effect == Fantasia.Data.SkillEffect.Cleanse) healsUsed++;
                        bool bossNow = battle.DevEnemies.Any(e => e.InBattle && e.Boss != null) && !(target != null && target.Boss != null && target.Boss.HasPending);
                        battle.DevUseSkill(skill, target, focus: actor.Member.CurrentFocus > skill.FocusCost + (bossNow ? 1 : 0));
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
                        else battle.DevAttack(focus: actor.Member.CurrentFocus > (bossFight ? 1 : 0));
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
            yield return new WaitForSeconds(0.5f);
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

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string what)
        {
            float end = Time.time + timeout;
            while (!condition() && Time.time < end) yield return null;
            if (!condition()) Note($"시간 초과: {what}");
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(_outDir, $"{++_shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            Note($"스크린샷 {Path.GetFileName(file)}");
            yield return null;
        }

        private void Note(string line)
        {
            _log.AppendLine(line);
            Debug.Log("[AutoPlay] " + line);
        }
    }
}
