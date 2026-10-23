using System.Collections.Generic;
using Fantasia.Data;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Combat
{
    // 전투 참가자 하나 — 아군(PartyMember)이든 적(EnemyDefinition)이든 같은 인터페이스로 다룬다.
    // 아군 HP·집중력은 PartyMember에 그대로 반영되어 전투가 끝나도 남는다. 적 HP는 전투 동안만.
    public class BattleUnit
    {
        public PartyMember Member { get; }
        public EnemyDefinition Enemy { get; }
        public bool IsAlly => Member != null;
        public int FormationIndex { get; }  // 같은 진영 안 편성 순서 (속도 동률 시 우선순위)
        public string DisplayName { get; set; }

        public bool IsDefending { get; set; }  // 방어 행동 — 다음 자기 개인 턴 시작 전까지
        public bool HasRetreated { get; set; } // 전투 중 퇴각 성공

        private int _enemyHP;
        private readonly StatusSet _enemyStatuses = new();

        public BattleUnit(PartyMember member, int formationIndex)
        {
            Member = member;
            FormationIndex = formationIndex;
            DisplayName = member.Job.DisplayName;
        }

        public BattleUnit(EnemyDefinition enemy, int formationIndex)
        {
            Enemy = enemy;
            FormationIndex = formationIndex;
            DisplayName = enemy.DisplayName;
            _enemyHP = enemy.MaxHP;
        }

        // 초상·모델 캐시 키 (같은 직업/적 종류는 같은 그림).
        public ScriptableObject Definition => IsAlly ? Member.Job : Enemy;

        public int CurrentHP => IsAlly ? Member.CurrentHP : _enemyHP;
        public int MaxHP => IsAlly ? Member.MaxHP : Enemy.MaxHP;
        // 아군은 PartyMember(전투 뒤에도 남는 중독 등), 적은 이 전투 동안만.
        public StatusSet Statuses => IsAlly ? Member.Statuses : _enemyStatuses;

        // 속도 감소(속박·중독)는 다음 라운드 정렬에 반영된다 (BattleQueue가 라운드 시작 때 읽음).
        public int Speed => IsAlly ? Member.FinalStats.Speed
            : Mathf.Max(0, Enemy.Speed - _enemyStatuses.SpeedPenalty - _enemyStatuses.AllStatPenalty);
        public bool HasEvasionState => !IsAlly && Enemy.HasEvasionState;
        public bool IsAlive => CurrentHP > 0;
        public bool InBattle => IsAlive && !HasRetreated;

        // 마법 무기 기본 공격은 MP를 쓴다 — 부족하면 선택 불가 (System.md "무기").
        public int BasicAttackMPCost => IsAlly && Member.EquippedWeapon != null ? Member.EquippedWeapon.BaseAttackMPCost : 0;
        public bool CanBasicAttack => !IsAlly || Member.CurrentMP >= BasicAttackMPCost;

        public AttackRequest BasicAttack(bool useFocus)
        {
            if (IsAlly) return AttackResolver.FromPartyMember(Member, 1f, useFocus); // 아군 공격력 감소는 PartyMember에 반영됨
            var request = AttackResolver.FromEnemy(Enemy);
            request.AttackValue = Mathf.Max(0, request.AttackValue - _enemyStatuses.AttackPenalty);
            return request;
        }

        // 이 참가자의 기본 공격이 크리티컬일 때 대상에게 거는 상태 (적 특성 / 무기 계열 효과).
        public StatusEffectDefinition[] OnCritStatuses =>
            IsAlly ? (Member.EquippedWeapon != null ? Member.EquippedWeapon.OnCritStatuses : null) ?? System.Array.Empty<StatusEffectDefinition>()
                   : Enemy.OnCritStatuses ?? System.Array.Empty<StatusEffectDefinition>();

        public DefenseProfile Defense() =>
            IsAlly ? AttackResolver.DefenseOf(Member, IsDefending)
                   : AttackResolver.DefenseOf(Enemy, _enemyHP, HasEvasionState, IsDefending);

        public void TakeDamage(int amount)
        {
            if (IsAlly) Member.TakeDamage(amount);
            else _enemyHP = Mathf.Max(0, _enemyHP - amount);
        }
    }

    // 보드 → 전투 씬으로 넘기는 조우 정보 (BoardSession.PendingBattle).
    public class BattleSetup
    {
        public List<EnemyDefinition> Enemies = new();
        public EncounterOutcome Opening = EncounterOutcome.FightStarted; // 기습 성공/실패면 첫 라운드 순서가 바뀐다
    }
}
