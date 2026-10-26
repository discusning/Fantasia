using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.Combat
{
    // 전투 중 저장·재개 (Docs/Planning/Save.md 5) — BattleSnapshot 참고.
    public partial class BattleController
    {
        private bool _introDone;

        private BattleSnapshot.UnitRef Ref(BattleUnit u) =>
            u == null ? null : new BattleSnapshot.UnitRef { ally = u.IsAlly, index = (u.IsAlly ? _allies : _enemies).IndexOf(u) };

        private BattleUnit FromRef(BattleSnapshot.UnitRef r)
        {
            if (r == null) return null;
            var list = r.ally ? _allies : _enemies;
            return r.index >= 0 && r.index < list.Count ? list[r.index] : null;
        }

        // 개인 턴 시작 처리 직후·아군 행동 확정 직후에 부른다. cmd가 있으면 이어할 때 그 행동을 그대로 다시 실행.
        private void SaveBattle(string reason, BattleSnapshot.Command cmd = null, bool confused = false)
        {
            var session = BoardSession.Instance;
            if (session == null || _setup == null) return;
            var snap = new BattleSnapshot
            {
                started = true,
                enemyIds = _setup.Enemies.Select(e => e.Id).ToList(),
                opening = (int)_setup.Opening,
                hasEncounterCoord = session.PendingEncounterCoord.HasValue,
                coordQ = session.PendingEncounterCoord?.Q ?? 0,
                coordR = session.PendingEncounterCoord?.R ?? 0,
                reaperFight = session.PendingReaperFight,
                zoneWearLevel = LootRules.CurrentZoneWearLevel,
                fixedExp = _setup.FixedExp,
                fixedCent = _setup.FixedCent,
                round = _queue.Round,
                index = _queue.Index,
                order = _queue.RoundOrder.Select(Ref).ToList(),
                shifted = _queue.ShiftedThisRound.Select(Ref).ToList(),
                current = Ref(_current),
                currentConfused = confused,
                introDone = _introDone,
                pending = cmd ?? new BattleSnapshot.Command(),
            };
            foreach (var u in _allies) snap.allies.Add(UnitData(u));
            foreach (var u in _enemies) snap.enemies.Add(UnitData(u));
            session.BattleSnapshotJson = JsonUtility.ToJson(snap);
            SaveGame.Checkpoint(reason);
        }

        private static BattleSnapshot.UnitData UnitData(BattleUnit u)
        {
            var d = new BattleSnapshot.UnitData
            {
                hp = u.IsAlly ? 0 : u.CurrentHP,
                defending = u.IsDefending,
                retreated = u.HasRetreated,
                firstGunShotUsed = u.FirstGunShotUsed,
                cooldowns = u.Cooldowns.Select(kv => new BattleSnapshot.CooldownData { skillId = kv.Key.Id, left = kv.Value }).ToList(),
            };
            if (!u.IsAlly)
                d.statuses = u.Statuses.All.Select(a => new BattleSnapshot.StatusData { id = a.Definition.Id, turns = a.RemainingTurns, stacks = a.Stacks }).ToList();
            if (u.Boss != null)
            {
                d.isBoss = true;
                d.patternIndex = u.Boss.PatternIndex;
                d.firedThresholds = u.Boss.FiredThresholds.ToList();
                d.pendingThreshold = u.Boss.Pending != null ? System.Array.IndexOf(u.Enemy.Thresholds, u.Boss.Pending) : -1;
                d.pendingCancelled = u.Boss.PendingCancelled;
                d.allyTurnSinceAnnounce = u.Boss.AllyTurnSinceAnnounce;
            }
            return d;
        }

        // 저장된 전투 읽기 — 없거나 깨졌으면 null
        private static BattleSnapshot ReadSnapshot(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<BattleSnapshot>(json); }
            catch { return null; }
        }

        // 진입 정보(적·개시·조우 칸)를 세션에 되돌린다. 적을 못 찾으면 null.
        private static BattleSetup SetupFromSnapshot(BattleSnapshot snap, BoardSession session)
        {
            var catalog = ItemCatalog.Current;
            if (snap == null || catalog == null) return null;
            var setup = new BattleSetup { Opening = (Fantasia.World.EncounterOutcome)snap.opening };
            foreach (var id in snap.enemyIds)
            {
                var e = catalog.Enemy(id);
                if (e == null) return null;
                setup.Enemies.Add(e);
            }
            session.PendingEncounterCoord = snap.hasEncounterCoord ? new Fantasia.Board.HexCoord(snap.coordQ, snap.coordR) : null;
            session.PendingReaperFight = snap.reaperFight;
            session.AllyTurnEndedByBattle = true;
            LootRules.CurrentZoneWearLevel = snap.zoneWearLevel;
            setup.FixedExp = snap.fixedExp;
            setup.FixedCent = snap.fixedCent;
            return setup;
        }

        // 유닛·행동 순서·보스 상태 복원 (아군 자원·상태·인벤토리는 SaveGame이 이미 복원)
        private void ApplySnapshot(BattleSnapshot snap)
        {
            var catalog = ItemCatalog.Current;
            for (int i = 0; i < _allies.Count && i < snap.allies.Count; i++) ApplyUnit(_allies[i], snap.allies[i], catalog);
            for (int i = 0; i < _enemies.Count && i < snap.enemies.Count; i++) ApplyUnit(_enemies[i], snap.enemies[i], catalog);
            _queue.Restore(snap.round, snap.order.Select(FromRef).Where(u => u != null).ToList(), snap.index, snap.shifted.Select(FromRef).Where(u => u != null));
            _introDone = snap.introDone;
            foreach (var u in _allies.Concat(_enemies))
            {
                if (!u.IsAlive) KnockDown(u);
                if (u.HasRetreated && _models.TryGetValue(u, out var model)) model.SetActive(false);
            }
            _hud.RefreshEnemies(null);
        }

        private static void ApplyUnit(BattleUnit u, BattleSnapshot.UnitData d, ItemCatalog catalog)
        {
            u.IsDefending = d.defending;
            u.HasRetreated = d.retreated;
            u.FirstGunShotUsed = d.firstGunShotUsed;
            var skills = u.IsAlly && u.Member.Job.Skills != null ? u.Member.Job.Skills : new SkillDefinition[0];
            foreach (var c in d.cooldowns) u.RestoreCooldown(skills.FirstOrDefault(s => s != null && s.Id == c.skillId), c.left);
            if (u.IsAlly) return;
            u.RestoreEnemyHP(d.hp);
            u.Statuses.ClearAll();
            foreach (var st in d.statuses) u.Statuses.Restore(catalog.Status(st.id), st.turns, st.stacks);
            if (u.Boss != null && d.isBoss)
            {
                u.Boss.PatternIndex = d.patternIndex;
                u.Boss.FiredThresholds.Clear();
                foreach (var f in d.firedThresholds) u.Boss.FiredThresholds.Add(f);
                u.Boss.Pending = d.pendingThreshold >= 0 && d.pendingThreshold < u.Enemy.Thresholds.Length ? u.Enemy.Thresholds[d.pendingThreshold] : null;
                u.Boss.PendingCancelled = d.pendingCancelled;
                u.Boss.AllyTurnSinceAnnounce = d.allyTurnSinceAnnounce;
            }
        }

        // 확정된 아군 행동 기록 (행동 확정 직후 저장)
        private BattleSnapshot.Command CommandFor(BattleSnapshot.CommandKind kind, BattleUnit target = null, SkillDefinition skill = null, ConsumableDefinition item = null) =>
            new() { kind = kind, skillId = skill != null ? skill.Id : null, itemId = item != null ? item.Id : null, hasTarget = target != null, target = Ref(target), focus = _hud.UseFocus };

        // 이어하기: 기록된 행동을 같은 순서로 다시 실행 (같은 게임 난수 → 같은 결과)
        private void ReplayCommand(BattleSnapshot.Command cmd)
        {
            var target = cmd.hasTarget ? FromRef(cmd.target) : null;
            Log($"[저장] 확정된 행동 다시 실행 — {cmd.kind}");
            _hud.DevSetFocus(cmd.focus);
            switch (cmd.kind)
            {
                case BattleSnapshot.CommandKind.Attack:
                    OnAttackClicked();
                    OnUnitPicked(target);
                    break;
                case BattleSnapshot.CommandKind.Skill:
                    var skill = _current.ActiveSkills().FirstOrDefault(s => s.Id == cmd.skillId);
                    OnSkillChosen(skill);
                    if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
                    break;
                case BattleSnapshot.CommandKind.Item:
                    OnItemChosen(ItemCatalog.Current != null ? ItemCatalog.Current.Consumable(cmd.itemId) : null);
                    if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
                    break;
                case BattleSnapshot.CommandKind.Revive:
                    OnReviveClicked();
                    OnUnitPicked(target);
                    break;
                case BattleSnapshot.CommandKind.Defend: OnDefendClicked(); break;
                case BattleSnapshot.CommandKind.Retreat: OnRetreatClicked(); break;
                case BattleSnapshot.CommandKind.Interrupt: OnInterruptClicked(); break;
            }
        }
    }
}
