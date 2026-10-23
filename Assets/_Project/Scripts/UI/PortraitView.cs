using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 캐릭터 초상 한 칸. 기본은 3D 모델 렌더(PortraitStage가 찍은 RenderTexture)를 보여주고,
    // 2D 초상 그림(Sprite)이 있으면 그걸 우선한다 — 데이터의 Model/Portrait 칸만 채우면
    // 코드 수정 없이 바뀐다. 파티 HUD에서 먼저 쓰고, 이후 전투 타임라인 등에서도 재사용한다.
    public class PortraitView
    {
        private static readonly Color HighlightBorder = new Color(0.95f, 0.78f, 0.4f);
        private static readonly Color EmptyFill = new Color(0.13f, 0.12f, 0.15f);

        public RectTransform Root { get; }
        private readonly Image _border;
        private readonly RawImage _modelView;
        private readonly Image _art;
        private readonly Color _normalBorder;

        public PortraitView(Transform parent, string name, Vector2 anchor, float size, Color borderColor)
        {
            var fill = UGUIKit.CreateBorderedPanel(parent, name, anchor, anchor, borderColor, EmptyFill, 3f);
            Root = (RectTransform)fill.parent;
            Root.sizeDelta = new Vector2(size, size);
            _border = Root.GetComponent<Image>();
            UITheme.Apply(_border, UITheme.Current.PortraitFrame, tint: true);
            _normalBorder = borderColor;

            var modelRect = UGUIKit.CreateRect(fill, "Model", Vector2.zero, Vector2.one);
            _modelView = modelRect.gameObject.AddComponent<RawImage>();
            _modelView.enabled = false;

            _art = UGUIKit.CreateImage(fill, "Art", Vector2.zero, Vector2.one, Color.white);
            _art.preserveAspect = true;
            _art.enabled = false;
        }

        // art: 2D 초상(있으면 우선), model: 3D 렌더 텍스처. 둘 다 없으면 빈 칸.
        public void Set(Sprite art, Texture model)
        {
            bool hasArt = art != null;
            _art.sprite = art;
            _art.enabled = hasArt;
            _modelView.texture = model;
            _modelView.enabled = !hasArt && model != null;
        }

        // 현재 판정·행동 중인 캐릭터 강조용.
        public void SetHighlighted(bool highlighted) => _border.color = highlighted ? HighlightBorder : _normalBorder;

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);
    }
}
