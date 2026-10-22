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
