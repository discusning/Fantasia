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
        public Sprite ThreatNotch;     // 피의 흔적 칸
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
        public static bool Apply(Image image, Sprite sprite, bool tint = false)
        {
            if (image == null || sprite == null) return false;
            image.sprite = sprite;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            if (!tint) image.color = Color.white;
            return true;
        }
    }
}
