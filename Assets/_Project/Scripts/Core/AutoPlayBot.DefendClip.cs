using System.Collections;
using System.IO;
using System.Linq;
using Fantasia.Board;
using Fantasia.Combat;
using Fantasia.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.Core
{
    // 11/14 팀원 공유용 영상 (-autoplay-defend-clip): 전투에서 [방어]를 눌렀을 때 화면이 어떻게 바뀌는지 약 11초.
    // 프레임마다 PNG로 저장(Time.captureFramerate 30 고정 → 게임 시간과 영상 시간이 같음) → 밖에서 ffmpeg로 mp4.
    public partial class AutoPlayBot
    {
        private const int ClipFps = 30;

        private IEnumerator DefendClipRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            if (Seed >= 0) GameRandom.Seed(Seed);
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
            EncounterPanel.DevChoose(0); // 싸우기
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            var battle = FindFirstObjectByType<BattleController>();
            yield return WaitFor(() => battle.DevAwaitingPlayer || battle.DevFinished, 40f, "아군 차례");
            yield return new WaitForSeconds(1.5f);

            // ── 녹화 시작 ──
            string dir = Path.Combine(_outDir, "frames");
            Directory.CreateDirectory(dir);
            Time.captureFramerate = ClipFps;
            bool recording = true;
            StartCoroutine(RecordFrames(dir, () => recording));

            var actor = battle.DevCurrent;
            Note($"방어 영상: {actor.DisplayName} 차례에 방어");
            yield return new WaitForSeconds(1.5f); // 행동 창을 잠깐 보여 줌
            var button = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "방어Button" && b.gameObject.activeInHierarchy);
            var ev = new PointerEventData(EventSystem.current);
            if (button != null)
            {
                ExecuteEvents.Execute(button.gameObject, ev, ExecuteEvents.pointerEnterHandler); // 마우스를 올린 모습
                yield return new WaitForSeconds(0.6f);
                ExecuteEvents.Execute(button.gameObject, ev, ExecuteEvents.pointerDownHandler); // 눌린 모습
                yield return new WaitForSeconds(0.2f);
                ExecuteEvents.Execute(button.gameObject, ev, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button.gameObject, ev, ExecuteEvents.pointerExitHandler);
            }
            battle.DevDefendButton();
            Note($"방어 뒤 보호막: {actor.Shield}");

            // 남은 시간: 다른 아군은 기본 공격, 적 차례는 그대로 둠 (적이 방어한 아군을 치면 보호막이 막는 모습)
            float end = Time.time + 8.5f;
            while (Time.time < end && !battle.DevFinished)
            {
                if (battle.DevAwaitingPlayer && battle.DevCurrent != actor)
                {
                    yield return new WaitForSeconds(0.8f);
                    if (battle.DevAwaitingPlayer) battle.DevAttack(focus: 0);
                }
                yield return null;
            }
            recording = false;
            yield return null;
            Time.captureFramerate = 0;
            Note($"방어 영상 끝: 프레임 {Directory.GetFiles(dir, "*.png").Length}장 · 보호막 {actor.Shield}");
            Application.Quit();
        }

        private IEnumerator RecordFrames(string dir, System.Func<bool> on)
        {
            int i = 0;
            while (on())
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f{i++:D4}.png"));
            }
        }
    }
}
