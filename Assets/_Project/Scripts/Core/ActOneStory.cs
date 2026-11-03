using Fantasia.UI;

namespace Fantasia.Core
{
    // 1막 런던 진행 (Docs/Planning/Boss_Abel_v1.0.md 기획 확정 2026-10-25 — 대사는 본 문구 그대로).
    // 진행 플래그는 BoardSession.StoryFlags에 저장: 최초 등장 확인 / 모로 처치 / 최초 보상 지급 / 제임스 보고(= 1막 완료·다음 지역 해금).
    // 증거품은 BoardSession.KeyItems (검시 기록·붉은 룬 조각).
    public static class ActOneStory
    {
        public const string BossId = "enemy.boss.abel_moreau";
        public const string BossBannerAct = "1막 보스";
        public const string BossBannerName = "검시관 아벨 모로";
        public const string HubLabel = "S05";        // 제임스에게 보고하는 곳 (☆ 런던 거점 — 기획에 장소 미지정)
        public const int RecommendedLevel = 3;
        public const string LevelWarning = "권장 레벨 3입니다. 회복·해독 수단을 준비하세요.";

        // 진행 플래그
        public const string FlagIntroSeen = "act1.abel.intro_seen";
        public const string FlagBossDefeated = "act1.abel.defeated";
        public const string FlagRewardGiven = "act1.abel.reward_given";
        public const string FlagReported = "act1.james_reported"; // 1막 완료 + 베르사유 해금
        public const string FlagIntroDone = "act1.intro_done";               // 1막 시작 대화를 끝까지 봄 (건너뛰기 포함)
        public const string FlagAfterDialogueDone = "act1.abel.after_dialogue_done"; // 처치 후 대화 — 도중 종료면 다시 재생 (결정서 T16-35)

        // 퀘스트 (메인 의뢰 목표는 1막 기획 결정서 05 — 아벨 모로를 범인으로 단정하지 않음)
        public const string QuestCase = "제임스의 의뢰";
        public const string QuestCaseObjective = "폐검시소(M11)의 검시 기록을 조사해 제임스 부인의 사건을 밝혀라";

        // 게임 오버·1막 클리어 문구 (결정서 06)
        public const string GameOverTitle = "조사대 전멸";
        public const string GameOverBody = "이 사건의 기록은 여기서 멈췄다.\n파티의 모든 캐릭터가 영구 사망해 이번 회차가 종료되었습니다.";
        public const string ClearSummary = "검시소의 범행은 멈췄다. 그러나 장부의 운반 기록은 베르사유를 가리킨다.";
        public const string ClearNext = "2막 베르사유 — 추후 공개";

        // 1막 시작 대화 (결정서 05) — 새 게임 → 이름 입력 뒤 S05에서. 내레이션은 초상 없음
        public static readonly DialoguePanel.DialogueLine[] Intro =
        {
            new("내레이션", "1774년, 런던. 안개가 걷히지 않는 거리에서 또 한 명의 이름이 신문에 실렸다."),
            new("내레이션", "피해자들은 달랐지만, 시신에 남은 기이한 상처는 같은 문양을 그리고 있었다."),
            new("제임스", "제 아내의 죽음을 조사해 주십시오. 기록에는 사고라고 적혔지만, 그 상처는 사고로 생길 수 없습니다."),
            new("명탐정", "부인에게서 발견한 물건과 마지막 행적을 모두 말씀해 주십시오. 빠진 기록부터 찾아보겠습니다."),
            new("제임스", "검시 기록의 일부가 지워져 있었습니다. 아내의 시신이 어디로 옮겨졌는지도 알려 주지 않습니다."),
            new("역병 의사", "상처의 흔적이 심상치 않군요. 직접 기록과 시약을 확인해야겠어요."),
            new("저격수", "길은 제가 살피겠습니다. 흔적을 지우기 전에 움직이죠."),
            new("명탐정", "먼저 폐검시소다. 기록을 숨긴 이유부터 밝히겠다."),
        };
        public const string QuestReport = "제임스에게 보고";
        public const string QuestReportObjective = "런던 그리니치 거점(S05)으로 돌아가 제임스에게 알린다";
        public const string QuestNext = "베르사유의 운반 기록 추적";
        public const string QuestNextObjective = "다음 목적지 베르사유 — 2막에서 공개 (출구 K12)";

        public static readonly DialoguePanel.DialogueLine[] BeforeBattle =
        {
            new("명탐정", "검시 기록을 고친 건 당신이군. 제임스 부인의 시신은 어디로 보냈지?"),
            new("아벨 모로", "시신만 보는 눈으로는 이해할 수 없지. 그분들이 원한 건 껍데기가 아니야."),
            new("명탐정", "이 문양… 현장의 상처와 같은 룬이다. 여기서 끝내겠다."),
            new("아벨 모로", "그렇다면 너희도 다음 장부에 올려 주마."),
        };

        public static readonly DialoguePanel.DialogueLine[] AfterBattle =
        {
            new("아벨 모로", "장부를 가져가도… 운반은 이미 시작됐어."),
            new("명탐정", "피해자 명단… 제임스의 부인도 있어. 베르사유 귀족의 인장이군."),
            new("역병 의사", "이 룬 조각은 영혼을 가두는 용기와 연결돼 있어요."),
            new("명탐정", "검시소는 시작에 불과했어. 제임스에게 알리고, 운반 기록을 추적하자."),
        };

        public static readonly DialoguePanel.DialogueLine[] ReportToJames =
        {
            new("제임스", "아내에게… 대체 무슨 일이 있었던 겁니까?"),
            new("명탐정", "검시소의 범행은 멈췄습니다. 하지만 지시한 자는 따로 있습니다. 남은 기록을 따라 끝까지 추적하겠습니다."),
        };

        public static bool IsActOneBoss(Fantasia.Data.EnemyDefinition e) => e != null && e.Id == BossId;

        // 보스 처치 확정 (보상·증거·퀘스트를 한 번에, 중복 없이). 대사·보상 창보다 먼저 호출.
        public static void MarkBossDefeated(BoardSession s)
        {
            if (!s.StoryFlags.Add(FlagBossDefeated)) return;
            s.CompleteQuest(QuestCase);
            s.SetQuest(QuestReport, QuestReportObjective, QuestType.Main);
        }

        public static bool NeedsReport(BoardSession s) => s.StoryFlags.Contains(FlagBossDefeated) && !s.StoryFlags.Contains(FlagReported);

        // 제임스 보고 완료 → 1막 클리어 및 다음 목적지 베르사유 안내 (실제 진입은 2막 구현 후 — 세이브 추가 수정안). 다시 불러도 중복 없음.
        public static void CompleteReport(BoardSession s)
        {
            if (!NeedsReport(s)) return;
            s.StoryFlags.Add(FlagReported);
            s.CompleteQuest(QuestReport);
            s.SetQuest(QuestNext, QuestNextObjective, QuestType.Main);
            s.Run = BoardSession.RunState.ActCleared; // 클리어 저장 → 결과 화면 → 메인 메뉴, 이어하기 = 결과 화면
        }
    }
}
