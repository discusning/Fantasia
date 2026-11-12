using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/11 받은 체력·마나 바 틀 (N21_체력마나_바, 2172×175) — 팀장 결정: HP·MP 바를 이 틀로 따로 2개.
    // 그림 구조(픽셀): 왼쪽 네모 칸 x62~185 · y37~138, 나무판(채움 자리) x196~2128 · y30~145, 오른쪽 끝 장식 x2128~2172.
    // 틀은 좌우로만 늘어남 (9-slice 왼 200 · 오른 50). 높이가 바뀌어도 모서리 비율이 맞게 크기에 따라 다시 계산.
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class FramedBar : MonoBehaviour
    {
        public const float ArtHeight = 175f;
        public Image Frame;         // 틀 (이 오브젝트)
        public RectTransform Plank; // 채움이 들어가는 나무판 자리 — 그 안의 Fill은 anchorMax.x로 비율
        public RectTransform Slot;  // 왼쪽 네모 칸 (아이콘·"MP" 글자)
        public Image FillImage;     // 둥근 채움 (Z39) — 높이에 맞춰 둥근 끝 크기 맞춤

        private void OnEnable() => Layout();
        private void OnRectTransformDimensionsChange() => Layout();

        public void Layout()
        {
            float h = ((RectTransform)transform).rect.height;
            if (h <= 0.5f) return;
            float k = h / ArtHeight; // 그림 1px → 화면 단위
            if (Frame != null) Frame.pixelsPerUnitMultiplier = 1f / k; // 9-slice 경계(그림 px)를 높이 비율대로
            if (Plank != null)
            {
                Plank.anchorMin = Vector2.zero;
                Plank.anchorMax = Vector2.one;
                Plank.offsetMin = new Vector2(196f * k, 32f * k);
                Plank.offsetMax = new Vector2(-46f * k, -32f * k);
            }
            if (Slot != null)
            {
                Slot.anchorMin = new Vector2(0f, 0f);
                Slot.anchorMax = new Vector2(0f, 1f);
                Slot.offsetMin = new Vector2(66f * k, 41f * k);
                Slot.offsetMax = new Vector2(181f * k, -41f * k);
            }
            if (FillImage != null && FillImage.sprite != null && FillImage.type == Image.Type.Sliced)
                FillImage.pixelsPerUnitMultiplier = FillImage.sprite.rect.height / Mathf.Max(1f, (ArtHeight - 64f) * k);
        }

        // 틀 + 나무판 + 채움 + 왼쪽 칸을 만든다. 틀 그림이 없으면 null (예전 바 그대로 씀).
        public static FramedBar Create(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color fillColor, out RectTransform fill)
        {
            fill = null;
            var theme = UITheme.Current;
            if (theme == null || theme.HpMpFrame == null) return null;
            var frame = UGUIKit.CreateImage(parent, name, anchorMin, anchorMax, Color.white);
            frame.sprite = theme.HpMpFrame;
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            var bar = frame.gameObject.AddComponent<FramedBar>();
            bar.Frame = frame;
            bar.Plank = UGUIKit.CreateRect(frame.transform, "Plank", Vector2.zero, Vector2.one);
            var fillImage = UGUIKit.CreateImage(bar.Plank, "Fill", Vector2.zero, Vector2.one, fillColor);
            UITheme.Apply(fillImage, theme.BarFill, tint: true);
            fillImage.raycastTarget = false;
            bar.FillImage = fillImage;
            bar.Slot = UGUIKit.CreateRect(frame.transform, "Slot", Vector2.zero, Vector2.one);
            fill = (RectTransform)fillImage.transform;
            bar.Layout();
            return bar;
        }
    }
}
