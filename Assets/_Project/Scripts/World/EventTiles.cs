using System;
using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.World
{
    // 이벤트 칸 (System.md "이벤트 칸" + "이벤트 수치·도시 거점 보완").
    // 종류: 기관차(2차) · 점술마녀 · 십자가 석상(기도 6종 — Data/Prayer.cs) · 분수대.
    public enum EventKind { None, FortuneTeller, Fountain, Statue, Train, Well } // Well = 회복 칸(우물) 선택 창 — 이벤트 칸 배정에는 안 씀

    public enum FortuneCard { Wealth, Recovery, Focus }

    public static class EventTiles
    {
        // 칸 → 종류. 칸마다 고정 (새 게임에도 같음) — Planning/EventTiles.md 10/26 확정.
        // 런던 2곳 확정. 베르사유 A15·A21, 로마 Q26·X28은 종류 기획 대기 (2막 이후 공개).
        private static readonly Dictionary<string, EventKind> Assigned = new()
        {
            ["M02"] = EventKind.FortuneTeller, // 런던 이벤트01
            ["S10"] = EventKind.Fountain,      // 런던 이벤트02
        };

        public static EventKind KindAt(string label) => label != null && Assigned.TryGetValue(label, out var k) ? k : EventKind.None;

        public static string Label(EventKind k) => k switch
        {
            EventKind.FortuneTeller => "점술마녀",
            EventKind.Fountain => "분수대",
            EventKind.Statue => "십자가 석상",
            EventKind.Train => "기관차",
            EventKind.Well => "우물",
            _ => "이벤트",
        };

        // ── 점술마녀: 20 Cent, 부·회복·집중 중 1장, 행운%(최대 95) 단일 판정, 같은 칸은 3월드 라운드 뒤 재참여 ──
        public const int WitchFee = 20;
        public const int WitchCooldownRounds = 3;
        public const int WealthCent = 60, RecoveryHP = 50, FocusAmount = 2;

        public static int SuccessPercent(PartyMember judge) => judge == null ? 0 : Mathf.Clamp(judge.FinalStats.Luck, 0, 95);

        public static string CardName(FortuneCard c) => c switch { FortuneCard.Wealth => "부", FortuneCard.Recovery => "회복", _ => "집중" };

        public static string CardReward(FortuneCard c) => c switch
        {
            FortuneCard.Wealth => $"성공 시 {WealthCent} Cent",
            FortuneCard.Recovery => $"성공 시 HP {RecoveryHP} 회복",
            _ => $"성공 시 집중력 {FocusAmount} 회복",
        };

        public static bool NeedsTarget(FortuneCard c) => c != FortuneCard.Wealth;

        // 남은 대기 라운드 (0이면 참여 가능)
        public static int WitchWaitRounds(int lastRound, int worldRound) => lastRound <= 0 ? 0 : Math.Max(0, lastRound + WitchCooldownRounds - worldRound);

        public readonly struct WitchResult
        {
            public readonly bool Ok, Success;
            public readonly string Message;
            public WitchResult(bool ok, bool success, string message) { Ok = ok; Success = success; Message = message; }
        }

        // 참가비 → 판정 → (성공 시) 보상. 판정은 게임 난수 — 호출 뒤 바로 저장하고 결과를 보여준다.
        public static WitchResult PlayWitch(Party party, PartyMember judge, FortuneCard card, PartyMember target)
        {
            if (party.Cent < WitchFee) return new WitchResult(false, false, $"참가비 부족 ({WitchFee} Cent 필요)");
            if (NeedsTarget(card) && (target == null || !target.IsAlive)) return new WitchResult(false, false, "대상을 고르세요");
            party.SpendCent(WitchFee);
            int percent = SuccessPercent(judge);
            bool success = Fantasia.Dice.DiceRoller.RollChance(percent / 100f);
            if (!success) return new WitchResult(true, false, $"실패 — 보상 없음 (성공률 {percent}%)");
            switch (card)
            {
                case FortuneCard.Wealth:
                    party.AddCent(WealthCent);
                    return new WitchResult(true, true, $"성공! {WealthCent} Cent 획득");
                case FortuneCard.Recovery:
                    int before = target.CurrentHP;
                    target.Heal(RecoveryHP);
                    return new WitchResult(true, true, $"성공! {target.Job.DisplayName} HP +{target.CurrentHP - before}");
                default:
                    int got = target.RestoreFocus(FocusAmount);
                    return new WitchResult(true, true, $"성공! {target.Job.DisplayName} 집중력 +{got}");
            }
        }

        // ── 분수대: 선택한 생존 캐릭터 LP +1, 칸마다 플레이 전체 1회, LP 최대 5 ──
        public static bool CanUseFountain(PartyMember m) => m != null && m.IsAlive && m.CurrentLP < PartyMember.MaxLP;
    }
}
