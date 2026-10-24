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

        // 퀘스트 (☆ 제목·목표 문구 중 "제임스에게 보고"·"베르사유의 운반 기록 추적"만 기획 확정, 나머지는 개발 임시)
        public const string QuestCase = "제임스의 의뢰";
        public const string QuestCaseObjective = "폐검시소(M11)의 검시관을 찾아 부인의 사건을 밝혀라";
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
