using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 화면 하단 파티 HUD — 3인 파티 카드를 가로로 나란히 (참고: Docs/Concept_Image/Concept/판타지아 보드판 컨셉.jpg).
    // 카드 한 장: 초상(+레벨) · 이름 · LP · 큰 HP 숫자 · 퀵 슬롯 4칸 · HP/MP 바 · 6대 스탯, 위에 집중력 표시.
    // 초상은 PortraitView + PortraitStudio(3D 렌더) — JobDefinition.Model에 모델 프리팹만 넣으면 그대로 바뀐다.
    // 틀·게이지·집중 칸 그림은 UITheme에서 온다.
    //
    // 씬마다 새로 만든다(DontDestroyOnLoad 아님) — 전투 씬은 자체 UI를 쓸 예정이라
    // 보드 HUD가 따라가지 않게. 수치는 매 프레임 PartyMember에서 다시 읽어 항상 최신 상태다.
    public class PartyHudPanel : MonoBehaviour
    {
        public static PartyHudPanel Instance { get; private set; }

        private const int CardCount = 3;
        private const int QuickSlotCount = PartyMember.QuickSlotCount; // System.md "퀵 슬롯" — 소모품 종류를 가리키고 수량은 파티 인벤토리

        // 퀵 슬롯 클릭 (전투: 지금 차례 캐릭터가 사용 / 보드: 월드 사용) · 카드(초상) 클릭 → 인벤토리 창
        public static event System.Action<PartyMember, int> QuickSlotClicked;
        public static event System.Action<PartyMember> CardClicked;

        private static readonly Color BorderNormal = new Color(0.45f, 0.4f, 0.32f);
        private static readonly Color BorderActive = new Color(0.95f, 0.78f, 0.4f);
        private static readonly Color CardFill = new Color(0.07f, 0.06f, 0.08f, 0.93f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color MpColor = new Color(0.3f, 0.5f, 0.9f);
        private static readonly Color BarBack = new Color(0.16f, 0.15f, 0.17f);
        private static readonly Color FocusOn = new Color(0.95f, 0.72f, 0.35f);
        private static readonly Color FocusOff = new Color(0.2f, 0.19f, 0.21f);

        private class Card
        {
            public GameObject Root;
            public Image Border;
            public PortraitView Portrait;
            public Text Level;
            public Text Name;
            public Text Lp;
            public Text BigHp;
            public RectTransform HpFill;
            public Text HpText;
            public RectTransform MpFill;
            public Text MpText;
            public Text Stats;
            public readonly List<Image> FocusPips = new();
            public Text Status;  // 카드 위 상태 문구 (행동 중인 캐릭터만)
            public StatusChipRow Statuses; // 걸린 상태 효과 (이름 배너 오른쪽)
            public readonly List<Image> QuickIcons = new();
            public readonly List<Text> QuickNames = new();
            public readonly List<Text> QuickCounts = new();
            public PartyMember Member;
        }

        private readonly List<Card> _cards = new();
        private Party _party;
        private PartyMember _active;
        private string _statusText;
        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("PartyHud").AddComponent<PartyHudPanel>().Initialize();
        }

        // active: 지금 턴인 캐릭터 (테두리 강조).
        public static void Show(Party party, PartyMember active)
        {
            EnsureExists();
            Instance._party = party;
            Instance._active = active;
            Instance.Refresh();
        }

        // 전투처럼 차례가 바뀌는 곳에서 — 강조할 캐릭터와 그 카드 위 문구 (예: "행동 선택 중").
        public static void SetActive(PartyMember active, string status = null)
        {
            if (Instance == null) return;
            Instance._active = active;
            Instance._statusText = status;
            Instance.Refresh();
        }

        private void Awake() => Initialize();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 에디터 미리보기용 — Play 모드가 아니면 촬영 카메라가 자동으로 안 돈다.
        public void RenderPortraitsNow()
        {
            PortraitStudio.RenderAllNow();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Build();
        }

        private void LateUpdate()
        {
            if (_party != null) Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                var member = _party != null && i < _party.Members.Count ? _party.Members[i] : null;
                card.Root.SetActive(member != null);
                if (member != null) Fill(card, member);
            }
        }

        private void Fill(Card card, PartyMember m)
        {
            card.Member = m;
            bool active = m == _active;
            card.Border.color = active ? BorderActive : BorderNormal;
            card.Portrait.Set(m.Job.Portrait, PortraitStudio.TextureFor(m.Job));
            card.Status.gameObject.SetActive(active && !string.IsNullOrEmpty(_statusText));
            card.Status.text = _statusText;
            card.Portrait.SetHighlighted(active);

            card.Level.text = m.Level.ToString();
            card.Name.text = m.IsPermanentlyDead ? $"{m.Job.DisplayName} (사망)" : m.IsDowned ? $"{m.Job.DisplayName} (쓰러짐)" : m.Job.DisplayName;
            card.Lp.text = $"LP {m.CurrentLP}";
            card.Statuses.Show(StatusChipRow.EntriesFor(m.Statuses, false));
            card.BigHp.text = m.CurrentHP.ToString();

            SetBar(card.HpFill, m.CurrentHP, m.MaxHP);
            card.HpText.text = $"{m.CurrentHP}/{m.MaxHP}";

            bool usesMp = m.MaxMP > 0;
            SetBar(card.MpFill, usesMp ? m.CurrentMP : 0, usesMp ? m.MaxMP : 1);
            card.MpText.text = usesMp ? $"{m.CurrentMP}/{m.MaxMP}" : "MP 없음";
            card.MpText.color = usesMp ? TextLight : TextDim;

            var s = m.FinalStats;
            card.Stats.text = $"힘 {s.Strength}   지력 {s.Intelligence}   활력 {s.Vitality}   속도 {s.Speed}   행운 {s.Luck}   인지 {s.Perception}";

            // 퀵 슬롯: 아이콘(없으면 이름 앞 두 글자) + 남은 개수
            var inventory = _party.Inventory;
            for (int q = 0; q < card.QuickIcons.Count; q++)
            {
                var def = m.QuickSlots[q];
                int count = def != null ? inventory.CountOf(def) : 0;
                card.QuickIcons[q].enabled = def != null && def.Icon != null;
                card.QuickIcons[q].sprite = def != null ? def.Icon : null;
                card.QuickIcons[q].color = count > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                card.QuickNames[q].text = def != null && def.Icon == null ? def.DisplayName.Substring(0, System.Math.Min(2, def.DisplayName.Length)) : "";
                card.QuickNames[q].color = count > 0 ? TextLight : TextDim;
                card.QuickCounts[q].text = def != null ? count.ToString() : "";
            }

            // 집중력: 최대치만큼 칸을 보이고 현재치만큼 채운다 (최대 6칸).
            for (int i = 0; i < card.FocusPips.Count; i++)
            {
                card.FocusPips[i].gameObject.SetActive(i < m.MaxFocus);
                card.FocusPips[i].color = i < m.CurrentFocus ? FocusOn : FocusOff;
            }
        }

        private static void SetBar(RectTransform fill, int current, int max)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
            fill.anchorMax = new Vector2(ratio, 1f);
        }

        private void Build()
        {
            var canvasGO = new GameObject("PartyHudCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<GraphicRaycaster>();

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            const float width = 0.31f, gap = 0.015f, left = 0.02f;
            for (int i = 0; i < CardCount; i++)
            {
                float x0 = left + i * (width + gap);
                _cards.Add(BuildCard(canvasGO.transform, i, new Vector2(x0, 0.015f), new Vector2(x0 + width, 0.2f)));
            }
        }

        private Card BuildCard(Transform parent, int index, Vector2 anchorMin, Vector2 anchorMax)
        {
            var card = new Card();
            var fill = UGUIKit.CreateThemedPanel(parent, $"PartyCard{index}", anchorMin, anchorMax, BorderNormal, CardFill, 2f);
            var outer = (RectTransform)fill.parent;
            card.Root = outer.gameObject;
            card.Border = outer.GetComponent<Image>();

            // 초상 + 레벨 배지 (왼쪽)
            card.Portrait = new PortraitView(fill, "Portrait", new Vector2(0.13f, 0.5f), 96f, BorderNormal);
            // 초상 클릭 → 그 캐릭터의 인벤토리·장비 창
            var portraitHit = UGUIKit.CreateImage(fill, "PortraitHit", new Vector2(0f, 0f), new Vector2(0.255f, 1f), Color.clear);
            portraitHit.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (card.Member != null) CardClicked?.Invoke(card.Member); });
            var levelFill = UGUIKit.CreateBorderedPanel(fill, "LevelBadge", new Vector2(0.03f, 0.13f), new Vector2(0.03f, 0.13f), BorderActive, new Color(0.2f, 0.08f, 0.1f), 1.5f);
            ((RectTransform)levelFill.parent).sizeDelta = new Vector2(30f, 30f);
            card.Level = MakeText(levelFill, "Value", Vector2.zero, Vector2.one, 16, TextAnchor.MiddleCenter, TextLight, bold: true);

            // 이름 배너 + LP (위)
            var nameFill = UGUIKit.CreateThemedPanel(fill, "NameBanner", new Vector2(0.26f, 0.8f), new Vector2(0.8f, 0.98f), BorderNormal, new Color(0.24f, 0.16f, 0.26f), 1f, UITheme.Current.Banner);
            card.Name = MakeText(nameFill, "Name", new Vector2(0.04f, 0f), Vector2.one, 13, TextAnchor.MiddleLeft, TextLight, bold: true);
            card.Lp = MakeText(fill, "LP", new Vector2(0.81f, 0.8f), new Vector2(0.98f, 0.98f), 12, TextAnchor.MiddleCenter, HpColor, bold: true);
            card.Statuses = new StatusChipRow(fill, "Status", new Vector2(0.56f, 0.815f), new Vector2(0.8f, 0.965f), 3, 8);

            // 큰 HP 숫자 + 퀵 슬롯 4칸 (가운데)
            card.BigHp = MakeText(fill, "BigHp", new Vector2(0.26f, 0.37f), new Vector2(0.47f, 0.8f), 34, TextAnchor.MiddleCenter, HpColor, bold: true);
            for (int q = 0; q < QuickSlotCount; q++)
            {
                float x0 = 0.49f + q * 0.123f;
                var slot = UGUIKit.CreateThemedPanel(fill, $"QuickSlot{q}", new Vector2(x0, 0.43f), new Vector2(x0 + 0.11f, 0.76f), BorderNormal, new Color(0.13f, 0.12f, 0.14f), 1f);
                var icon = UGUIKit.CreateImage(slot, "Icon", new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f), Color.white);
                icon.preserveAspect = true;
                icon.enabled = false;
                icon.raycastTarget = false;
                card.QuickIcons.Add(icon);
                card.QuickNames.Add(MakeText(slot, "Name", new Vector2(0f, 0.3f), new Vector2(1f, 1f), 10, TextAnchor.MiddleCenter, TextLight, bold: true));
                card.QuickCounts.Add(MakeText(slot, "Count", new Vector2(0f, 0f), new Vector2(0.94f, 0.42f), 9, TextAnchor.LowerRight, TextLight, bold: true));
                MakeText(slot, "Key", new Vector2(0.06f, 0f), new Vector2(1f, 0.4f), 9, TextAnchor.LowerLeft, TextDim, bold: false).text = (q + 1).ToString();
                var slotButton = slot.parent.gameObject.AddComponent<Button>();
                slotButton.targetGraphic = slot.parent.GetComponent<Image>();
                int slotIndex = q;
                slotButton.onClick.AddListener(() => { if (card.Member != null) QuickSlotClicked?.Invoke(card.Member, slotIndex); });
            }

            // HP / MP 바
            (card.HpFill, card.HpText) = BuildBar(fill, "HpBar", new Vector2(0.26f, 0.25f), new Vector2(0.98f, 0.36f), HpColor);
            (card.MpFill, card.MpText) = BuildBar(fill, "MpBar", new Vector2(0.26f, 0.13f), new Vector2(0.98f, 0.24f), MpColor);

            // 6대 스탯 (아래)
            card.Stats = MakeText(fill, "Stats", new Vector2(0.26f, 0f), new Vector2(0.99f, 0.12f), 9, TextAnchor.MiddleLeft, TextDim, bold: false);

            // 집중력 표시 — 카드 위쪽 바깥에 마름모 칸 (최대 6)
            for (int f = 0; f < PartyMember.AbsoluteMaxFocus; f++)
            {
                var pip = UGUIKit.CreateImage(outer, $"Focus{f}", new Vector2(0.29f, 1f), new Vector2(0.29f, 1f), FocusOff);
                var rt = (RectTransform)pip.transform;
                rt.sizeDelta = new Vector2(11f, 11f);
                rt.anchoredPosition = new Vector2(f * 17f, 11f);
                if (UITheme.Apply(pip, UITheme.Current.FocusPip, tint: true)) rt.localRotation = Quaternion.identity;
                else rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                card.FocusPips.Add(pip);
            }
            var focusLabel = MakeText(outer, "FocusLabel", new Vector2(0.26f, 1f), new Vector2(0.26f, 1f), 12, TextAnchor.MiddleRight, FocusOn, bold: true);
            ((RectTransform)focusLabel.transform).sizeDelta = new Vector2(48f, 20f);
            ((RectTransform)focusLabel.transform).anchoredPosition = new Vector2(-26f, 11f);
            focusLabel.text = "집중";

            // 행동 중일 때 카드 위에 뜨는 상태 문구 (예: "행동 선택 중")
            card.Status = MakeText(outer, "Status", new Vector2(0.5f, 1f), new Vector2(1f, 1f), 13, TextAnchor.MiddleRight, TextLight, bold: true);
            ((RectTransform)card.Status.transform).sizeDelta = new Vector2(0f, 22f);
            ((RectTransform)card.Status.transform).anchoredPosition = new Vector2(-6f, 12f);
            card.Status.gameObject.SetActive(false);

            return card;
        }

        private static (RectTransform fill, Text label) BuildBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var back = UGUIKit.CreateImage(parent, name, anchorMin, anchorMax, BarBack);
            UITheme.Apply(back, UITheme.Current.BarBack);
            var fill = UGUIKit.CreateImage(back.transform, "Fill", Vector2.zero, Vector2.one, color);
            UITheme.Apply(fill, UITheme.Current.BarFill, tint: true);
            var label = MakeText(back.transform, "Value", Vector2.zero, Vector2.one, 10, TextAnchor.MiddleCenter, TextLight, bold: true);
            return ((RectTransform)fill.transform, label);
        }

        private static Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int size, TextAnchor align, Color color, bool bold)
        {
            var text = UGUIKit.CreateText(parent, name, anchorMin, anchorMax, "", size, align);
            text.color = color;
            if (bold) text.fontStyle = FontStyle.Bold;
            return text;
        }
    }
}
