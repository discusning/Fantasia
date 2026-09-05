using UnityEngine;
using UnityEngine.EventSystems;

namespace Fantasia.UI
{
    // Per-slot pointer handling for StatusInventoryPanel's inventory grid.
    // Pure event forwarding — the panel owns all inventory state and
    // interaction rules (swap/discard/use/equip).
    public class InventorySlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public StatusInventoryPanel Panel;
        public int Index;

        public void OnBeginDrag(PointerEventData eventData) => Panel.BeginDragSlot(Index, eventData);
        public void OnDrag(PointerEventData eventData) => Panel.DragSlot(eventData);
        public void OnEndDrag(PointerEventData eventData) => Panel.EndDragSlot(eventData);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                Panel.TryEquipSlot(Index);
            }
            else if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2)
            {
                Panel.UseSlot(Index);
            }
        }
    }
}
