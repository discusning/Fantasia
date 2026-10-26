using System;
using System.Collections.Generic;

namespace Fantasia.Combat
{
    // 전투 중 저장 (Docs/Planning/Save.md 5 — 진행 중인 전투의 마지막 확정 상태에서 재개, 도주로 처리하지 않음).
    // 저장 시점: ① 전투 진입(조우 결과 확정) ② 개인 턴 시작 처리 직후 ③ 아군 행동 확정 직후(명령 기록).
    // ③에서 꺼지면 이어할 때 기록된 명령을 같은 게임 난수로 다시 실행 → 결과가 바뀌지 않는다.
    // 아군 HP·MP·집중력·상태·인벤토리는 SaveGame 파티 정보에 함께 저장된다 (같은 시점).
    [Serializable]
    public class BattleSnapshot
    {
        public bool started;                 // false = 진입만 (판정 확정 뒤 전투 씬 전) → 이어하면 처음부터 (같은 난수)
        public List<string> enemyIds = new();
        public int opening;                  // EncounterOutcome
        public bool hasEncounterCoord;
        public int coordQ, coordR;
        public bool reaperFight;
        public int zoneWearLevel = 1;
        public int fixedExp, fixedCent; // 지도 적 칸 확정 보상 (MapTileRules)

        public List<UnitData> allies = new(), enemies = new();
        public int round, index;
        public List<UnitRef> order = new(), shifted = new();
        public UnitRef current;
        public bool currentConfused;
        public bool introDone;
        public Command pending;              // 확정된 아군 행동 (없으면 kind = None)

        [Serializable] public class UnitRef { public bool ally; public int index; }

        [Serializable] public class StatusData { public string id; public int turns, stacks; }
        [Serializable] public class CooldownData { public string skillId; public int left; }

        [Serializable]
        public class UnitData
        {
            public int hp;                    // 적만 (아군은 파티 정보)
            public bool defending, retreated, firstGunShotUsed;
            public List<StatusData> statuses = new(); // 적만
            public List<CooldownData> cooldowns = new();
            // 보스
            public bool isBoss;
            public int patternIndex = 0, pendingThreshold = -1;
            public List<int> firedThresholds = new();
            public bool pendingCancelled, allyTurnSinceAnnounce;
        }

        public enum CommandKind { None, Attack, Skill, Item, Revive, Defend, Retreat, Interrupt }

        [Serializable]
        public class Command
        {
            public CommandKind kind = CommandKind.None;
            public string skillId, itemId;
            public bool hasTarget;
            public UnitRef target;
            public bool focus;
        }
    }
}
