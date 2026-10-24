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
        // 월드맵 칸 종류 (Docs/Planning/WorldMap.md 범례 색과 비슷하게)
        [SerializeField] private Color hubColor = new Color(0.25f, 0.5f, 0.9f);
        [SerializeField] private Color bossColor = new Color(0.55f, 0.15f, 0.4f);
        [SerializeField] private Color recoveryColor = new Color(0.2f, 0.65f, 0.4f);
        [SerializeField] private Color treasureColor = new Color(0.85f, 0.65f, 0.2f);
        [SerializeField] private Color exitLockedColor = new Color(0.3f, 0.18f, 0.32f);
        [SerializeField] private Color exitOpenColor = new Color(0.7f, 0.55f, 0.75f);
        [SerializeField] private Color usedColor = new Color(0.42f, 0.4f, 0.38f);

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
        // 월드맵 칸 종류 (랜덤 보드면 Walkable). 이름은 지점 표시용 (예: "S05 그리니치 거점").
        public MapTileKind MapKind { get; private set; } = MapTileKind.Walkable;
        public string MapLabel { get; private set; }
        public bool IsBoss => MapKind == MapTileKind.Boss;
        public bool IsUsed { get; private set; } // 회복·보물 칸을 이미 씀

        public void SetMapKind(MapTileKind kind, string label)
        {
            MapKind = kind;
            MapLabel = label;
            RefreshColor();
        }

        public void SetUsed(bool used)
        {
            IsUsed = used;
            RefreshColor();
        }
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
        // 이동 가능 칸이 특수 칸이면 그 색과 초록을 섞어 둘 다 보이게 한다 (2026-10-21 피드백 반영).
        // 나중에 아트가 들어오면 테두리 링 같은 별도 하이라이트 레이어로 바꾸면 된다.
        private void RefreshColor()
        {
            Color color = MapKind == MapTileKind.Exit ? (IsBlocked ? exitLockedColor : (_isReachable ? reachableColor : exitOpenColor))
                : IsBlocked ? blockedColor
                : IsUsed ? usedColor
                : MapKind == MapTileKind.Hub ? hubColor
                : IsBoss && !IsCleared ? bossColor
                : MapKind == MapTileKind.Recovery ? recoveryColor
                : MapKind == MapTileKind.Treasure ? treasureColor
                : IsCleared ? clearedColor
                : IsEncounter ? encounterColor
                : IsLandmark ? Landmark.AccentColor
                : IsEventTile ? eventColor
                : _isReachable ? reachableColor
                : baseColor;
            if (_isReachable && !IsBlocked && color != reachableColor) color = Color.Lerp(color, reachableColor, 0.55f);
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
