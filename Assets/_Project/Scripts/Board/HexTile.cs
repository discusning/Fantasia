using Fantasia.Data;
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
        [SerializeField] private Color eventColor = new Color(0.55f, 0.35f, 0.85f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _renderer;
        private MaterialPropertyBlock _props;
        private bool _isReachable;

        public HexCoord Coordinate { get; private set; }
        public bool IsBlocked { get; private set; }
        public bool IsEncounter { get; private set; }
        public bool IsCleared { get; private set; }
        // 전투를 거는 IsEncounter와 별개 — 기관차/점술마녀/십자가 석상 같은 월드 이벤트 칸
        // (System.md "이벤트 칸"). 1차 구현에서는 종류 구분 없이 "도착 시 추가 이동 0/1/2"
        // 보너스만 공통으로 적용한다. 실제 이벤트 종류별 처리는 D23에서 확장한다.
        public bool IsEventTile { get; private set; }

        // 이 칸의 인카운터가 어떤 적인지 (D08 조우 판정에 슬롯 수·판정 확률이 필요해서 추가).
        // 실제 소형 4종 등 콘텐츠 채우기는 D13에서 진행 — 지금은 null 허용.
        public EnemyDefinition Enemy { get; private set; }
        public LandmarkDefinition Landmark { get; private set; }
        public bool IsLandmark => Landmark != null;

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

        public void SetEncounter(bool encounter, EnemyDefinition enemy = null)
        {
            IsEncounter = encounter;
            Enemy = encounter ? enemy : null;
            RefreshColor();
        }

        public void SetCleared(bool cleared)
        {
            IsCleared = cleared;
            RefreshColor();
        }

        public void SetEventTile(bool isEvent)
        {
            IsEventTile = isEvent;
            RefreshColor();
        }

        public void SetReachable(bool reachable)
        {
            _isReachable = reachable;
            RefreshColor();
        }

        // Ctrl+Right-click reads this off the raycast hit (see
        // BoardTestController.TryInspectLandmark) — no per-type tile prefab
        // yet, so the accent color IS the tile's visual identity for now.
        public void SetLandmark(LandmarkDefinition landmark)
        {
            Landmark = landmark;
            RefreshColor();
        }

        // Encounter/cleared/landmark markers stay visible even while
        // highlighted reachable — they're points of interest, not something
        // a highlight overlay should hide.
        // KNOWN GAP (2026-10-21, 사용자 피드백): 이 우선순위 때문에 인카운터/이벤트/
        // 지형지물 타일은 "이동 가능(초록)" 신호를 아예 못 보여준다. 색 하나로 둘 다
        // 표현하지 말고, 테두리 링 같은 별도 하이라이트 레이어로 분리하는 방향 검토할 것.
        private void RefreshColor()
        {
            Color color = IsBlocked ? blockedColor
                : IsCleared ? clearedColor
                : IsEncounter ? encounterColor
                : IsLandmark ? Landmark.AccentColor
                : IsEventTile ? eventColor
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
