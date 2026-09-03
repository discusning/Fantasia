using System.Collections.Generic;
using Fantasia.Board;
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
