using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/12 팀장 결정: 가방 설명 카드(장착 중·선택)에 상점 설명 칸과 같은 큰 아이템 그림.
    // 그림이 있으면 카드 위쪽에 그림, 글은 그 아래로. 그림이 없으면(아트 전) 예전 배치 그대로.
    public partial class InventoryPanel
    {
        private static readonly (Vector2 min, Vector2 max)[] TextPlain =
        {
            (new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f)),   // Header
            (new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.9f)),    // Name
            (new Vector2(0.04f, 0.69f), new Vector2(0.96f, 0.8f)),   // Main
            (new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.68f)),   // Body
        };
        private static readonly (Vector2 min, Vector2 max)[] TextWithIcon =
        {
            (new Vector2(0.04f, 0.6f), new Vector2(0.96f, 0.66f)),
            (new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.6f)),
            (new Vector2(0.04f, 0.44f), new Vector2(0.96f, 0.52f)),
            (new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.43f)),
        };

        private static void BuildCardIcon(Transform fill, CardView card)
        {
            card.Icon = UGUIKit.CreateImage(fill, "Icon", new Vector2(0.15f, 0.67f), new Vector2(0.85f, 0.97f), Color.white);
            card.Icon.preserveAspect = true;
            card.Icon.raycastTarget = false;
            card.Icon.enabled = false;
        }

        private static void SetCardIcon(CardView card, Sprite sprite)
        {
            if (card.Icon == null) return;
            card.Icon.enabled = sprite != null;
            card.Icon.sprite = sprite;
            var layout = sprite != null ? TextWithIcon : TextPlain;
            var texts = new[] { card.Header, card.Name, card.Main, card.Body };
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i].rectTransform.anchorMin = layout[i].min;
                texts[i].rectTransform.anchorMax = layout[i].max;
            }
        }

        // 자동 플레이 점검용
        public static bool DevSelectedCardHasIcon => Instance != null && Instance._selectedCard.Icon != null && Instance._selectedCard.Icon.enabled;
    }
}
