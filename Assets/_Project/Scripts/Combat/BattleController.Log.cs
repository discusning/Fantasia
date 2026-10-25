using System.Linq;
using Fantasia.Core;
using Fantasia.Data;

namespace Fantasia.Combat
{
    // 플레이테스트 기록 (Core/PlaytestLog) — 전투 1회를 battles.csv 1줄로.
    public partial class BattleController
    {
        private PlaytestLog.Battle _playLog;
        // 범위 공격은 슬롯 판정 1번을 여러 대상에 쓴다 → 슬롯·크리티컬은 첫 대상에서만 세고, 회피는 대상마다
        private bool _inAreaRoll, _areaSlotsLogged;

        private void BeginAreaLog() { _inAreaRoll = true; _areaSlotsLogged = false; }
        private void EndAreaLog() => _inAreaRoll = false;

        private void LogHit(BattleUnit attacker, AttackRequest request, AttackOutcome outcome)
        {
            if (_playLog == null) return;
            if (_inAreaRoll && _areaSlotsLogged)
            {
                if (!attacker.IsAlly && outcome.Evaded) _playLog.AllyEvades++;
                return;
            }
            _playLog.Hit(attacker.IsAlly, outcome.Slots.SlotCount, outcome.Slots.SuccessCount, request.SlotChance, outcome.IsCritical, outcome.Evaded);
            if (_inAreaRoll) _areaSlotsLogged = true;
        }

        private void BeginPlayLog(bool resumed)
        {
            var session = BoardSession.Instance;
            string coord = "";
            if (session.PendingEncounterCoord.HasValue)
            {
                var (row, col) = Fantasia.Board.WorldMapData.AxialToOffset(session.PendingEncounterCoord.Value);
                coord = Fantasia.Board.WorldMapData.Label(row, col);
            }
            else if (session.PendingReaperFight) coord = "피의 사냥";
            _playLog = new PlaytestLog.Battle
            {
                Coord = coord,
                Enemies = string.Join(" + ", _setup.Enemies.Select(e => e.DisplayName)),
                Opening = _setup.Opening.ToString(),
                Resumed = resumed,
                Party = string.Join(" / ", _allies.Select(a => $"{a.DisplayName} Lv{a.Member.Level}")),
                Gear = string.Join(" / ", _allies.Select(a => string.Join("+", new[] { GearSlot.Weapon, GearSlot.Body, GearSlot.Head, GearSlot.Ring, GearSlot.Necklace }
                    .Select(slot => a.Member.GetEquipped(slot)).Where(g => g != null).Select(g => g.DisplayName)))),
                StartHp = string.Join(" / ", _allies.Select(a => $"{a.CurrentHP}/{a.MaxHP}")),
                StartLp = _allies.Sum(a => a.Member.CurrentLP),
                Seed = string.Join("-", GameRandom.State.Select(v => v.ToString("x8"))),
            };
        }

        private void EndPlayLog(string result, BattleRewards rewards, int plunder)
        {
            PlaytestLog.EndBattle(_playLog, result, _queue != null ? _queue.Round : 0,
                _allies.Select(a => (a.DisplayName, a.CurrentHP, a.MaxHP, a.Member.CurrentLP)),
                rewards?.Cent ?? 0, rewards?.Experience ?? 0,
                rewards != null ? string.Join(" ", rewards.Items.Select(i => i.Name)) : "", plunder);
            _playLog = null;
        }
    }
}
