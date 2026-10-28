using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // UI 아트 교체 창구 — Assets/_Project/Resources/UITheme.asset 하나에 스프라이트·프리팹·글꼴을 넣으면
    // 코드 수정 없이 전투·HUD UI가 그 아트로 바뀐다. 비어 있는 칸은 지금처럼 단색 도형으로 그린다.
    //
    // 9-slice가 필요한 틀(패널·버튼·게이지)은 스프라이트 에디터에서 Border를 지정해 두면 자동으로
    // Sliced로 늘어난다. 색 의미가 있는 칸(HP 빨강, MP 파랑, 아군/적 테두리)은 그림 위에 색을 곱한다.
    [CreateAssetMenu(fileName = "UITheme", menuName = "Fantasia/UI Theme")]
    public class UITheme : ScriptableObject
    {
        public const string ResourcePath = "UITheme";

        [Header("패널·배너·버튼")]
        public Sprite PanelFrame;   // 패널 바깥 테두리
        public Sprite PanelFill;    // 패널 안쪽 바탕
        public Sprite Banner;       // 이름 배너
        public Sprite Button;

        [Header("게이지")]
        public Sprite BarBack;
        public Sprite BarFill;      // HP/MP 공용 — 색은 코드에서 곱한다

        [Header("판정·자원")]
        public Sprite SlotIcon;     // 슬롯 판정 아이콘 (미정/성공/실패는 색으로 구분)
        public Sprite FocusPip;     // 집중력 칸

        [Header("초상·타임라인")]
        public Sprite PortraitFrame;
        public Sprite TimelineFrame;
        [Header("보드 상단 바 (아군·적 턴 시계·위협 트랙)")]
        public Sprite TopBarFrame;
        public Sprite PhaseDialAllyActive, PhaseDialAllyIdle;   // 시계 왼쪽(아군) 반원 — 활성/비활성
        public Sprite PhaseDialEnemyActive, PhaseDialEnemyIdle; // 시계 오른쪽(적) 반원
        public Sprite ThreatNotch;     // 피의 흔적 칸 (UI 솔루션 Q2로 10칸은 화면에서 뺌 — 보관)
        public Sprite DaySun;          // 가운데 원 해 (N02 투명 PNG 오면) — 아침·낮
        public Sprite NightMoon;       // 가운데 원 달 (N01 투명 PNG 오면) — 저녁·새벽
        public Sprite RegionBanner;    // 지역명 배너
        public Sprite AllyTurnBanner;  // 턴 전환 배너 — 아군 턴
        public Sprite EnemyTurnBanner; // 턴 전환 배너 — 적 턴

        [Header("전투 행동 아이콘")]
        public Sprite AttackIcon;
        public Sprite SkillIcon;
        public Sprite ItemIcon;
        public Sprite DefendIcon;
        public Sprite RetreatIcon;
        public Sprite ReviveIcon;
        public Sprite FocusToggleIcon;

        // 아이콘 Z01~Z44 (Docs/Art/Fantasia_Icon_Usage_Guide.pdf) — 메뉴 Fantasia/UI/Apply Icon Set이 채운다.
        // 비어 있으면 지금처럼 글자로만 보인다.
        [Header("아이콘 — 자원·상태 (Z01~Z04, Z37)")]
        public Sprite BloodTrailIcon;  // Z01 피의 흔적
        public Sprite SoulIcon;        // Z02 영혼 흡수
        public Sprite CentIcon;        // Z03 Cent
        public Sprite LpIcon;          // Z04 LP
        public Sprite LevelBadge;      // Z37 레벨 배지 (숫자는 글자로 위에)

        [Header("아이콘 — 능력치 (Z05~Z10, 힘·속도·활력·지력·행운·인지 순)")]
        public Sprite StrengthIcon, SpeedIcon, VitalityIcon, IntelligenceIcon, LuckIcon, PerceptionIcon;

        [Header("아이콘 — 집중력·퀵 슬롯 (Z29·Z30·Z35·Z36)")]
        public Sprite FocusPipEmpty;   // Z30 빈 칸 (보유 칸은 위 FocusPip = Z29)
        public Sprite QuickSlotFrame;  // Z35
        public Sprite QuickSlotSelected; // Z36

        [Header("아이콘 — 의뢰·기록 (Z15~Z17, Z33·Z34)")]
        public Sprite QuestIcon;       // Z15
        public Sprite EvidenceIcon;    // Z16 검시 기록 (퀘스트 물건)
        public Sprite TreasureIcon;    // Z17 보물·전리품
        public Sprite ObjectiveOpen;   // Z33
        public Sprite ObjectiveDone;   // Z34

        [Header("아이콘 — 지도 칸 표식 (Z17, Z23~Z28)")]
        public Sprite HubMarker;       // Z23 그리니치 거점
        public Sprite HoundMarker;     // Z24 그림자 사냥개 칸
        public Sprite EnemyMarker;     // 일반 적 칸 공용 표식 (디자인 새 그림 오기 전 Z19 교차 칼)

        [Header("대화 인물 (UI 솔루션 Q8) — 화자 이름이 같으면 대화 창 왼쪽에. 직업 화자는 직업 초상을 씀")]
        public SpeakerPortrait[] SpeakerPortraits = System.Array.Empty<SpeakerPortrait>();

        public Sprite PortraitForSpeaker(string speaker)
        {
            foreach (var p in SpeakerPortraits) if (p != null && p.Sprite != null && p.Speaker == speaker) return p.Sprite;
            return null;
        }
        public Sprite BossMarker;      // Z25
        public Sprite RecoveryMarker;  // Z26 우물(회복 칸)
        public Sprite EventMarker;     // Z27
        public Sprite ExitMarker;      // Z28

        public Sprite StatIcon(Fantasia.Data.StatKind kind) => kind switch
        {
            Fantasia.Data.StatKind.Strength => StrengthIcon,
            Fantasia.Data.StatKind.Speed => SpeedIcon,
            Fantasia.Data.StatKind.Vitality => VitalityIcon,
            Fantasia.Data.StatKind.Intelligence => IntelligenceIcon,
            Fantasia.Data.StatKind.Luck => LuckIcon,
            _ => PerceptionIcon,
        };

        [Header("전장")]
        // 공격 대상 발밑 표시. 비우면 붉은 육각형 링을 코드로 그린다.
        public GameObject TargetMarkerPrefab;

        [Header("글꼴")]
        // 한글 글꼴(.ttf/.otf) — 비우면 Unity 기본 글꼴.
        public Font Font;

        private static UITheme _current;

        // Resources에 에셋이 없으면 빈 테마(전부 단색 폴백)를 쓴다.
        public static UITheme Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<UITheme>(ResourcePath);
                    if (_current == null) _current = CreateInstance<UITheme>();
                }
                return _current;
            }
        }

        // 스프라이트가 있으면 이미지에 입히고 true. tint=false면 색을 흰색으로 돌려 그림 원색을 쓴다.
        // sliceHeight: 9-slice 그림을 얇은 칸(게이지 등)에 쓸 때, 그림 전체 높이를 화면 몇 단위로 볼지 — 모서리가 칸보다 커지지 않게.
        public static bool Apply(Image image, Sprite sprite, bool tint = false, float sliceHeight = 0f)
        {
            if (image == null || sprite == null) return false;
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            if (image.type == Image.Type.Sliced && sliceHeight > 0f) image.pixelsPerUnitMultiplier = sprite.rect.height / sliceHeight;
            if (!tint) image.color = Color.white;
            return true;
        }
    }
}

namespace Fantasia.UI
{
    [System.Serializable]
    public class SpeakerPortrait
    {
        public string Speaker;  // 대사의 화자 이름과 같게 (예: "아벨 모로")
        public Sprite Sprite;
    }
}
