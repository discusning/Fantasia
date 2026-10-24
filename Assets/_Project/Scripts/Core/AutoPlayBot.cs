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
            Note($"자동 플레이 시작 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 해상도 {Screen.width}x{Screen.height}");

            yield return Play();

            File.WriteAllText(Path.Combine(_outDir, "autoplay_log.txt"), _log.ToString(), Encoding.UTF8);
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        private IEnumerator Play()
        {
            // ── 1. 보드 ──
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 씬 로드");
            yield return new WaitForSeconds(1f);
            var board = FindFirstObjectByType<BoardTestController>();
            yield return Shot("board_start");
            ApplyStartOverrides();
            NoteParty("보드 시작");

            for (int turn = 1; turn <= 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 10f, "이동 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                Note($"보드 {turn}턴: 월드 라운드 {board.DevWorldRound} 아군 턴 — 주사위 굴림");
                if (turn == 1) yield return Shot("board_roll_highlight");
                board.DevMoveTowardEncounter();
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen || board.DevAwaitingSelection, 15f, "이동");
                // 이벤트 칸 추가 이동이 생기면 그것도 인카운터 쪽으로.
                if (board.DevAwaitingSelection && !EncounterPanel.IsOpen)
                {
                    Note("  이벤트 칸 추가 이동");
                    board.DevMoveTowardEncounter();
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
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투 관리자");
            var battle = FindFirstObjectByType<BattleController>();
            bool shotFirstTurn = false, shotRoll = false, shotEnemy = false, defended = false, shotSkillMenu = false, shotSkill = false, shotRevive = false;
            float deadline = Time.time + 180f;

            while (!battle.DevFinished && Time.time < deadline)
            {
                if (battle.DevAwaitingPlayer)
                {
                    yield return new WaitForSeconds(0.3f);
                    if (!shotFirstTurn) { shotFirstTurn = true; yield return Shot("battle_first_turn"); }

                    var actor = battle.DevCurrent;
                    // 스킬을 쓸 상황이면 스킬(D11), 아니면 기본 공격 — 집중력이 남으면 추가 집중을 켠다.
                    var (skill, target) = ChooseSkill(battle, actor);
                    var downed = battle.DevAllies.FirstOrDefault(a => a.Member.IsDowned && a.Member.CurrentLP > 0);
                    if (battle.DevCanRevive && downed != null && !Environment.GetCommandLineArgs().Contains("-autoplay-no-revive"))
                    {
                        // 쓰러진 동료가 있으면 먼저 부활 (D21)
                        battle.DevRevive(downed);
                        if (!shotRevive)
                        {
                            shotRevive = true;
                            yield return new WaitForSeconds(0.4f);
                            yield return Shot("battle_revive");
                        }
                    }
                    else if (skill != null)
                    {
                        if (!shotSkillMenu)
                        {
                            shotSkillMenu = true;
                            battle.DevHud.DevOpenSkills();
                            yield return new WaitForSeconds(0.2f);
                            yield return Shot("battle_skill_menu");
                        }
                        battle.DevUseSkill(skill, target, focus: actor.Member.CurrentFocus > skill.FocusCost);
                        if (!shotSkill)
                        {
                            shotSkill = true;
                            yield return new WaitForSeconds(0.6f);
                            yield return Shot($"battle_skill_{skill.Id.Split('.').Last()}");
                        }
                    }
                    else if (!defended && actor.Member.Job.Id == Fantasia.Data.JobId.PlagueDoctor && actor.Member.CurrentMP < 12)
                    {
                        defended = true;
                        battle.DevDefend();
                    }
                    else
                    {
                        battle.DevAttack(focus: actor.Member.CurrentFocus > 0);
                        if (!shotRoll)
                        {
                            shotRoll = true;
                            yield return new WaitForSeconds(0.5f);
                            yield return Shot("battle_player_roll");
                            yield return new WaitForSeconds(1.2f);
                            yield return Shot("battle_after_hit");
                        }
                    }
                }
                else if (!shotEnemy && battle.DevCurrent != null && !battle.DevCurrent.IsAlly)
                {
                    shotEnemy = true;
                    yield return new WaitForSeconds(0.9f);
                    yield return Shot("battle_enemy_turn");
                }
                yield return null;
            }
            if (!battle.DevFinished) { Note("실패: 전투가 180초 안에 끝나지 않음"); yield return Shot("battle_timeout"); yield break; }

            // ── 4. 결과 → 보드 복귀 ──
            yield return new WaitForSeconds(0.8f);
            yield return Shot("battle_result");
            battle.DevContinue();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "BoardTest" && FindFirstObjectByType<BoardTestController>() != null, 10f, "보드 복귀");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("board_after_battle");
            NoteParty("보드 복귀 후");
            Note("자동 플레이 완료");
        }

        // 사람이 할 법한 스킬 사용 — 쓸 수 있는(비용·쿨다운 OK) 스킬 중 상황에 맞는 것 하나. 없으면 (null, null).
        private static (Fantasia.Data.SkillDefinition, BattleUnit) ChooseSkill(BattleController battle, BattleUnit actor)
        {
            Fantasia.Data.SkillDefinition Usable(string id) =>
                actor.ActiveSkills().FirstOrDefault(s => s.Id == id && actor.CannotUseReason(s) == null);

            var enemies = battle.DevEnemies.Where(e => e.InBattle).ToList();
            var allies = battle.DevAllies.Where(a => a.InBattle).ToList();
            if (enemies.Count == 0) return (null, null);
            var evasive = enemies.FirstOrDefault(e => e.HasEvasionState);
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
        }

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
