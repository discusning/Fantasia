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
        // 패턴을 쓰는 보스면 상태 (D14). 일반 적은 null.
        public BossState Boss { get; }
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
            if (enemy.HasPattern || (enemy.Thresholds != null && enemy.Thresholds.Length > 0)) Boss = new BossState();
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
        // [회피 상태] — 장막 걷기·성수로 일시 해제(EvasionRevealed)되면 일반 공격도 맞는다.
        public bool HasEvasionState => !IsAlly && Enemy.HasEvasionState && !_enemyStatuses.EvasionRevealed;
        public bool IsAlive => CurrentHP > 0;
        public bool InBattle => IsAlive && !HasRetreated;

        // 마법 무기 기본 공격은 MP를 쓴다 — 부족하면 선택 불가 (System.md "무기").
        public int BasicAttackMPCost => IsAlly && Member.EquippedWeapon != null ? Member.EquippedWeapon.BaseAttackMPCost : 0;
        public bool CanBasicAttack => !IsAlly || Member.CurrentMP >= BasicAttackMPCost;

        public AttackRequest BasicAttack(bool useFocus)
        {
            // 아군 공격력 감소는 PartyMember에 반영됨. 영점 조정(정확도 +)은 무기 공격에만.
            if (IsAlly) return AttackResolver.FromPartyMember(Member, 1f, useFocus, Statuses.AccuracyBonus);
            var request = AttackResolver.FromEnemy(Enemy);
            request.AttackValue = Mathf.Max(0, request.AttackValue - _enemyStatuses.AttackPenalty);
            return request;
        }

        // 이 참가자의 기본 공격이 크리티컬일 때 대상에게 거는 상태 (적 특성 / 무기 계열 효과).
        public StatusEffectDefinition[] OnCritStatuses =>
            IsAlly ? (Member.EquippedWeapon != null ? Member.EquippedWeapon.OnCritStatuses : null) ?? System.Array.Empty<StatusEffectDefinition>()
                   : Enemy.OnCritStatuses ?? System.Array.Empty<StatusEffectDefinition>();

        public TurnShift OnCritTurnShift =>
            IsAlly ? (Member.EquippedWeapon != null ? Member.EquippedWeapon.OnCritTurnShift : TurnShift.None) : Enemy.OnCritTurnShift;

        // 스킬 공격 판정 (D11). SlotCount 0이면 현재 무기 슬롯·참조 스탯(+영점 조정), 아니면 스킬 고유 판정.
        // 무기 관통은 장비를 쓰는 공격 스킬에도 적용 — 스킬 관통과 큰 쪽.
        public AttackRequest SkillAttack(SkillDefinition skill, bool useFocus)
        {
            bool weaponBased = skill.SlotCount <= 0;
            var request = AttackResolver.FromPartyMember(Member, skill.Coefficient, useFocus, weaponBased ? Statuses.AccuracyBonus : 0f);
            if (skill.UsesMagic)
            {
                request.Type = DamageType.Magic;
                request.AttackValue = Member.FinalMagicAttack;
            }
            if (!weaponBased)
            {
                request.SlotCount = skill.SlotCount;
                request.SlotChance = SlotCheck.FinalSlotChance(skill.SlotCount, Member.FinalStats.Get(skill.RollStat));
            }
            request.PierceRatio = Mathf.Max(request.PierceRatio, skill.PierceRatio);
            return request;
        }

        // ── 스킬 쿨다운 (자기 개인 턴 기준, 전투가 끝나면 BattleUnit째 사라져 초기화) ──
        private readonly Dictionary<SkillDefinition, int> _cooldowns = new();

        // 쿨다운 N: 사용 뒤 자신의 다음 N개 개인 턴 동안 불가 → N+1로 두고 매 개인 턴 시작에 1씩 줄인다.
        public void StartCooldown(SkillDefinition skill)
        {
            if (skill.CooldownTurns > 0) _cooldowns[skill] = skill.CooldownTurns + 1;
        }

        public void TickCooldowns()
        {
            foreach (var key in new List<SkillDefinition>(_cooldowns.Keys)) _cooldowns[key] = Mathf.Max(0, _cooldowns[key] - 1);
        }

        public int CooldownLeft(SkillDefinition skill) => _cooldowns.TryGetValue(skill, out var left) ? left : 0;

        // 쓸 수 없으면 이유 (UI·로그 표시), 쓸 수 있으면 null.
        public string CannotUseReason(SkillDefinition skill)
        {
            if (!IsAlly || skill == null) return "사용 불가";
            int cd = CooldownLeft(skill);
            if (cd > 0) return $"재사용 대기 {cd}턴";
            if (Member.CurrentFocus < skill.FocusCost) return $"집중력 부족 ({skill.FocusCost} 필요)";
            if (Member.CurrentMP < skill.MPCost) return $"MP 부족 ({skill.MPCost} 필요)";
            if (Member.CurrentHP <= skill.HPCost) return $"HP 부족 ({skill.HPCost} 필요)";
            return null;
        }

        public IEnumerable<SkillDefinition> ActiveSkills()
        {
            if (!IsAlly || Member.Job.Skills == null) yield break;
            foreach (var s in Member.Job.Skills) if (s != null && s.Type == SkillType.Active) yield return s;
        }

        // 첫 탄의 침묵 — 전투 중 첫 활·총 공격 1회만.
        public bool FirstGunShotUsed { get; set; }

        public DefenseProfile Defense()
        {
            if (IsAlly) return AttackResolver.DefenseOf(Member, IsDefending); // 아군 방어 감소는 PartyMember에 반영됨
            var profile = AttackResolver.DefenseOf(Enemy, _enemyHP, HasEvasionState, IsDefending);
            profile.PhysicalDefense = Mathf.Max(0, profile.PhysicalDefense - _enemyStatuses.DefensePenalty);
            profile.MagicDefense = Mathf.Max(0, profile.MagicDefense - _enemyStatuses.DefensePenalty);
            return profile;
        }

        public void TakeDamage(int amount)
        {
            if (IsAlly) Member.TakeDamage(amount);
            else _enemyHP = Mathf.Max(0, _enemyHP - amount);
        }

        // 적 자가 회복 (보스 금지된 봉합 등) — 최대 HP를 넘지 않음. 회복량을 돌려준다.
        public int HealEnemy(int amount)
        {
            if (IsAlly || !IsAlive) return 0;
            int before = _enemyHP;
            _enemyHP = Mathf.Min(Enemy.MaxHP, _enemyHP + Mathf.Max(0, amount));
            return _enemyHP - before;
        }
    }

    // 보드 → 전투 씬으로 넘기는 조우 정보 (BoardSession.PendingBattle).
    public class BattleSetup
    {
        public List<EnemyDefinition> Enemies = new();
        public EncounterOutcome Opening = EncounterOutcome.FightStarted; // 기습 성공/실패면 첫 라운드 순서가 바뀐다
    }
}
