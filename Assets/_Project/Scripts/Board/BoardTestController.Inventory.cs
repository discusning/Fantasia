using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // 인벤토리·퀵 슬롯·야영 텐트 (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // ── D17 인벤토리: 초상 클릭·I 키로 창, 퀵 슬롯 클릭으로 월드 사용 ──
        private bool WorldItemUseAllowed => !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && Clock.Phase == WorldPhase.AllyTurn;

        private void HookInventory()
        {
            InventoryPanel.WorldUseAllowed = () => WorldItemUseAllowed;
            InventoryPanel.AllyTurnEndRequested += OnItemEndedAllyTurn;
            InventoryPanel.AllyTurnEndRequested += PitCampTent; // 11/14 기획 Q4: 야영 성공 → 텐트 (가방에서 턴을 끝내는 건 야영뿐)
            Fantasia.UI.ShopPanel.AllyTurnEndRequested += OnItemEndedAllyTurn;
            PartyHudPanel.CardClicked += OnCardClicked;
            PartyHudPanel.QuickSlotClicked += OnBoardQuickSlot;
        }

        private void OnDestroy()
        {
            InventoryPanel.AllyTurnEndRequested -= OnItemEndedAllyTurn;
            InventoryPanel.AllyTurnEndRequested -= PitCampTent;
            Fantasia.UI.ShopPanel.AllyTurnEndRequested -= OnItemEndedAllyTurn;
            PartyHudPanel.CardClicked -= OnCardClicked;
            PartyHudPanel.QuickSlotClicked -= OnBoardQuickSlot;
        }

        private void OnCardClicked(PartyMember member)
        {
            if (!EncounterPanel.IsOpen) InventoryPanel.Open(_testParty, member);
        }

        private void OnBoardQuickSlot(PartyMember member, int slot)
        {
            var def = member.QuickSlots[slot];
            if (def == null) return;
            string message = WorldItemUseAllowed ? InventoryPanel.UseWorldItem(def, member) : "보드의 아군 턴(이동 중 아님)에만 쓸 수 있음";
            ItemAcquiredToast.Show(message);
        }

        // 11/14 기획 회신 Q4: 안개 야영 세트 성공 → 지금 칸에 텐트·모닥불 (야영 성공을 보여 주는 연출, 규칙 영향 없음).
        // 다음 보드 이동이 시작될 때 치움. 말은 칸 앞쪽에 서 있으므로 텐트는 칸 뒤쪽(카메라 반대)에 세워 말을 가리지 않게.
        private GameObject _campTent;
        public static int DevTentsPitched { get; private set; }
        public bool DevTentVisible => _campTent != null;
        public void DevPitCampTent() => PitCampTent();
        public void DevEndTurn() => OnItemEndedAllyTurn(); // 자동 점검: 아군 턴 끝내기 (11/15 숙박이 턴을 안 끝내게 된 뒤 저장 점검용)
        private void PitCampTent()
        {
            ClearCampTent();
            var prefab = Resources.Load<GameObject>("BoardArt/CampTent");
            if (prefab == null || _board == null) return;
            // 11/14 팀장 "텐트가 너무 작음, 다른 구조물 정도" → 밟는 칸 구조물 최대 폭(1.6m)에 맞춤 (텐트 묶음 폭 약 1.45m 기준)
            float s = HexTile.WalkablePropMaxWidth / 1.45f;
            // 11/14 QA: 이동 시점은 카메라가 진행 방향으로 돎 → "칸 뒤쪽"은 고정 +z가 아니라 카메라에서 본 뒤쪽. 입구는 카메라를 봄
            var cam = Camera.main;
            var away = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            var side = Vector3.Cross(Vector3.up, away);
            var pos = _token.position + away * (0.5f * s + 0.25f) - side * 0.1f; // 텐트 앞면이 말 바로 뒤
            _campTent = Instantiate(prefab, pos, Quaternion.LookRotation(away));
            _campTent.transform.localScale = Vector3.one * s;
            _campTent.name = "CampTent";
            DevTentsPitched++;
        }
        private void ClearCampTent()
        {
            if (_campTent != null) Destroy(_campTent);
            _campTent = null;
        }

        // 안개 야영 세트: 사용 후 1월드 라운드 경과 → 아군 턴 종료
        private void OnItemEndedAllyTurn()
        {
            if (!_turnEnding && Clock.Phase == WorldPhase.AllyTurn) StartCoroutine(EndAllyTurn());
        }
    }
}
