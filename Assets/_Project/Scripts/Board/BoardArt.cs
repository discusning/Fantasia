using UnityEngine;

namespace Fantasia.Board
{
    // 받은 보드 3D 에셋 (Resources/BoardArt — Editor/ArtSetup이 크기를 맞춰 만든다).
    // 파일이 없으면 모두 null이고 보드는 예전처럼 색 칸 + 납작한 표식으로 그린다.
    public sealed class BoardArt
    {
        public GameObject TileA, TileB, Hub, Boss, BlockA, BlockB, BlockTrees, Treasure, Recovery, FortuneTeller, Fountain, Exit;
        public bool HasTiles => TileA != null;

        public static BoardArt Load()
        {
            GameObject L(string n) => Resources.Load<GameObject>("BoardArt/" + n);
            return new BoardArt
            {
                TileA = L("Tile_Road_A"), TileB = L("Tile_Road_B") ?? L("Tile_Road_A"),
                Hub = L("Hub"), Boss = L("Boss"),
                BlockA = L("Block_A"), BlockB = L("Block_B"), BlockTrees = L("Block_Trees"),
                Treasure = L("Treasure"), Recovery = L("Recovery"),
                FortuneTeller = L("Event_FortuneTeller"), Fountain = L("Event_Fountain"), Exit = L("Exit"),
            };
        }

        // 칸 종류별 위에 올릴 구조물. 일반 적 칸은 표식 그대로 (구조물 없음).
        public GameObject PropFor(MapTile t)
        {
            switch (t.Kind)
            {
                case MapTileKind.Hub: return Hub;
                case MapTileKind.Boss: return Boss;
                case MapTileKind.Treasure: return Treasure;
                case MapTileKind.Recovery: return Recovery;
                case MapTileKind.Exit: return Exit;
                case MapTileKind.Event:
                    var kind = Fantasia.World.EventTiles.KindAt(t.Label);
                    return kind == Fantasia.World.EventKind.FortuneTeller ? FortuneTeller
                        : kind == Fantasia.World.EventKind.Fountain ? Fountain : null;
                case MapTileKind.Blocked:
                    // 좌표로 고정 추첨 — 다시 불러와도 같은 모습. 나무는 면 수가 많아 6칸 중 1칸만.
                    int h = ((t.Row * 73 + t.Col * 151) % 12 + 12) % 12;
                    if (h < 2 && BlockTrees != null) return BlockTrees;
                    return (h % 2 == 0 ? BlockA : BlockB) ?? BlockA;
                default: return null;
            }
        }
    }
}
