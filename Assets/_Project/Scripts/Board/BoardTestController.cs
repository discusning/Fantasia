using System.Collections;
using System.Collections.Generic;
using Fantasia.Core;
using Fantasia.Dice;
using Fantasia.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // Standalone play-mode test harness for the board: rolls a d6, highlights
    // the reachable tiles, and walks a placeholder token tile-by-tile along
    // the rolled path on click. No UI prefabs or art required — draws its
    // own status line with OnGUI.
    [RequireComponent(typeof(HexBoard))]
    public class BoardTestController : MonoBehaviour
    {
        private const string CombatSceneName = "CombatTest";

        [SerializeField] private float tokenRadius = 0.5f; // matches the default primitive Sphere's radius
        [SerializeField] private float secondsPerTile = 0.25f;

        private HexBoard _board;
        private Transform _token;
        private HexCoord _currentCoord;
        private HexBoard.ReachabilityMap _reachability;
        private int _lastRoll;
        private bool _awaitingSelection;
        private bool _isMoving;

        private void Start()
        {
            BoardSession.EnsureExists();
            ItemAcquiredToast.EnsureExists();
            _board = GetComponent<HexBoard>();
            _currentCoord = BoardSession.Instance.PlayerPosition;
            SpawnToken();
        }

        // Public so editor tooling can spawn it for a headless screenshot
        // check without entering Play mode (Start() doesn't run there).
        public void SpawnToken()
        {
            _board ??= GetComponent<HexBoard>();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Token";
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };

            _token = go.transform;
            _token.position = TokenSurfacePosition(_currentCoord);
        }

        // Tiles are positioned base-first (see HexBoard.TileHeight), so a
        // token resting on top needs the tile's height plus its own radius —
        // not a flat guessed offset that ends up burying it in the mesh.
        private Vector3 TokenSurfacePosition(HexCoord coord)
        {
            return _board.CoordToWorld(coord) + Vector3.up * (_board.TileHeight + tokenRadius);
        }

        private void Update()
        {
            if (_isMoving) return;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                RollAndHighlight();
            }

            if (_awaitingSelection && Input.GetMouseButtonDown(0))
            {
                TrySelectTile();
            }
        }

        private void RollAndHighlight()
        {
            _lastRoll = DiceRoller.Roll(6);
            _reachability = _board.ComputeReachability(_currentCoord, _lastRoll);

            _board.ClearHighlights();
            foreach (var coord in _reachability.ReachableTiles)
            {
                if (_board.TryGetTile(coord, out var tile))
                {
                    tile.SetReachable(true);
                }
            }

            _awaitingSelection = true;
        }

        private void TrySelectTile()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit)) return;

            var tile = hit.collider.GetComponent<HexTile>();
            if (tile == null || !_reachability.Contains(tile.Coordinate)) return;

            var path = _reachability.GetPathTo(tile.Coordinate);
            _board.ClearHighlights();
            _awaitingSelection = false;
            StartCoroutine(WalkPath(path));
        }

        private IEnumerator WalkPath(List<HexCoord> path)
        {
            _isMoving = true;

            foreach (var coord in path)
            {
                var start = _token.position;
                var end = TokenSurfacePosition(coord);

                for (float t = 0f; t < secondsPerTile; t += Time.deltaTime)
                {
                    _token.position = Vector3.Lerp(start, end, t / secondsPerTile);
                    yield return null;
                }
                _token.position = end;
                _currentCoord = coord;
            }

            _isMoving = false;
            BoardSession.Instance.PlayerPosition = _currentCoord;

            // Landing on an encounter tile — not just passing through it —
            // is what starts a fight, matching how the highlight/click flow
            // already treats the destination as the meaningful stop. Cleared
            // ones don't re-trigger.
            if (_board.TryGetTile(_currentCoord, out var landedTile) && landedTile.IsEncounter && !landedTile.IsCleared)
            {
                BoardSession.Instance.PendingEncounterCoord = _currentCoord;
                SceneManager.LoadScene(CombatSceneName);
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 300, 24), $"주사위: {_lastRoll}  (Space로 굴리기)");
            if (_awaitingSelection)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "초록 타일을 클릭해서 이동 (주황 = 인카운터)");
            }
            else if (_isMoving)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "이동 중...");
            }
        }
    }
}
