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
    }
}
