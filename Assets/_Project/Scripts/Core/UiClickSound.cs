using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.Core
{
    // 11/13 효과음 점검: 버튼 86곳 중 2곳만 클릭 소리(UI_Click)를 냈음 → 버튼마다 고치지 않고 여기 한 곳에서.
    // 마우스를 누른 버튼과 뗀 버튼이 같고, 그 버튼이 켜져 있으면 클릭 소리. (이미 직접 부르는 곳과 겹쳐도 SfxPlayer가 0.05초 안 중복은 무시)
    public class UiClickSound : MonoBehaviour
    {
        private static readonly List<RaycastResult> Hits = new();
        private Selectable _pressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("UiClickSound");
            DontDestroyOnLoad(go);
            go.AddComponent<UiClickSound>();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0)) _pressed = UnderPointer();
            else if (Input.GetMouseButtonUp(0))
            {
                var up = UnderPointer();
                if (up != null && up == _pressed && up.IsInteractable() && up.isActiveAndEnabled) SfxPlayer.Click();
                _pressed = null;
            }
        }

        // 마우스 아래 맨 위 UI에서 버튼·토글 찾기
        private static Selectable UnderPointer()
        {
            var es = EventSystem.current;
            if (es == null) return null;
            Hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, Hits);
            if (Hits.Count == 0) return null;
            var s = Hits[0].gameObject.GetComponentInParent<Selectable>();
            return s is Button || s is Toggle ? s : null;
        }
    }
}
