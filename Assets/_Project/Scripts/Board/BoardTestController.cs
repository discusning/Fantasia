using System.Collections;
using System.Collections.Generic;
using Fantasia.Dice;
using UnityEngine;

namespace Fantasia.Board
{
    // Standalone play-mode test harness for the board: rolls a d6, highlights
    // the reachable tiles, and walks a placeholder token tile-by-tile along
    // the rolled path on click. No UI prefabs or art required — draws its
    // own status line with OnGUI.
    [RequireComponent(typeof(HexBoard))]
    public class BoardTestController : MonoBehaviour
    {
        [SerializeField] private HexCoord startCoord = new HexCoord(0, 0);
        [SerializeField] private float tokenHeight = 0.4f;
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
            _board = GetComponent<HexBoard>();
            _currentCoord = startCoord;
            SpawnToken();
        }

        private void SpawnToken()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Token";
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };

            _token = go.transform;
            _token.position = _board.CoordToWorld(_currentCoord) + Vector3.up * tokenHeight;
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
                var end = _board.CoordToWorld(coord) + Vector3.up * tokenHeight;

                for (float t = 0f; t < secondsPerTile; t += Time.deltaTime)
                {
                    _token.position = Vector3.Lerp(start, end, t / secondsPerTile);
                    yield return null;
                }
                _token.position = end;
                _currentCoord = coord;
            }

            _isMoving = false;
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 300, 24), $"주사위: {_lastRoll}  (Space로 굴리기)");
            if (_awaitingSelection)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "초록 타일을 클릭해서 이동");
            }
            else if (_isMoving)
            {
                GUI.Label(new Rect(10, 34, 400, 24), "이동 중...");
            }
        }
    }
}
