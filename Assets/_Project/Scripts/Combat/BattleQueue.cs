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
    // - 방해함·초기화(Shift): 아직 행동하지 않은 대상의 이번 라운드 위치를 뒤로 민다. 같은 대상에게
    //   라운드당 합계 1회, 추가 행동·행동 삭제 없음. 이미 행동했으면 효과 없음(다음 라운드 누적 지연 금지).
    //   돌격(앞으로 당기기)은 쓰는 스킬이 생길 때 추가. 기획이 "턴의 변화"를 다시 검토할 예정이라 규칙은 여기 한 곳.
    public class BattleQueue
    {
        private readonly List<BattleUnit> _units;
        private readonly EncounterOutcome _opening;
        private List<BattleUnit> _round = new();
        private int _index = -1;
        private readonly HashSet<BattleUnit> _shiftedThisRound = new();

        public int Round { get; private set; }
        public BattleUnit Current => _index >= 0 && _index < _round.Count ? _round[_index] : null;

        public BattleQueue(IEnumerable<BattleUnit> units, EncounterOutcome opening)
        {
            _units = units.ToList();
            _opening = opening;
        }


        // ── 전투 저장 (BattleSnapshot) ──
        public IReadOnlyList<BattleUnit> RoundOrder => _round;
        public int Index => _index;
        public IEnumerable<BattleUnit> ShiftedThisRound => _shiftedThisRound;

        public void Restore(int round, List<BattleUnit> order, int index, IEnumerable<BattleUnit> shifted)
        {
            Round = round;
            _round = order;
            _index = index;
            _roundSpeeds.Clear();
            foreach (var u in order) _roundSpeeds[u] = u.Speed; // 불러오기: 지금 속도로 근사
            _lastSpeedOrder = ComputeOrder(_units, 0, _opening);
            _orderChange.Clear();
            _shiftedThisRound.Clear();
            foreach (var u in shifted) _shiftedThisRound.Add(u);
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

        // 라운드가 끝날 때 (다음 라운드 정렬 직전) — 라운드 단위 상태(속도 증가)의 기간을 줄인다.
        public event System.Action RoundEnded;

        // 이번 라운드 정렬에 쓴 속도 (타임라인 초상 아래 표시)
        private readonly Dictionary<BattleUnit, int> _roundSpeeds = new();
        public int RoundSpeedOf(BattleUnit unit) => _roundSpeeds.TryGetValue(unit, out int s) ? s : unit.Speed;

        // 직전 라운드 대비 순서 변화 (+1 앞당겨짐 ↑, −1 늦춰짐 ↓). 생존자끼리만 비교 — 사망으로 당겨진 건 0.
        // 비교 기준은 속도 정렬 순서 (기습 첫 라운드·방해함으로 밀린 자리는 빼고 순수 속도 순).
        private List<BattleUnit> _lastSpeedOrder = new();
        private readonly Dictionary<BattleUnit, int> _orderChange = new();
        public int OrderChangeOf(BattleUnit unit) => _orderChange.TryGetValue(unit, out int c) ? c : 0;

        private void StartNextRound()
        {
            if (Round > 0) RoundEnded?.Invoke();
            Round++;
            _round = ComputeOrder(_units, Round, _opening);
            _index = 0;
            _shiftedThisRound.Clear();

            _roundSpeeds.Clear();
            foreach (var u in _round) _roundSpeeds[u] = u.Speed;
            var speedOrder = ComputeOrder(_units, 0, _opening); // 0 = 기습 보정 없는 순수 속도 순
            _orderChange.Clear();
            if (Round > 1)
            {
                var common = speedOrder.Where(_lastSpeedOrder.Contains).ToList();
                var before = _lastSpeedOrder.Where(common.Contains).ToList();
                for (int i = 0; i < common.Count; i++)
                {
                    int was = before.IndexOf(common[i]);
                    if (was != i) _orderChange[common[i]] = was > i ? 1 : -1;
                }
            }
            _lastSpeedOrder = speedOrder;
        }

        // 방해함/초기화. 실제로 밀렸으면 true (이미 행동함·이번 라운드 이미 밀림·더 뒤로 갈 곳 없음이면 false).
        public bool Shift(BattleUnit target, Fantasia.Data.TurnShift shift)
        {
            if (shift == Fantasia.Data.TurnShift.None || target == null) return false;
            int pos = _round.IndexOf(target);
            if (pos <= _index || _shiftedThisRound.Contains(target)) return false; // 이미 행동했거나 지금 행동 중

            // 남은 큐 = 지금 차례 다음부터 (대상 포함).
            int remainingStart = _index + 1;
            int remainingCount = _round.Count - remainingStart;
            int offset = shift == Fantasia.Data.TurnShift.Reset
                ? remainingCount - 1
                : (int)System.Math.Ceiling(remainingCount * 2.0 / 3.0) - 1; // 남은 큐의 2/3 위치
            int newPos = remainingStart + System.Math.Max(0, offset);
            if (newPos <= pos) return false; // 이미 그보다 뒤 — 앞으로 당기지 않는다

            _round.RemoveAt(pos);
            _round.Insert(newPos, target);
            _shiftedThisRound.Add(target);
            return true;
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
            public readonly int Speed; // 이번 라운드 = 정렬에 쓴 속도, 다음 라운드 = 지금 속도(예상)
            public Entry(BattleUnit unit, int round, int speed) { Unit = unit; Round = round; Speed = speed; }
        }

        // 타임라인 표시용: 현재 차례부터 앞으로 count개 — 이번 라운드 + 다음 라운드까지만 (속도 추가 기획 10/26).
        // 다음 라운드는 지금 속도 기준 "예상" 순서 (속도 변화·쓰러짐으로 바뀔 수 있음).
        public List<Entry> Preview(int count)
        {
            var result = new List<Entry>();
            for (int i = System.Math.Max(_index, 0); i < _round.Count && result.Count < count; i++)
            {
                if (_round[i].InBattle) result.Add(new Entry(_round[i], Round, RoundSpeedOf(_round[i])));
            }

            int round = Round;
            while (result.Count < count && round < Round + 1)
            {
                round++;
                var next = ComputeOrder(_units, round, _opening);
                if (next.Count == 0) break;
                foreach (var unit in next)
                {
                    if (result.Count >= count) break;
                    result.Add(new Entry(unit, round, unit.Speed));
                }
            }
            return result;
        }
    }
}
