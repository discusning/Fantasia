using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/12 디자인에서 받은 메인 화면 그림: 배경(안개 낀 런던 골목) + 게임 로고.
    // 배경은 화면을 꽉 채움 (화면 비율이 달라도 빈 곳 없이, 넘치는 쪽은 잘림). 버튼 글자가 잘 보이게 위에 어두운 막.
    // 그림이 없으면 예전 그대로 (단색 바탕 · "판타지아" 글자).
    public partial class MainMenuController
    {
        private static void BuildMainArt(Transform bg)
        {
            var sprite = UITheme.Current != null ? UITheme.Current.MainBackground : null;
            if (sprite == null) return;
            var art = UGUIKit.CreateImage(bg, "MainArt", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Color.white);
            art.sprite = sprite;
            art.raycastTarget = false;
            var fitter = art.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; // 화면 비율과 달라도 꽉 채움
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            art.transform.SetAsFirstSibling();

            // 어두운 막: 전체 살짝 + 아래(제작진 글자 뒤). 가운데 띠는 끝이 딱딱해 보여 뺌 (11/12)
            Shade(bg, "Shade", Vector2.zero, Vector2.one, 0.28f);
            Shade(bg, "ShadeBottom", Vector2.zero, new Vector2(1f, 0.1f), 0.35f);
        }

        private static void Shade(Transform parent, string name, Vector2 min, Vector2 max, float alpha)
        {
            var img = UGUIKit.CreateImage(parent, name, min, max, new Color(0f, 0f, 0f, alpha));
            img.raycastTarget = false;
        }

        private void BuildTitle(Transform menu)
        {
            var logo = UITheme.Current != null ? UITheme.Current.MainLogo : null;
            if (logo == null)
            {
                Label(menu, "Title", new Vector2(0.2f, 0.66f), new Vector2(0.8f, 0.86f), 64, TextAnchor.MiddleCenter, Gold, true).text = "판타지아";
                return;
            }
            var img = UGUIKit.CreateImage(menu, "Title", new Vector2(0.22f, 0.62f), new Vector2(0.78f, 0.93f), Color.white);
            img.sprite = logo;
            img.preserveAspect = true; // 로고 비율 3:1 그대로
            img.raycastTarget = false;
        }
    }
}
