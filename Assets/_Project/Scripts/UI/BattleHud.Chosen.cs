using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/11 팀장 지적: 스킬을 골라도 무엇을 골랐는지·취소됐는지 알 수 없음.
    // 고른 칸 = 빨간 테두리, 나머지 칸 = 어둡게, 안내 줄 = 크게 "약점 간파 → 대상 적 선택".
    // 방어 보호막: 캐릭터 발밑에 방패 + 숫자 (다음 자기 차례에 사라짐).
    public partial class BattleHud
    {
        private enum ChosenKind { None, Attack, Skill, Item }
        private ChosenKind _chosen;
        private static readonly Color ChosenRed = new Color(1f, 0.12f, 0.08f, 1f);
        private static readonly Color HintBig = new Color(1f, 0.62f, 0.5f);

        // 스킬·소모품 칸을 접거나 다른 칸으로 바꿀 때 — 고른 행동을 취소해 달라는 신호
        public event Action CancelRequested;

        public void MarkChosenAttack() => Mark(ChosenKind.Attack, -1);
        public void MarkChosenSkill(Fantasia.Data.SkillDefinition skill) => Mark(ChosenKind.Skill, _shownSkills.IndexOf(skill));
        public void MarkChosenItem(Fantasia.Data.ConsumableDefinition item) => Mark(ChosenKind.Item, _shownItems.IndexOf(item));

        public void ClearChosen()
        {
            _chosen = ChosenKind.None;
            SetSlotLooks(null, -1);
            ApplyHintStyle();
        }

        private void Mark(ChosenKind kind, int index)
        {
            _chosen = kind;
            var list = kind == ChosenKind.Skill ? _skillSlots : kind == ChosenKind.Item ? _itemSlots : null;
            SetSlotLooks(list, index);
            ApplyHintStyle();
        }

        // 고른 칸만 빨간 테두리, 같은 줄 나머지는 어둡게. list가 null이면 모두 원래대로.
        private void SetSlotLooks(List<(Button button, Image icon, Text name, Text sub)> list, int index)
        {
            foreach (var group in new[] { _skillSlots, _itemSlots })
                for (int i = 0; i < group.Count; i++)
                {
                    bool chosen = group == list && i == index;
                    bool dim = group == list && i != index;
                    SetLook(group[i].button, chosen, dim);
                }
            SetLook(_attackButton, _chosen == ChosenKind.Attack, false);
        }

        private static void SetLook(Selectable button, bool chosen, bool dim)
        {
            if (button == null) return;

            // 빨간 틀 (칸 위에 덮는 테두리 — 외곽선만으로는 흐려 보였음)
            var frame = button.transform.Find("ChosenFrame");
            if (chosen && frame == null)
            {
                var root = UGUIKit.CreateRect(button.transform, "ChosenFrame", Vector2.zero, Vector2.one);
                root.offsetMin = new Vector2(-3f, -3f); root.offsetMax = new Vector2(3f, 3f);
                const float w = 3f; // 선 두께
                foreach (var (min, max, oMin, oMax) in new[] {
                    (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -w), Vector2.zero),   // 위
                    (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, w)),    // 아래
                    (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f)),    // 왼쪽
                    (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-w, 0f), Vector2.zero) }) // 오른쪽
                {
                    var line = UGUIKit.CreateImage(root, "Line", min, max, ChosenRed);
                    line.rectTransform.offsetMin = oMin; line.rectTransform.offsetMax = oMax;
                    line.raycastTarget = false;
                }
                frame = root;
            }
            if (frame != null) { frame.gameObject.SetActive(chosen); frame.SetAsLastSibling(); }
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dim ? 0.4f : 1f;
        }

        private void ApplyHintStyle()
        {
            if (_hintText == null) return;
            bool big = _chosen != ChosenKind.None;
            _hintText.fontSize = big ? 15 : 10;
            _hintText.fontStyle = big ? FontStyle.Bold : FontStyle.Normal;
            _hintText.color = big ? HintBig : TextLight;
            _hintText.resizeTextForBestFit = big; _hintText.resizeTextMinSize = 10; _hintText.resizeTextMaxSize = 15;
        }

        // ── 방어 보호막 표시 (캐릭터 발밑 방패 + 남은 양) ──
        // 11/12 팀장 지적: 방패가 캐릭터 이동을 따라갔다가 제자리로 바로 안 돌아옴 — 보호막이 바뀐 순간의 좌표를 고정으로 들고 있었음.
        // → 캐릭터(Follow)를 매 프레임 따라감. World는 Follow가 없을 때만.
        private sealed class ShieldMark { public RectTransform Root; public Text Amount; public Vector3 World; public Camera Cam; public Transform Follow; public Vector3 Offset; }
        private readonly Dictionary<object, ShieldMark> _shieldMarks = new();

        public void SetShieldMark(object key, Camera cam, Vector3 worldFoot, int amount) => SetShieldMark(key, cam, null, worldFoot, amount);

        // follow가 있으면 offset = follow 기준 위치 (머리 위)
        public void SetShieldMark(object key, Camera cam, Transform follow, Vector3 offset, int amount)
        {
            if (_resultPanel != null && _resultPanel.activeSelf) amount = 0; // 결과 창이 떠 있으면 방패 표시 안 함 (11/12)
            _shieldMarks.TryGetValue(key, out var mark);
            if (amount <= 0)
            {
                if (mark != null) { Destroy(mark.Root.gameObject); _shieldMarks.Remove(key); }
                return;
            }
            if (mark == null)
            {
                var icon = UGUIKit.CreateImage(_canvasRect, "ShieldMark", Vector2.zero, Vector2.zero, Color.white);
                icon.sprite = UITheme.Current != null ? UITheme.Current.ShieldIcon : null;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                if (icon.sprite == null) icon.color = new Color(0.95f, 0.95f, 0.98f, 0.9f);
                var rt = icon.rectTransform;
                rt.sizeDelta = new Vector2(46f, 46f);
                // 맨 뒤(마지막)에 둠 — 맨 앞에 두면 전투 화면 바탕 판에 가려 안 보였음
                var text = UGUIKit.CreateText(rt, "Amount", Vector2.zero, Vector2.one, "", 18, TextAnchor.MiddleCenter);
                text.color = new Color(0.15f, 0.15f, 0.2f);
                text.fontStyle = FontStyle.Bold;
                mark = new ShieldMark { Root = rt, Amount = text };
                _shieldMarks[key] = mark;
            }
            mark.Cam = cam;
            mark.Follow = follow;
            mark.Offset = offset;
            mark.World = follow != null ? follow.position + offset : offset;
            mark.Amount.text = amount.ToString();
            PlaceShieldMark(mark);
        }

        public int DevShieldMarkCount => _shieldMarks.Count;
        public Vector3? DevShieldMarkWorld(object key) => _shieldMarks.TryGetValue(key, out var m) ? m.World : (Vector3?)null;

        public void ClearShieldMarks()
        {
            foreach (var m in _shieldMarks.Values) if (m.Root != null) Destroy(m.Root.gameObject);
            _shieldMarks.Clear();
        }

        private void LateUpdate()
        {
            foreach (var m in _shieldMarks.Values) PlaceShieldMark(m);
        }

        private void PlaceShieldMark(ShieldMark m)
        {
            if (m.Root == null || m.Cam == null) return;
            if (m.Follow != null) m.World = m.Follow.position + m.Offset;
            Vector2 screen = m.Cam.WorldToScreenPoint(m.World);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            m.Root.anchoredPosition = local + _canvasRect.rect.size * 0.5f;
        }
    }
}
