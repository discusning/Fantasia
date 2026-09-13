using System.Collections.Generic;
using Fantasia.Board;
using Fantasia.Items;
using UnityEngine;

namespace Fantasia.Core
{
    // Only decides QuestTrackerPanel's title color for now (gold=Main,
    // silver=Sub — the "tier" coloring convention common in MMO/mobile RPG
    // quest logs) and display order (Main pinned at top, Sub stacks below)
    // — no gameplay difference yet.
    public enum QuestType
    {
        Main,
        Sub,
    }

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

        // Fires on every successful AddItem regardless of source — UI (e.g.
        // ItemAcquiredToast) subscribes here instead of each pickup site
        // calling into UI directly.
        public event System.Action<ItemDefinition> ItemAdded;

        // Returns false if every slot is already full.
        public bool AddItem(ItemDefinition item)
        {
            if (item == null) return false;

            for (int i = 0; i < Inventory.Length; i++)
            {
                if (Inventory[i] == null)
                {
                    Inventory[i] = item;
                    ItemAdded?.Invoke(item);
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

        // Keyed by ItemDefinition rather than slot index so equip state
        // follows the item through a drag/swap instead of staying pinned to
        // a grid position. No per-pickup item identity yet, so two inventory
        // entries sharing the same ItemDefinition asset are indistinguishable
        // and will show as equipped together — acceptable until items get
        // real per-instance identity.
        public HashSet<ItemDefinition> EquippedItems { get; } = new HashSet<ItemDefinition>();

        // Returns true if the item ends up equipped, false if it was just unequipped.
        public bool ToggleEquipped(ItemDefinition item)
        {
            if (!EquippedItems.Add(item))
            {
                EquippedItems.Remove(item);
                return false;
            }
            return true;
        }

        // Real quests (GDD content, not yet designed) will replace this —
        // for now it only proves the right-side quest-tracker UI mechanism
        // works (including several active at once), seeded with throwaway
        // dummy quests (see QuestTrackerPanel).
        public readonly struct QuestEntry
        {
            public readonly string Title;
            public readonly string Objective;
            public readonly QuestType Kind;

            public QuestEntry(string title, string objective, QuestType kind)
            {
                Title = title;
                Objective = objective;
                Kind = kind;
            }
        }

        private readonly List<QuestEntry> _quests = new List<QuestEntry>();
        public IReadOnlyList<QuestEntry> Quests => _quests;

        public event System.Action QuestChanged;

        // Adds a new quest, or updates the existing one with the same title.
        public void SetQuest(string title, string objective, QuestType kind)
        {
            for (int i = 0; i < _quests.Count; i++)
            {
                if (_quests[i].Title == title)
                {
                    _quests[i] = new QuestEntry(title, objective, kind);
                    QuestChanged?.Invoke();
                    return;
                }
            }
            _quests.Add(new QuestEntry(title, objective, kind));
            QuestChanged?.Invoke();
        }

        // Fires after a quest is removed by CompleteQuest — DialoguePanel
        // subscribes to this instead of any specific completion site calling
        // into UI directly (same decoupling idea as ItemAdded/QuestChanged).
        public event System.Action<QuestEntry> QuestCompleted;

        // Returns false if no quest had that title.
        public bool CompleteQuest(string title)
        {
            for (int i = 0; i < _quests.Count; i++)
            {
                if (_quests[i].Title == title)
                {
                    var completed = _quests[i];
                    _quests.RemoveAt(i);
                    QuestChanged?.Invoke();
                    QuestCompleted?.Invoke(completed);
                    return true;
                }
            }
            return false;
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
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                // Uncapped frame rate on a scene full of 3D objects can pin a
                // low-end GPU and crash the editor. This is the one place
                // every scene's Start() reaches before anything else runs
                // (see BoardTestController/CombatTestController.Start(),
                // ItemAcquiredToast.Awake()), so cap it once here rather than
                // per-scene.
                Application.targetFrameRate = 60;

                // Dummy quests so the right-side tracker UI has something to
                // show before real quest content exists — see QuestTrackerPanel.
                // Both Main and Sub seeded together so the multi-quest
                // stacking layout gets exercised in real Play mode, not just
                // one type at a time.
                SetQuest("게임 완성시키기", "판타지아를 끝까지 만들어라", QuestType.Main);
                SetQuest("1차 개발 완성", "판타지아 프로토타입 1차 개발을 마무리하라", QuestType.Sub);
            }
            BoardSeed = Random.Range(int.MinValue, int.MaxValue);
        }
    }
}
