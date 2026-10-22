using System.Collections.Generic;
using Fantasia.Data;

namespace Fantasia.World
{
    // 월드 라운드 = 살아 있는 아군 전원이 월드 행동을 한 번씩 마친 주기 (System.md
    // "자원·행동·피해의 공통 정의"). 전투 라운드·개인 턴과는 별도 시계이며, 전투·메뉴를
    // 열고 닫는 행동은 여기 포함하지 않는다 — 호출하는 쪽에서 실제 월드 행동(이동 종료,
    // 숙박, 상점 이용 등)일 때만 NotifyWorldActionTaken을 부른다.
    public class WorldClock
    {
        public int WorldRound { get; private set; } = 1;

        private readonly HashSet<PartyMember> _actedThisRound = new();
        private readonly HashSet<PartyMember> _eventBonusUsedThisTurn = new();

        // 해당 캐릭터의 새 월드 턴이 시작될 때 호출 — 이벤트 추가 이동 사용 여부를 리셋한다.
        public void BeginTurn(PartyMember actor)
        {
            _eventBonusUsedThisTurn.Remove(actor);
        }

        // 이벤트 추가 이동은 개인 월드 턴당 1회만 쓸 수 있다 (System.md). 이미 썼으면 false.
        public bool TryConsumeEventBonus(PartyMember actor)
        {
            return _eventBonusUsedThisTurn.Add(actor);
        }

        // 해당 캐릭터가 월드 행동(이동 등)을 끝냈을 때 호출. 생존 파티원 전원이 행동을
        // 마쳤으면 월드 라운드가 1 증가하고 다음 라운드를 위해 기록을 비운다.
        public void NotifyWorldActionTaken(Party party, PartyMember actor)
        {
            if (actor == null || !actor.IsAlive) return;
            _actedThisRound.Add(actor);

            if (AllAliveHaveActed(party))
            {
                WorldRound++;
                _actedThisRound.Clear();
            }
        }

        private bool AllAliveHaveActed(Party party)
        {
            foreach (var member in party.AliveMembers)
            {
                if (!_actedThisRound.Contains(member)) return false;
            }
            return true;
        }
    }
}
