using UnityEngine;

namespace Fantasia.Board
{
    [RequireComponent(typeof(Renderer))]
    public class HexTile : MonoBehaviour
    {
        [SerializeField] private Color baseColor = new Color(0.55f, 0.5f, 0.45f);
        [SerializeField] private Color reachableColor = new Color(0.35f, 0.85f, 0.45f);
        [SerializeField] private Color blockedColor = new Color(0.12f, 0.12f, 0.12f);
        [SerializeField] private Color encounterColor = new Color(0.85f, 0.45f, 0.15f);
        [SerializeField] private Color clearedColor = new Color(0.45f, 0.65f, 0.55f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _renderer;
        private MaterialPropertyBlock _props;
        private bool _isReachable;

        public HexCoord Coordinate { get; private set; }
        public bool IsBlocked { get; private set; }
        public bool IsEncounter { get; private set; }
        public bool IsCleared { get; private set; }

        // Set up directly from Initialize rather than Awake — HexBoard always
        // calls this right after AddComponent, and Awake isn't guaranteed to
        // have run yet when tiles are generated outside Play mode (editor tooling).
        public void Initialize(HexCoord coordinate)
        {
            Coordinate = coordinate;
            _renderer = GetComponent<Renderer>();
            _props = new MaterialPropertyBlock();
            RefreshColor();
        }

        public void SetBlocked(bool blocked)
        {
            IsBlocked = blocked;
            RefreshColor();
        }

        public void SetEncounter(bool encounter)
        {
            IsEncounter = encounter;
            RefreshColor();
        }

        public void SetCleared(bool cleared)
        {
            IsCleared = cleared;
            RefreshColor();
        }

        public void SetReachable(bool reachable)
        {
            _isReachable = reachable;
            RefreshColor();
        }

        // Encounter/cleared markers stay visible even while highlighted
        // reachable — they're landmarks, not something a highlight overlay
        // should hide.
        private void RefreshColor()
        {
            Color color = IsBlocked ? blockedColor
                : IsCleared ? clearedColor
                : IsEncounter ? encounterColor
                : _isReachable ? reachableColor
                : baseColor;
            ApplyColor(color);
        }

        private void ApplyColor(Color color)
        {
            _renderer.GetPropertyBlock(_props);
            _props.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_props);
        }
    }
}
