using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/12 팀장 결정: 얻은 아이템은 하나씩 알림으로 띄우지 않고 전투 승리 결과 창에서 한꺼번에 보여 줌.
    // 결과 카드 아래쪽 띠에 칸마다 그림(자체 그림 또는 종류 아이콘) + 등급 색 이름, 그림 둘레도 등급 색.
    public partial class BattleHud
    {
        public const int MaxResultItems = 4;
        private RectTransform _itemStrip;
        private readonly List<(GameObject root, Image frame, Image icon, Text name)> _itemCells = new();
        private List<(Sprite icon, string name, Color color)> _pendingItems;

        // 다음 결과 창(승리)에 보일 아이템. 빈 목록이면 띠 없음
        public void SetResultItems(List<(Sprite icon, string name, Color color)> items) => _pendingItems = items;

        private void BuildItemStrip()
        {
            if (_itemStrip != null || _resultCard == null) return;
            var fill = _resultBody.transform.parent;
            _itemStrip = UGUIKit.CreateRect(fill, "Items", new Vector2(0.04f, 0.19f), new Vector2(0.96f, 0.5f));
            for (int i = 0; i < MaxResultItems; i++)
            {
                var cell = UGUIKit.CreateRect(_itemStrip, $"Item{i}", Vector2.zero, Vector2.one);
                var frame = UGUIKit.CreateImage(cell, "Frame", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Color.white);
                frame.rectTransform.pivot = new Vector2(0.5f, 1f);
                frame.rectTransform.sizeDelta = new Vector2(46f, 46f);
                frame.raycastTarget = false;
                var back = UGUIKit.CreateImage(frame.transform, "Back", Vector2.zero, Vector2.one, new Color(0.1f, 0.08f, 0.07f, 1f));
                back.rectTransform.offsetMin = new Vector2(2f, 2f);
                back.rectTransform.offsetMax = new Vector2(-2f, -2f);
                back.raycastTarget = false;
                var icon = UGUIKit.CreateImage(back.transform, "Icon", Vector2.zero, Vector2.one, Color.white);
                icon.rectTransform.offsetMin = new Vector2(3f, 3f);
                icon.rectTransform.offsetMax = new Vector2(-3f, -3f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var name = UGUIKit.CreateText(cell, "Name", new Vector2(0f, 0f), new Vector2(1f, 1f), "", 12, TextAnchor.UpperCenter);
                name.rectTransform.offsetMax = new Vector2(-2f, -50f);
                name.rectTransform.offsetMin = new Vector2(2f, 0f);
                name.raycastTarget = false;
                _itemCells.Add((cell.gameObject, frame, icon, name));
            }
            _itemStrip.gameObject.SetActive(false);
        }

        // ShowPanelResult 끝에서 부름: 아이템이 있으면 본문을 위로 올리고 아래 띠에 칸
        private void ApplyResultItems(bool victory)
        {
            BuildItemStrip();
            var items = victory ? _pendingItems : null;
            _pendingItems = null;
            bool any = items != null && items.Count > 0;
            if (_itemStrip == null) return;
            _itemStrip.gameObject.SetActive(any);
            DevResultItemCount = any ? Mathf.Min(items.Count, MaxResultItems) : 0;
            if (!any) return;
            _resultBodyRect.anchorMin = new Vector2(0.06f, 0.5f);
            _resultCard.sizeDelta = new Vector2(_resultCard.sizeDelta.x, 330f);
            int n = Mathf.Min(items.Count, MaxResultItems);
            for (int i = 0; i < _itemCells.Count; i++)
            {
                var c = _itemCells[i];
                bool on = i < n;
                c.root.SetActive(on);
                if (!on) continue;
                var rt = (RectTransform)c.root.transform;
                rt.anchorMin = new Vector2((float)i / n, 0f);
                rt.anchorMax = new Vector2((float)(i + 1) / n, 1f);
                c.frame.color = items[i].color;
                c.icon.sprite = items[i].icon;
                c.icon.enabled = items[i].icon != null;
                c.name.text = items[i].name;
                c.name.color = items[i].color;
            }
        }

        private void HideResultItems()
        {
            if (_itemStrip != null) _itemStrip.gameObject.SetActive(false);
            DevResultItemCount = 0;
        }

        public int DevResultItemCount { get; private set; }
    }
}
