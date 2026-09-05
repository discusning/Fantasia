using System.Collections.Generic;
using Fantasia.Board;
using Fantasia.Items;
using UnityEngine;

namespace Fantasia.Core
{
    // Persists across the board <-> combat scene round trip (DevSceneNav /
    // real encounter triggers) so player position and cleared-encounter
    // progress survive it. The board itself still regenerates fresh on every
    // scene load, but from this same seed, so each coordinate's
    // blocked/encounter roll comes out identical every time.
    public class BoardSession : MonoBehaviour
    {
        public static BoardSession Instance { get; private set; }

        public int BoardSeed { get; private set; }
        public HexCoord PlayerPosition { get; set; }
        public HashSet<HexCoord> ClearedEncounters { get; } = new HashSet<HexCoord>();
        public HexCoord? PendingEncounterCoord { get; set; }

        // Fixed slot array (not a growing list) so a specific grid position
        // can be targeted directly — StatusInventoryPanel's drag/drop needs
        // "move to slot index" and "swap these two slots", which a compact
        // list can't express once there's a gap. Any source (combat loot,
        // events, camp, ...) can feed this through AddItem — it isn't
        // combat-specific.
        public const int InventoryCapacity = 12;
        public ItemDefinition[] Inventory { get; } = new ItemDefinition[InventoryCapacity];

        // Returns false if every slot is already full.
        public bool AddItem(ItemDefinition item)
        {
            if (item == null) return false;

            for (int i = 0; i < Inventory.Length; i++)
            {
                if (Inventory[i] == null)
                {
                    Inventory[i] = item;
                    return true;
                }
            }
            return false;
        }

        public void RemoveItemAt(int index)
        {
            if (index >= 0 && index < Inventory.Length) Inventory[index] = null;
        }

        // Swapping into a null slot is how an item moves to an empty slot —
        // no separate "move" method needed.
        public void SwapItems(int indexA, int indexB)
        {
            if (indexA == indexB) return;
            if (indexA < 0 || indexA >= Inventory.Length) return;
            if (indexB < 0 || indexB >= Inventory.Length) return;

            (Inventory[indexA], Inventory[indexB]) = (Inventory[indexB], Inventory[indexA]);
        }

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("BoardSession").AddComponent<BoardSession>().Initialize();
        }

        private void Awake() => Initialize();

        // Idempotent, like HexBoard.Generate() — AddComponent reliably fires
        // Awake in Play mode, but not always from editor tooling, so
        // EnsureExists() also calls this directly rather than trusting Awake.
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            // Only legal in Play mode — editor tooling (self-tests, scene
            // scaffolding) that calls this outside Play mode skips it, since
            // there's no scene-load lifecycle to persist across there anyway.
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            BoardSeed = Random.Range(int.MinValue, int.MaxValue);
        }
    }
}
