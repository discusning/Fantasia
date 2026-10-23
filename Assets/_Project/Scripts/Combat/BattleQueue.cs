using System.Collections.Generic;
using System.Linq;
using Fantasia.World;

namespace Fantasia.Combat
{
    // D09 전투 행동 순서 (System.md "자원·행동·피해의 공통 정의" + "전투 시 조작").
    // - 전투 라운드 시작 시 살아 있는 아군·적을 최종 속도 내림차순으로 정렬. 동률이면 아군 우선,
    //   같은 진영은 편성 순서. 속도 버프·디버프는 다음 라운드 정렬에 반영된다(라운드 중 재정렬 없음).
    // - 각 참가자는 라운드마다 행동 1회. 쓰러지거나 퇴각한 참가자는 건너뛴다.
    // - 기습 성공: 첫 라운드만 아군 전원이 적보다 먼저 / 기습 실패: 적 전원이 먼저.
    // 돌격·방해함·초기화(턴 위치 이동)는 기획이 "다시 생각해볼 예정"이라 아직 넣지 않았다.
    public class BattleQueue
    {
        private readonly List<BattleUnit> _units;
        private readonly EncounterOutcome _opening;
        private List<BattleUnit> _round = new();
        private int _index = -1;

        public int Round { get; private set; }
        public BattleUnit Current => _index >= 0 && _index < _round.Count ? _round[_index] : null;

        public BattleQueue(IEnumerable<BattleUnit> units, EncounterOutcome opening)
        {
            _units = units.ToList();
            _opening = opening;
        }

        // 다음 행동할 참가자로 넘어간다. 라운드가 끝나면 새 라운드를 정렬한다. 아무도 없으면 null.
        public BattleUnit Advance()
        {
            if (!_units.Any(u => u.InBattle)) return null;

            while (true)
            {
                _index++;
                if (_index >= _round.Count) StartNextRound();
                if (Current != null && Current.InBattle) return Current;
            }
        }

        private void StartNextRound()
        {
            Round++;
            _round = ComputeOrder(_units, Round, _opening);
            _index = 0;
        }

        public static List<BattleUnit> ComputeOrder(IEnumerable<BattleUnit> units, int round, EncounterOutcome opening)
        {
            var sorted = units.Where(u => u.InBattle)
                .OrderByDescending(u => u.Speed)
                .ThenBy(u => u.IsAlly ? 0 : 1)
                .ThenBy(u => u.FormationIndex)
                .ToList();

            if (round != 1) return sorted;
            if (opening == EncounterOutcome.AmbushSuccess) return sorted.Where(u => u.IsAlly).Concat(sorted.Where(u => !u.IsAlly)).ToList();
            if (opening == EncounterOutcome.AmbushFailure) return sorted.Where(u => !u.IsAlly).Concat(sorted.Where(u => u.IsAlly)).ToList();
            return sorted;
        }

        public readonly struct Entry
        {
            public readonly BattleUnit Unit;
            public readonly int Round;
            public Entry(BattleUnit unit, int round) { Unit = unit; Round = round; }
        }

        // 타임라인 표시용: 현재 차례부터 앞으로 count개. 다음 라운드는 지금 속도 기준 예상 순서.
        public List<Entry> Preview(int count)
        {
            var result = new List<Entry>();
            for (int i = System.Math.Max(_index, 0); i < _round.Count && result.Count < count; i++)
            {
                if (_round[i].InBattle) result.Add(new Entry(_round[i], Round));
            }

            int round = Round;
            while (result.Count < count)
            {
                round++;
                var next = ComputeOrder(_units, round, _opening);
                if (next.Count == 0) break;
                foreach (var unit in next)
                {
                    if (result.Count >= count) break;
                    result.Add(new Entry(unit, round));
                }
            }
            return result;
        }
    }
}
