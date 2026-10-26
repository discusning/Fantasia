using System;
using UnityEngine;

namespace Fantasia.Data
{
    // 소모품 효과 종류 (System.md "소모품·이동권·재료 · 29종"). 계산은 Items/ConsumableRules.
    public enum ConsumableEffect
    {
        None,           // 효과 없음 (재료 등)
        RestoreHP,      // Amount만큼 HP (+ Cures)
        RestoreMP,
        RestoreFocus,
        Cure,           // Cures 상태 제거만
        RevealEvasion,  // 성수 — 적 1개 회피 상태 해제 (Duration 개인 턴)
        Firebomb,       // 연금술 화염병 — 모든 적에게 고정 Amount 마법 피해, 크리티컬 없음
        Escape,         // 탈출권 — 생존 아군 전원 퇴각, 경험치·전리품 없음
        AmbushBonus,    // 안개 연막병 — 다음 기습 슬롯 +Amount%p
        CampRest,       // 안개 야영 세트 — 생존 파티 최대 HP·MP 25%·집중력 1 회복
        LifeLaurel,     // 생명의 월계관 — LP +1 (최대 5)
        StatBuff,       // 강장제류 — BuffStat +Amount, Duration
        // 아래는 다른 시스템이 생기면 연결 (지금은 사용 불가로 표시)
        RuneTicket,     // 기관차 칸 (도시 설계 대기)
        Teleport,       // 텔레포트권 (보드 탐색 기록 필요)
        RepairWeapon,   // 장비 파손 (2차)
        RepairGear,
        SilverBullet,   // 2막 이후
        WeaponUpgrade,  // 마리의 집문서 (고성)
        Material,       // 연금술 시약 — 제작 재료
    }

    [Flags]
    public enum UseContext
    {
        None = 0,
        Battle = 1,
        World = 2,
    }

    [CreateAssetMenu(fileName = "Consumable", menuName = "Fantasia/Data/Consumable Definition")]
    public class ConsumableDefinition : ScriptableObject
    {
        public const int DefaultMaxStack = 10; // 일반 소모품 종류당 10개, 시약 30개 (System.md)

        [Header("식별")]
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        [TextArea] public string EffectDescription; // 원문 그대로

        [Header("효과")]
        public ConsumableEffect Effect;
        public int Amount;
        public int Duration;                       // 개인 턴(전투) / 월드 라운드(월드)
        public StatKind BuffStat;                  // StatBuff 전용
        public StatusEffectType[] Cures = Array.Empty<StatusEffectType>();
        public StatusEffectDefinition[] AppliedStatuses = Array.Empty<StatusEffectDefinition>(); // 대상에게 거는 상태 (성수: 회피 상태 해제)
        public UseContext Usable = UseContext.Battle | UseContext.World;

        [Header("경제")]
        public int Price;
        public int CityStock;
        public int MaxStack = DefaultMaxStack;
        public bool AvailableFromAct2; // 판매 조건 "2막 이후"

        // 쓸 수 있는 효과인지 (연결 안 된 효과는 인벤토리에서 "아직 사용 불가")
        public bool IsImplemented => Effect switch
        {
            ConsumableEffect.RestoreHP or ConsumableEffect.RestoreMP or ConsumableEffect.RestoreFocus or ConsumableEffect.Cure
                or ConsumableEffect.RevealEvasion or ConsumableEffect.Firebomb or ConsumableEffect.Escape
                or ConsumableEffect.AmbushBonus or ConsumableEffect.CampRest or ConsumableEffect.LifeLaurel => true,
            ConsumableEffect.StatBuff => BuffStat == StatKind.Speed, // 신속의 앰플만 연결 (힘·지력 강장제는 2막 이후 — 버프 상태가 생기면)
            _ => false,
        };

        public bool UsableIn(UseContext context) => IsImplemented && (Usable & context) != 0;
    }
}
