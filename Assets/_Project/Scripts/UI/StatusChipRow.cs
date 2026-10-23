using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 상태 효과 칸 한 줄 — 적 HP 바·파티 카드 공용. 데이터의 StatusEffectDefinition.Icon이 있으면 그 그림 +
    // 남은 턴/스택 숫자, 없으면 짧은 글자(ShortLabel) 배지로 그린다. 상시 특성([회피 상태])도 같은 칸을 쓴다.
    public class StatusChipRow
    {
        private static readonly Color Border = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.25f, 0.15f, 0.3f);
        private static readonly Color TextColor = new Color(0.95f, 0.93f, 0.9f);

        public readonly struct Entry
        {
            public readonly string Label;
            public readonly Sprite Icon;
            public readonly string Count;
            public Entry(string label, Sprite icon, string count) { Label = label; Icon = icon; Count = count; }
        }

        private readonly List<(GameObject root, Image icon, Text label, Text count)> _chips = new();

        // min~max 영역을 count칸으로 나눈다.
        public StatusChipRow(Transform parent, string name, Vector2 min, Vector2 max, int count, int fontSize = 9)
        {
            float width = (max.x - min.x) / count;
            for (int i = 0; i < count; i++)
            {
                float x0 = min.x + i * width;
                var fill = UGUIKit.CreateThemedPanel(parent, $"{name}{i}", new Vector2(x0, min.y), new Vector2(x0 + width * 0.9f, max.y), Border, Fill, 1f);
                var icon = UGUIKit.CreateImage(fill, "Icon", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), Color.white);
                icon.preserveAspect = true;
                var label = UGUIKit.CreateText(fill, "Label", Vector2.zero, Vector2.one, "", fontSize, TextAnchor.MiddleCenter);
                label.color = TextColor;
                label.fontStyle = FontStyle.Bold;
                var countText = UGUIKit.CreateText(fill, "Count", new Vector2(0.45f, 0f), new Vector2(1f, 0.55f), "", fontSize - 1, TextAnchor.LowerRight);
                countText.color = TextColor;
                countText.gameObject.AddComponent<Outline>().effectColor = Color.black;
                fill.parent.gameObject.SetActive(false);
                _chips.Add((fill.parent.gameObject, icon, label, countText));
            }
        }

        public void Show(IReadOnlyList<Entry> entries)
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                var chip = _chips[i];
                bool has = i < entries.Count;
                chip.root.SetActive(has);
                if (!has) continue;

                var e = entries[i];
                bool art = e.Icon != null;
                chip.icon.enabled = art;
                chip.icon.sprite = e.Icon;
                chip.label.enabled = !art;
                chip.label.text = string.IsNullOrEmpty(e.Count) ? e.Label : $"{e.Label}{e.Count}";
                chip.count.enabled = art && !string.IsNullOrEmpty(e.Count);
                chip.count.text = e.Count;
            }
        }

        // 상태 목록 → 칸 내용. 숫자는 스택(×n)이 있으면 그것, 아니면 남은 턴 (정화 전까지면 비움).
        public static List<Entry> EntriesFor(StatusSet statuses, bool evasionState)
        {
            var list = new List<Entry>();
            if (evasionState) list.Add(new Entry("회피", null, ""));
            if (statuses == null) return list;
            foreach (var a in statuses.All)
            {
                string count = a.Stacks > 1 ? $"×{a.Stacks}" : a.RemainingTurns > 0 ? a.RemainingTurns.ToString() : "";
                string label = string.IsNullOrEmpty(a.Definition.ShortLabel) ? a.Definition.DisplayName : a.Definition.ShortLabel;
                list.Add(new Entry(label, a.Definition.Icon, count));
            }
            return list;
        }
    }
}
