using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/11 팀장 지적: 지금 차례 카드도 덜 빛나 보임 — 예전엔 판 그림 색만 바꿔서 얼굴·숫자·바는 그대로였음.
    // 차례가 있는 곳(전투)에서만: 차례 아닌 카드 = 카드 전체 위에 어두운 막, 지금 차례 = 금빛 테두리(숨쉬듯) + 조금 크게.
    // 보드(Show로 파티장 지정)는 어둡게 안 함 — 파티장 카드에 금 테두리만 은은하게.
    public partial class PartyHudPanel
    {
        private static readonly Color TurnGold = new Color(1f, 0.8f, 0.36f, 1f);
        private const float DimAlpha = 0.45f, ActiveScale = 1.035f;
        private bool _turnMode; // SetActive(전투)로 왔을 때만 어둡게·숨쉬기·크게. Show(보드)는 파티장 금 테두리만

        private static void BuildTurnLook(Card card, RectTransform outer)
        {
            var dim = UGUIKit.CreateImage(outer, "TurnDim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0f));
            dim.raycastTarget = false;
            dim.transform.SetAsLastSibling();
            card.TurnDim = dim;

            var glow = UGUIKit.CreateRect(outer, "TurnGlow", Vector2.zero, Vector2.one);
            glow.offsetMin = new Vector2(-5f, -5f); glow.offsetMax = new Vector2(5f, 5f);
            const float w = 4f;
            foreach (var (min, max, oMin, oMax) in new[] {
                (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -w), Vector2.zero),
                (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, w)),
                (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f)),
                (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-w, 0f), Vector2.zero) })
            {
                var line = UGUIKit.CreateImage(glow, "Line", min, max, TurnGold);
                line.rectTransform.offsetMin = oMin; line.rectTransform.offsetMax = oMax;
                line.raycastTarget = false;
            }
            glow.SetAsLastSibling();
            glow.gameObject.SetActive(false);
            card.TurnGlow = glow;
        }

        private void ApplyTurnLook(Card card, bool active)
        {
            bool turnBased = _turnMode && _active != null;
            if (card.TurnDim != null) card.TurnDim.color = new Color(0f, 0f, 0f, turnBased && !active ? DimAlpha : 0f);
            if (card.TurnGlow != null)
            {
                card.TurnGlow.gameObject.SetActive(active);
                if (active)
                {
                    float a = turnBased ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f) : 0.6f; // 보드 파티장은 숨쉬기 없이 // 숨쉬듯 밝아졌다 어두워짐
                    foreach (var img in card.TurnGlow.GetComponentsInChildren<Image>()) img.color = new Color(TurnGold.r, TurnGold.g, TurnGold.b, a);
                }
            }
            card.Root.transform.localScale = Vector3.one * (turnBased && active ? ActiveScale : 1f);
        }
    }
}
