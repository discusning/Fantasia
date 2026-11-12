using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/12 팀장 지적: 아군에게 쓰는 스킬·아이템·부활은 전장의 캐릭터 모델을 눌러 골랐는데,
    // 가운데 행동 창(스킬 칸)이 아군 모델을 가려 누르기 어려움 → 아래 캐릭터 카드를 눌러도 대상으로 고름.
    // 대상 고르기 중에만 카드 위에 덮개가 생김: 고를 수 있는 카드 = 금빛 테두리 + "대상" 표, 고를 수 없는 카드 = 어둡게.
    // 평소에는 덮개가 꺼져 있어 퀵 슬롯·초상 클릭은 그대로. 전장 모델 클릭도 그대로 됨.
    public partial class PartyHudPanel
    {
        public static event System.Action<PartyMember> TargetCardClicked;
        public static PartyMember HoveredTargetCard { get; private set; }

        private static readonly Color PickEdge = new Color(1f, 0.82f, 0.35f);
        private static readonly Color PickEdgeHover = new Color(1f, 0.97f, 0.75f);
        private static readonly Color PickDim = new Color(0f, 0f, 0f, 0.6f);

        private class TargetPickView
        {
            public GameObject Root;
            public Image Fill;
            public readonly List<Image> Edges = new();
            public GameObject Tag;
            public bool Valid;
        }

        private readonly Dictionary<Card, TargetPickView> _pick = new();
        private System.Func<PartyMember, bool> _pickValid;

        private void BuildTargetPick(Card card)
        {
            var outer = (RectTransform)card.Root.transform;
            var v = new TargetPickView();
            var fill = UGUIKit.CreateImage(outer, "TargetPick", Vector2.zero, Vector2.one, Color.clear);
            v.Root = fill.gameObject;
            v.Fill = fill;
            const float t = 4f; // 테두리 두께
            (Vector2 min, Vector2 max, Vector2 size)[] edges =
            {
                (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, t)),
                (new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, t)),
                (new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(t, 0f)),
                (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(t, 0f)),
            };
            foreach (var (min, max, size) in edges)
            {
                var e = UGUIKit.CreateImage(fill.transform, "Edge", min, max, PickEdge);
                e.rectTransform.sizeDelta = size;
                e.raycastTarget = false;
                v.Edges.Add(e);
            }
            // 카드 위쪽 가운데 "대상" 표
            var tag = UGUIKit.CreateThemedPanel(fill.transform, "Tag", new Vector2(0.38f, 1f), new Vector2(0.62f, 1f), PickEdge, new Color(0.22f, 0.15f, 0.06f, 0.96f), 1.5f);
            var tagRt = (RectTransform)tag.parent;
            tagRt.sizeDelta = new Vector2(0f, 26f);
            tagRt.anchoredPosition = new Vector2(0f, 15f);
            var label = UGUIKit.CreateText(tag, "Label", Vector2.zero, Vector2.one, "▼ 대상 선택", 15, TextAnchor.MiddleCenter);
            label.color = new Color(1f, 0.93f, 0.75f);
            label.raycastTarget = false;
            foreach (var g in tag.parent.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            v.Tag = tag.parent.gameObject;

            var button = v.Root.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                if (!v.Valid || card.Member == null) return;
                Fantasia.Core.SfxPlayer.Click();
                TargetCardClicked?.Invoke(card.Member);
            });
            var trigger = v.Root.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () => { if (v.Valid) { HoveredTargetCard = card.Member; Paint(v, true); } });
            AddTrigger(trigger, EventTriggerType.PointerExit, () => { if (HoveredTargetCard == card.Member) HoveredTargetCard = null; Paint(v, false); });
            v.Root.SetActive(false);
            _pick[card] = v;
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private static void Paint(TargetPickView v, bool hover)
        {
            v.Fill.color = !v.Valid ? PickDim : hover ? new Color(1f, 0.9f, 0.6f, 0.16f) : Color.clear;
            foreach (var e in v.Edges) { e.enabled = v.Valid; e.color = hover ? PickEdgeHover : PickEdge; }
            v.Tag.SetActive(v.Valid);
        }

        // 대상 고르기 시작(valid: 고를 수 있는 아군) / 끝(null)
        public static void SetTargetPick(System.Func<PartyMember, bool> valid)
        {
            if (Instance == null) return;
            Instance._pickValid = valid;
            HoveredTargetCard = null;
            Instance.RefreshTargetPick();
        }

        private void RefreshTargetPick()
        {
            foreach (var card in _cards)
            {
                if (!_pick.TryGetValue(card, out var v)) continue;
                bool on = _pickValid != null && card.Member != null && card.Root.activeSelf;
                v.Root.SetActive(on);
                if (!on) continue;
                v.Root.transform.SetAsLastSibling();
                v.Valid = _pickValid(card.Member);
                Paint(v, false);
            }
        }

        // 점검용
        public static bool DevTargetPickOn => Instance != null && Instance._pickValid != null;
        public static int DevTargetPickValidCount
        {
            get
            {
                int n = 0;
                if (Instance != null) foreach (var kv in Instance._pick) if (kv.Value.Root.activeSelf && kv.Value.Valid) n++;
                return n;
            }
        }
        // 그 캐릭터 카드를 실제 클릭처럼 누름 (UI 이벤트 경유)
        public static bool DevClickTargetCard(PartyMember member)
        {
            if (Instance == null) return false;
            foreach (var kv in Instance._pick)
                if (kv.Key.Member == member && kv.Value.Root.activeInHierarchy)
                {
                    ExecuteEvents.Execute(kv.Value.Root, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                    return true;
                }
            return false;
        }
        // 카드 화면 영역 (점검: 덮개가 카드와 같은 자리인지)
        public static Rect DevCardScreenRect(PartyMember member)
        {
            if (Instance == null) return default;
            foreach (var c in Instance._cards)
                if (c.Member == member)
                {
                    var corners = new Vector3[4];
                    ((RectTransform)c.Root.transform).GetWorldCorners(corners);
                    return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
                }
            return default;
        }
    }
}
