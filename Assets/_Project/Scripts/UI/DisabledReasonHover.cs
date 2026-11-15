using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Fantasia.UI
{
    // 11/15: 못 쓰는 버튼에 마우스를 올리거나 누르면 이유를 안내 줄에 (쓸 수 있으면 아무 일 없음)
    public class DisabledReasonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Func<string> _reason;
        private Action<string> _show;
        private bool _shown;

        public void Init(Func<string> reason, Action<string> show) { _reason = reason; _show = show; }

        public void OnPointerEnter(PointerEventData e) => Show();
        public void OnPointerClick(PointerEventData e) { Show(); if (_shown) Fantasia.Core.SfxPlayer.Play(Fantasia.Core.SfxIds.UiClose); }
        public void OnPointerExit(PointerEventData e) { if (_shown) { _shown = false; _show?.Invoke(null); } }
        private void OnDisable() { if (_shown) { _shown = false; _show?.Invoke(null); } }

        private void Show()
        {
            var r = _reason?.Invoke();
            if (string.IsNullOrEmpty(r)) return;
            _shown = true;
            _show?.Invoke(r);
        }
    }
}
