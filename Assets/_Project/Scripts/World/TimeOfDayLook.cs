using System.Linq;
using UnityEngine;

namespace Fantasia.World
{
    // 시간대별 풍경 (2026-11-11 팀장 결정, Docs/Planning/TimeOfDay_Scenery_제안.md 1단계).
    // 월드 라운드마다 아침 → 낮 → 저녁 → 새벽 (BoardTopBar.TimeOfDayIndex). 빛 색·세기·각도, 판 바깥 색, 안개만 바꿈.
    // 게임 규칙에는 영향 없음 (결정서 10 T4). 가장 어두운 새벽도 빛 세기·환경광 60% 아래로 안 내림 (잘 보이기 먼저).
    public readonly struct TimeOfDayLook
    {
        public readonly Color Sun;          // 햇빛(달빛) 색
        public readonly float SunMul;       // 기본 세기 대비
        public readonly float ElevDelta;    // 기본 각도 대비 높이(도) — 아침·저녁은 낮게, 그림자 길게
        public readonly float YawDelta;     // 기본 각도 대비 방향(도) — 아침은 한쪽, 저녁은 반대쪽에서
        public readonly float AmbientMul;   // 환경광 세기
        public readonly Color Outside;      // 판 바깥 단색 (낮 = 기획 회신 03 색)
        public readonly float Fog;          // 안개 짙기 0~1

        public TimeOfDayLook(Color sun, float sunMul, float elev, float yaw, float ambient, Color outside, float fog)
        { Sun = sun; SunMul = sunMul; ElevDelta = elev; YawDelta = yaw; AmbientMul = ambient; Outside = outside; Fog = fog; }

        public static readonly TimeOfDayLook[] All =
        {
            new(new Color(1f, 0.86f, 0.68f), 0.9f, -20f, -50f, 0.85f, new Color(0.15f, 0.14f, 0.14f), 0.8f),   // 아침: 낮은 금빛, 짙은 아침 안개
            new(Color.white, 1f, 0f, 0f, 1f, new Color(0.075f, 0.065f, 0.07f), 0.2f),                            // 낮: 기준 (지금 화면)
            new(new Color(1f, 0.62f, 0.4f), 0.85f, -24f, 60f, 0.75f, new Color(0.12f, 0.06f, 0.05f), 0.5f),     // 저녁: 주황빛, 긴 그림자
            new(new Color(0.58f, 0.68f, 1f), 0.6f, 5f, 0f, 0.6f, new Color(0.03f, 0.04f, 0.08f), 0.8f),         // 새벽: 푸른 달빛
        };

        // 개발용: -no-timeofday = 예전처럼 늘 낮 모양 (비교 사진), -timeofday N = 시간대 고정 (0 아침 ~ 3 새벽, 시안 사진)
        public static bool Disabled => System.Environment.GetCommandLineArgs().Contains("-no-timeofday");
        public static int? Forced
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-timeofday");
                return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? Mathf.Clamp(n, 0, 3) : (int?)null;
            }
        }

        public static int IndexFor(int worldRound) => Forced ?? (Disabled ? 1 : Fantasia.UI.BoardTopBar.TimeOfDayIndex(worldRound));

        public static TimeOfDayLook Lerp(TimeOfDayLook a, TimeOfDayLook b, float t) => new(
            Color.Lerp(a.Sun, b.Sun, t), Mathf.Lerp(a.SunMul, b.SunMul, t), Mathf.Lerp(a.ElevDelta, b.ElevDelta, t),
            Mathf.Lerp(a.YawDelta, b.YawDelta, t), Mathf.Lerp(a.AmbientMul, b.AmbientMul, t),
            Color.Lerp(a.Outside, b.Outside, t), Mathf.Lerp(a.Fog, b.Fog, t));
    }
}
