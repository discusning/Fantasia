using UnityEngine;

namespace Fantasia.Data
{
    // 직업(플레이어블 캐릭터) 정의 (System.md "레벨·기본 자원" 표 + "직업" 절).
    // D03 스키마 — 기존 Fantasia.Characters.CharacterDefinition(구 프로토타입)과는
    // 별개다. 실제 교체는 D04에서 진행한다.
    [CreateAssetMenu(fileName = "Job", menuName = "Fantasia/Data/Job Definition")]
    public class JobDefinition : ScriptableObject
    {
        [Header("식별")]
        public JobId Id;
        public string DisplayName;
        [TextArea] public string Role; // 역할 한 줄 (예: "근접 공격·수사·약점 분석")

        [Header("아트")]
        // 캐릭터 3D 모델 프리팹 — 전투 필드와 초상 칸(PortraitStage)이 같이 쓴다. 비워두면 기본 도형으로
        // 조립한 임시 인형(UI/PlaceholderFigure). 정면이 +Z를 보도록 만든 프리팹을 넣으면 된다.
        [UnityEngine.Serialization.FormerlySerializedAs("PortraitModel")]
        public GameObject Model;
        // 2D 초상 그림이 따로 생기면 여기에 — 넣으면 3D 모델보다 우선한다.
        // Texture Type을 Sprite (2D and UI)로 임포트할 것.
        public Sprite Portrait;

        [Header("시작 자원 (System.md 직업별 초기 HP/MP/집중력/LP 표)")]
        public int StartingHP;
        public int StartingMP;
        public int StartingFocus = 3;
        public int StartingLP;

        [Header("전투 기본 능력치")]
        public int BasePhysicalAttack;  // 공격력 (물리 직업만 보유, 나머지 0)
        public int BaseMagicAttack;     // 마력 (마법 직업만 보유, 나머지 0)
        public int BasePhysicalDefense;
        public int BaseMagicDefense;

        [Header("6대 스탯 (힘/속도/활력/지력/행운/인지)")]
        public StatBlock Stats;

        [Header("장비")]
        public WeaponCategory[] AllowedWeaponCategories;
        public WeaponDefinition StartingWeapon;

        [Header("스킬 (패시브 1 + 액티브 3, System.md 기준)")]
        public SkillDefinition[] Skills;

        // 최대 HP 기본값 = 활력 × 2 (System.md "레벨·기본 자원")
        public int ComputeBaseMaxHP() => Stats.Vitality * 2;

        // MP를 쓰는 직업(역병 의사·연금술사·지휘자·도박가)만 활력×2, 나머지는 0에서 시작.
        // StartingMP가 이미 그 값을 담고 있으므로 레벨업 시 갱신 공식에서 재사용한다.
        public bool UsesMP => StartingMP > 0;
    }
}
