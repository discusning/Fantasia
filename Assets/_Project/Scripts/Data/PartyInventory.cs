using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Data
{
    // 파티 공용 인벤토리 (System.md "소모품·이동권·재료": 일반 소모품 종류당 10개·시약 30개, 일반 장비 20개).
    // 꽉 찼을 때 새 전리품은 자동 삭제하지 않는다 — 넣지 못한 것은 호출부(전리품 창)가 버리기·교체를 묻는다.
    public class PartyInventory
    {
        public const int MaxGear = 20;

        public class Stack
        {
            public ConsumableDefinition Definition;
            public int Count;
        }

        private readonly List<Stack> _consumables = new();
        private readonly List<GearItem> _gear = new();

        public IReadOnlyList<Stack> Consumables => _consumables;
        public IReadOnlyList<GearItem> Gear => _gear;
        public bool IsGearFull => _gear.Count >= MaxGear;

        // 내용이 바뀔 때마다 (UI 갱신용)
        public event Action Changed;

        public int CountOf(ConsumableDefinition definition) => Find(definition)?.Count ?? 0;

        public int SpaceFor(ConsumableDefinition definition) =>
            definition == null ? 0 : Math.Max(0, definition.MaxStack - CountOf(definition));

        // 넣은 개수를 돌려준다 (종류당 상한을 넘는 만큼은 못 넣음)
        public int AddConsumable(ConsumableDefinition definition, int count = 1)
        {
            int added = Math.Min(count, SpaceFor(definition));
            if (added <= 0) return 0;
            var stack = Find(definition);
            if (stack == null) _consumables.Add(stack = new Stack { Definition = definition });
            stack.Count += added;
            Changed?.Invoke();
            return added;
        }

        public bool RemoveConsumable(ConsumableDefinition definition, int count = 1)
        {
            var stack = Find(definition);
            if (stack == null || stack.Count < count) return false;
            stack.Count -= count;
            if (stack.Count == 0) _consumables.Remove(stack);
            Changed?.Invoke();
            return true;
        }

        public bool AddGear(GearItem item)
        {
            if (item == null || IsGearFull) return false;
            _gear.Add(item);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveGear(GearItem item)
        {
            if (!_gear.Remove(item)) return false;
            Changed?.Invoke();
            return true;
        }

        // 장착 해제·교체처럼 넣고 빼기를 한 번에 할 때 이벤트 한 번만
        internal void SwapGear(GearItem remove, GearItem add)
        {
            if (remove != null) _gear.Remove(remove);
            if (add != null) _gear.Add(add);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _consumables.Clear();
            _gear.Clear();
            Changed?.Invoke();
        }

        private Stack Find(ConsumableDefinition definition) => _consumables.FirstOrDefault(s => s.Definition == definition);
    }
}
