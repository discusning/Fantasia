using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 전투 화면 UI (참고: Docs/Concept_Image/Concept/Fantasia_인터페이스_사진원본/01_포더킹1_실제_스크린샷/
    // 05_턴제_전투와_공격대상_선택_한국어.jpg). 하단 파티 카드는 PartyHudPanel을 그대로 쓰고, 여기서는
    //   ① 상단 행동 순서 타임라인  ② 상단 적 HP 바  ③ 행동 메뉴 + 판정 미리보기
    //   ④ 슬롯 판정 연출  ⑤ 피해 숫자  ⑥ 승리·패배 창 을 그린다.
    // 틀·버튼·게이지·아이콘 그림은 UITheme, 초상은 PortraitStudio(데이터의 Model/Portrait) — 아트가
    // 들어오면 코드 수정 없이 바뀐다. 전투 진행은 BattleController가 이 클래스의 메서드를 부른다.
    public partial class BattleHud : MonoBehaviour
    {
        private const int TimelineLength = 10;  // 한 화면 칸 수 (UI 솔루션 Q6)
        public const int TimelineMax = TimelineLength; // 11/11: 띠를 늘 10칸 꽉 채움 (예상 라운드로 채워서 넘겨 보기 필요 없음)
        private const int MaxEnemyBars = 3;
        private const int StatusIconSlots = 4;
        private const float SecondsPerSlot = 0.3f;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color AllyBorder = new Color(0.4f, 0.62f, 0.95f);
        private static readonly Color EnemyBorder = new Color(0.85f, 0.3f, 0.3f);
        private static readonly Color PanelFill = new Color(0.07f, 0.06f, 0.08f, 0.93f);
        private static readonly Color ButtonFill = new Color(0.36f, 0.12f, 0.14f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.8f, 0.77f, 0.71f); // 11/12 가독성 ②: 갈색 판 위 회색 글이 흐림 → 밝게
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color SlotUnknown = new Color(0.22f, 0.2f, 0.24f);
        public static readonly Color SlotSuccess = new Color(0.3f, 0.65f, 0.4f);
        public static readonly Color SlotFailure = new Color(0.7f, 0.25f, 0.25f);
        public static readonly Color CritColor = new Color(1f, 0.8f, 0.3f);
        public static readonly Color StatusColor = new Color(0.75f, 0.5f, 0.95f);
        // 11/13 팀장 결정: 떠오르는 숫자 색 구분 (예전엔 피해·회복 모두 초록) — 회복 초록 · 일반 피해 주황 · 크리티컬 붉은 주황(조금 크게)
        public static readonly Color HealColor = SlotSuccess;
        public static readonly Color DamageColor = new Color(1f, 0.6f, 0.18f);
        public static readonly Color CritDamageColor = new Color(1f, 0.3f, 0.14f);

        // 행동 메뉴 버튼 이벤트 (BattleController가 구독)
        public event Action AttackClicked;
        public event Action DefendClicked;
        public event Action RetreatClicked;
        public event Action ReviveClicked;
        public event Action InterruptClicked; // 보스 예고 중 전용 행동 (봉합 방해)
        public event Action<BattleUnit> EnemyBarClicked;
        public event Action<Fantasia.Data.SkillDefinition> SkillChosen;
        public event Action<Fantasia.Data.ConsumableDefinition> ItemChosen;
        // 보스 봉쇄 — 퇴각 버튼 비활성 (BattleController가 행동 메뉴 열 때마다 갱신)
        public bool RetreatBlocked { get; set; }

        // 이번 행동에 쓸 집중 개수 (2026-11-06 변경: 켬/끔 1개 → 0~남은 집중력 중 플레이어가 고름).
        // 실제로 쓰는 개수는 BattleController가 이 공격의 슬롯 수·스킬 집중 비용에 맞춰 자른다.
        public int FocusCount { get; private set; }
        public event Action FocusChanged; // 대상 미리보기(크리티컬 확률) 갱신용

        // 자동 플레이 점검용 — 결과 창 버튼을 누른 것과 같다.
        public void DevPressResult() => _onResultContinue?.Invoke();

        // 자동 플레이 점검용 — 스킬 버튼을 누른 것과 같다 (스킬 메뉴 스크린샷).
        public void DevOpenSkills()
        {
            if (!_skillPanel.activeSelf) ToggleSkills();
        }

        // 자동 플레이 점검용·이어하기 — 집중 −/+ 버튼으로 개수를 맞춘 것과 같다.
        public void DevSetFocus(int count) => SetFocusCount(count);

        // 11/15: 확정용 집중은 "스킬 비용을 내고 남는 만큼"까지만. 고른 스킬이 있으면 그 비용을 빼고 셈.
        private int FocusPickMax => _actionActor == null ? 0 : Mathf.Max(0, _actionActor.Member.CurrentFocus - (_chosen == ChosenKind.Skill && ChosenSkill != null ? ChosenSkill.FocusCost : 0));
        private void ClampFocusToChosen() { if (FocusCount > FocusPickMax) SetFocusCount(FocusPickMax); }
        private void RefreshSkillReasons()
        {
            if (_actionActor == null || !_skillPanel.activeSelf) return;
            for (int i = 0; i < _skillSlots.Count && i < _shownSkills.Count; i++)
            {
                var slot = _skillSlots[i]; var skill = _shownSkills[i];
                bool picked = _chosen == ChosenKind.Skill && ChosenSkill == skill;
                string reason = _actionActor.CannotUseReason(skill, picked ? 0 : FocusCount);
                slot.button.interactable = reason == null || picked;
                slot.sub.text = reason ?? SkillCostText(skill);
                slot.sub.color = reason == null ? TextDim : SlotFailure;
            }
        }
        private void SetFocusCount(int count)
        {
            int max = FocusPickMax;
            if (count > max && _actionActor != null && count <= _actionActor.Member.CurrentFocus && _chosen == ChosenKind.Skill && ChosenSkill != null)
                SetHint($"집중력 부족 — {ChosenSkill.DisplayName} 비용 {ChosenSkill.FocusCost}을 내면 확정 집중은 {max}개까지");
            FocusCount = Mathf.Clamp(count, 0, max);
            RefreshFocusPicker();
            RefreshSkillReasons();
            FocusChanged?.Invoke();
        }

        private const float FocusGemW = 19f, FocusGemH = 32f, FocusGemGap = 5f;
        private void RefreshFocusPicker()
        {
            if (_focusLabel == null) return;
            int max = _actionActor != null ? _actionActor.Member.CurrentFocus : 0;
            _focusCountText.text = max > 0 ? $"{FocusCount} / {max}" : "없음";
            _focusCountText.color = FocusCount > 0 ? CritColor : TextDim;
            // 가진 집중만큼 보석을 보임: 쓰기로 고른 것 = 활성화, 안 고른 것 = 비활성화
            var theme = UITheme.Current;
            // 11/13 팀장: 보석이 칸 밖으로 삐져나옴 → 칸(줄) 크기에 맞춰 보석 크기를 정함 (창 크기·집중 개수가 달라도 안에 들어감)
            float gemH = FocusGemH, gemW = FocusGemW, gap = FocusGemGap;
            if (_focusRow != null && _focusRow.rect.height > 1f)
            {
                gemH = Mathf.Min(FocusGemH, _focusRow.rect.height * 0.86f);
                gemW = gemH * (FocusGemW / FocusGemH); gap = gemW * 0.3f;
                float fit = (_focusRow.rect.width * 0.9f) / Mathf.Max(1f, max * gemW + Mathf.Max(0, max - 1) * gap);
                if (fit < 1f) { gemH *= fit; gemW *= fit; gap *= fit; }
            }
            float total = max * gemW + Mathf.Max(0, max - 1) * gap;
            for (int i = 0; i < _focusGems.Count; i++)
            {
                var gem = _focusGems[i];
                gem.gameObject.SetActive(i < max);
                if (i >= max) continue;
                gem.rectTransform.sizeDelta = new Vector2(gemW, gemH);
                gem.rectTransform.anchoredPosition = new Vector2(-total / 2f + gemW / 2f + i * (gemW + gap), 0f);
                bool on = i < FocusCount;
                if (theme != null && theme.FocusPip != null && theme.FocusPipEmpty != null) { gem.sprite = on ? theme.FocusPip : theme.FocusPipEmpty; gem.color = Color.white; }
                else gem.color = on ? CritColor : new Color(0.4f, 0.36f, 0.3f);
            }
        }

        private RectTransform _canvasRect;

        private class TimelineEntry { public GameObject Root; public Image Border; public RawImage Portrait; public Text Round; public Text Mark; public Text Speed; public Text Arrow; public Image Dashed; public Image Team; public Image FrameArt; public GameObject Divider; public RectTransform Rect; public bool IsAlly, Predicted; }
        private IReadOnlyList<BattleQueue.Entry> _timelineEntries;
        private int _timelineRound, _timelineOffset;
        private Button _timelinePrev, _timelineNext;
        private int _arrowRound;
        private Coroutine _arrowHide;
        private readonly Dictionary<BattleUnit, int> _arrows = new();
        private readonly List<TimelineEntry> _timeline = new();
        private Text _roundText;

        private class EnemyBar
        {
            public GameObject Root; public Image Border; public RawImage Portrait; public Text Name; public Text Badge;
            public Text Hp; public RectTransform HpFill; public StatusChipRow Statuses; public BattleUnit Unit; public Transform Plate;
            public GameObject Telegraph; public Text TelegraphText; public float FlashUntil;
            public GameObject Trait; public Text TraitText; // 11/15 팀장: 특성(회피 상태 등)을 카드에 짧게
        }
        private readonly List<EnemyBar> _enemyBars = new();

        private GameObject _actionPanel;
        private GameObject _skillPanel;
        private readonly List<(Button button, Image icon, Text name, Text sub)> _skillSlots = new();
        private readonly List<Fantasia.Data.SkillDefinition> _shownSkills = new();
        private GameObject _itemPanel;
        private readonly List<(Button button, Image icon, Text name, Text sub)> _itemSlots = new();
        private readonly List<Fantasia.Data.ConsumableDefinition> _shownItems = new();
        private BattleUnit _actionActor;
        private Text _actorText;
        private Text _hintText;
        private Button _attackButton, _skillButton, _itemButton, _defendButton, _reviveButton, _retreatButton;
        private Text _focusLabel, _focusCountText;
        private readonly List<Image> _focusGems = new(); // 11/13 집중 보석 (눌러서 개수 고름)
        private RectTransform _focusRow;

        private GameObject _rollPanel;
        private Text _rollTitle;
        private Text _rollResult;
        private RectTransform _rollSlotRow;
        private readonly List<Image> _rollSlots = new();
        private readonly List<Text> _rollSlotLabels = new();

        private GameObject _resultPanel;
        private Text _resultTitle;
        private Text _resultBody;
        private Text _resultButtonLabel;
        private Action _onResultContinue;
        private Action _onResultSecond;
        private GameObject _resultSecondButton;
        private RectTransform _resultButtonRect;
        private Text _resultSecondLabel;
        private RectTransform _resultCard, _resultBodyRect;
        private PanelDropView _victoryDrop, _defeatDrop, _shownDrop;
        private Coroutine _resultReveal;

        public static BattleHud Create()
        {
            var hud = new GameObject("BattleHud").AddComponent<BattleHud>();
            hud.Build();
            return hud;
        }

        // UI 위에 마우스가 있는지 — 전장 클릭(대상 선택)과 겹치지 않게.
        public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
