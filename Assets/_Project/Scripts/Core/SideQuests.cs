using System.Collections.Generic;
using System.Linq;
using Fantasia.Board;

namespace Fantasia.Core
{
    // 런던 선택 의뢰 2개 (Docs/Planning/Fantasia_1막_기획_결정서_2026-11-01.pdf 04 A3 — 문구 그대로).
    // 기존 적 칸 재사용. 두 좌표 전투 승리(ClearedEncounters) = 목표 달성 → 자동 완료, 보상 80 Cent + 생존 파티원 각각 경험치 40, 회차당 1회.
    // 진행은 BoardSession.StoryFlags (수락·완료) — 저장·불러오기로 보상을 반복하지 않는다. 보스 진입 조건 아님.
    public static class SideQuests
    {
        public const int RewardCent = 80;
        public const int RewardExp = 40;

        public sealed class Def
        {
            public string Id, Title, Objective, AcceptLine, CompleteLine;
            public string[] Targets; // 승리해야 하는 지도 좌표
        }

        public static readonly Def[] All =
        {
            new Def
            {
                Id = "LDN_SIDE_01", Title = "검역 구역의 역병 쥐",
                Objective = "P04와 Q01 전투에 각각 승리한다 (각 칸 역병 쥐 2마리)",
                Targets = new[] { "P04", "Q01" },
                AcceptLine = "감염된 쥐 떼가 수사를 방해하고 있습니다. P04와 Q01의 위협을 정리해 주세요.",
                CompleteLine = "두 지점의 감염 흔적을 정리했다. 검역 기록에 조사대의 성과가 남았다.",
            },
            new Def
            {
                Id = "LDN_SIDE_02", Title = "연성 독거미 발생 조사",
                Objective = "O07과 W04 전투에 각각 승리한다 (O07: 역병 쥐 1 + 독거미 유체 1, W04: 독거미 유체 2)",
                Targets = new[] { "O07", "W04" },
                AcceptLine = "독낭이 부푼 거미가 나타났습니다. O07과 W04를 조사하고 위협을 제거해 주세요.",
                CompleteLine = "독낭과 몸체의 변형은 자연적인 것이 아니다. 조사 결과를 검역 기록에 덧붙였다.",
            },
        };

        private static string AcceptedFlag(Def q) => $"side.{q.Id}.accepted";
        private static string DoneFlag(Def q) => $"side.{q.Id}.done";

        public static bool IsAccepted(BoardSession s, Def q) => s.StoryFlags.Contains(AcceptedFlag(q));
        public static bool IsDone(BoardSession s, Def q) => s.StoryFlags.Contains(DoneFlag(q));

        // 목표 칸 중 이긴 칸 수 (퇴각·몰래 지나가기·패배는 ClearedEncounters에 안 들어감)
        public static int TargetsWon(BoardSession s, Def q) =>
            q.Targets.Count(label => WorldMapData.TryParseLabel(label, out var c) && s.ClearedEncounters.Contains(c));

        public static string ProgressText(BoardSession s, Def q) => $"{q.Objective} ({TargetsWon(s, q)}/{q.Targets.Length})";

        // 상점 [의뢰] 탭 [수락]. 이미 이긴 목표는 소급 인정 — 둘 다 끝냈으면 수락하면서 바로 정산.
        public static bool Accept(BoardSession s, Def q)
        {
            if (IsAccepted(s, q)) return false;
            s.StoryFlags.Add(AcceptedFlag(q));
            s.SetQuest(q.Title, ProgressText(s, q), QuestType.Sub);
            return true;
        }

        // 수락했고 목표를 다 이긴 의뢰를 완료·보상 (1회). 완료한 의뢰 목록을 돌려준다 (완료 대사용).
        public static List<Def> SettleAll(BoardSession s)
        {
            var done = new List<Def>();
            if (s.Party == null) return done;
            foreach (var q in All)
            {
                if (!IsAccepted(s, q) || IsDone(s, q)) continue;
                if (TargetsWon(s, q) < q.Targets.Length) { s.SetQuest(q.Title, ProgressText(s, q), QuestType.Sub); continue; }
                s.StoryFlags.Add(DoneFlag(q));
                s.Party.AddCent(RewardCent);
                s.Party.GrantExperienceToSurvivors(RewardExp);
                s.CompleteQuest(q.Title);
                PlaytestLog.Cent("의뢰 완료", RewardCent, s.Party.Cent, q.Title);
                done.Add(q);
            }
            return done;
        }
    }
}
