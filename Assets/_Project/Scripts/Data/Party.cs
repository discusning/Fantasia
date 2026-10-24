using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Data
{
    // 3인 파티 + 공유 지갑 (System.md "상점" 절: 파티 지갑 공유, 시작 자금 180 Cent).
    public class Party
    {
        public const int MaxSize = 3;
        public const int StartingCent = 180;

        private readonly List<PartyMember> _members = new();

        public IReadOnlyList<PartyMember> Members => _members;
        public int Cent { get; private set; } = StartingCent;

        public void AddMember(PartyMember member)
        {
            if (_members.Count >= MaxSize)
                throw new InvalidOperationException($"파티는 최대 {MaxSize}명까지다.");
            _members.Add(member);
        }

        public IEnumerable<PartyMember> AliveMembers => _members.Where(m => m.IsAlive);

        // 영구 사망하지 않은 파티원 (쓰러진 사람 포함). 전원 영구 사망이면 플레이 종료.
        public IEnumerable<PartyMember> LivingMembers => _members.Where(m => !m.IsPermanentlyDead);
        public bool IsWipedOut => _members.All(m => m.IsPermanentlyDead);

        // 파티장 = 1번 캐릭터 고정 (2026-10-24 기획 결정). 보드 이동·기습·몰래 지나가기 판정을 맡는다.
        // 1번이 쓰러지거나 영구 사망하면 다음 순번의 생존자가 맡는다 (개발 임시 결정 — Decisions.md).
        public PartyMember Leader => _members.FirstOrDefault(m => m.IsAlive) ?? _members.FirstOrDefault(m => !m.IsPermanentlyDead);

        // 약탈 (패배 시 1회) — 소지 Cent의 percent%, 소수점 버림. 지갑 0이면 손실 0.
        public int LoseCentPercent(int percent)
        {
            int loss = Cent * percent / 100;
            Cent -= loss;
            return loss;
        }

        // 경험치는 살아 있는 파티원에게 각각 동일하게 지급한다 (System.md "레벨·기본 자원").
        public void GrantExperienceToSurvivors(int amount)
        {
            foreach (var member in AliveMembers)
                member.GrantExperience(amount);
        }

        public void AddCent(int amount) => Cent = Math.Max(0, Cent + amount);

        public void SpendCent(int amount)
        {
            if (amount > Cent)
                throw new InvalidOperationException("Cent가 부족하다.");
            Cent -= amount;
        }
    }
}
