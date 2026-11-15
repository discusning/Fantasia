namespace Fantasia.UI
{
    // 11/11 팀장 결정 — 여러 UI가 한꺼번에 뜰 때 순서 규칙.
    // 규칙 1: 알림은 줄을 서서 하나씩 (덮어쓰기 없음) — ItemAcquiredToast.
    // 규칙 2: 대화창 → 창(이벤트·상점 등) → 턴 판 → 알림. 위쪽이 떠 있는 동안 아래쪽은 기다림.
    public static class UIFlow
    {
        // 대화창·창이 하나라도 떠 있음 (턴 판·알림이 기다림)
        public static bool WindowOpen =>
            DialoguePanel.IsOpen || EventPanel.IsOpen || EncounterPanel.IsOpen || ShopPanel.IsOpen || InventoryPanel.IsOpen
            || LandmarkInfoPanel.IsOpen || ConfirmPanel.IsOpen || CaseLogPanel.IsOpen || ActClearPanel.IsOpen
            || PauseMenu.IsOpen || SettingsPanel.IsOpen || BattleHud.ResultOpen || TreasurePanel.IsOpen || NotebookPanel.IsOpen;

        // 알림이 기다려야 함 (창 + 턴 판)
        public static bool ToastBlocked => WindowOpen || BoardTopBar.DevTurnDropVisible;
        // ③ 보이지 않는 UI(투명 판·글자 칸)가 마우스를 막지 않게: 실제로 눌리는 UI(버튼·카드 등 Selectable, 스크롤)만 막는 것으로 침.
        private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _uiHits = new();
        public static bool PointerBlockedByUI()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var data = new UnityEngine.EventSystems.PointerEventData(es) { position = UnityEngine.Input.mousePosition };
            _uiHits.Clear();
            es.RaycastAll(data, _uiHits);
            foreach (var r in _uiHits)
            {
                if (r.gameObject == null) continue;
                var cg = r.gameObject.GetComponentInParent<UnityEngine.CanvasGroup>();
                if (cg != null && (!cg.blocksRaycasts || cg.alpha < 0.05f)) continue;
                if (r.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
                if (r.gameObject.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null) return true;
                var img = r.gameObject.GetComponent<UnityEngine.UI.Graphic>();
                if (img != null && img.color.a > 0.5f && !(img is UnityEngine.UI.Text)) return true; // 불투명한 판(카드·창 배경)은 막음
            }
            return false;
        }
    }
}
