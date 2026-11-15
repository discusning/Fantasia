using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // 테스트 파티·시작 위치·랜드마크·말(파티장 모델) (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // Public so editor tooling can build the HUD for a headless screenshot (Start() doesn't run there).
        // 파티는 BoardSession에 두어 전투를 다녀와도 HP·집중력이 유지된다.
        public void BuildTestParty()
        {
            BoardSession.EnsureExists();
            if (BoardSession.Instance.Party != null && BoardSession.Instance.Party.Members.Count > 0)
            {
                _testParty = BoardSession.Instance.Party;
                PartyHudPanel.Show(_testParty, Leader);
                return;
            }

            _testParty = new Party();
            BoardSession.Instance.Party = _testParty;
            var jobs = TestPartyJobs.Length > 0 ? TestPartyJobs : LoadDefaultTestPartyJobs();
            foreach (var job in jobs)
            {
                if (job != null && _testParty.Members.Count < Party.MaxSize) _testParty.AddMember(new PartyMember(job));
            }
            if (_testParty.Members.Count == 0) return;
            _testParty.GiveStartingItems(ItemCatalog.Current);
            PartyHudPanel.Show(_testParty, Leader);
        }

        // 씬이 Setup Board Test Scene 재실행 전 버전이라 TestPartyJobs가 비어 있어도 3인이 나오게 —
        // 에디터에서만 시드된 직업 자산을 직접 읽는다 (빌드에서는 명탐정 1명으로 폴백).
        private JobDefinition[] LoadDefaultTestPartyJobs()
        {
#if UNITY_EDITOR
            var loaded = new List<JobDefinition>();
            foreach (var name in new[] { "Job_명탐정", "Job_역병의사", "Job_저격수" })
            {
                var job = UnityEditor.AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                if (job != null) loaded.Add(job);
            }
            if (loaded.Count > 0) return loaded.ToArray();
#endif
            return new[] { TestHeroJob };
        }

        // 월드맵 처음 시작: 런던 거점 S05 (WorldMap.md)
        private void PlaceAtStartIfNew()
        {
            BoardSession.EnsureExists();
            var session = BoardSession.Instance;
            if (!_board.UsesWorldMap || session.HasPlayerPosition) return;
            session.PlayerPosition = _board.Map.TileAt(StartHubLabel).Coord;
            session.LastSafeCoord = session.PlayerPosition;
            session.HasPlayerPosition = true;
        }

        private const string StartHubLabel = "S05";

        private void PlaceLandmarks()
        {
            if (_board.UsesWorldMap) return; // 확정 맵에는 임시 지형지물을 놓지 않음
            for (int i = 0; i < PlaceholderLandmarks.Length && i < LandmarkOffsets.Length; i++)
            {
                if (!_board.TryGetTile(LandmarkOffsets[i], out var tile)) continue;

                // A landmark is a real point of interest — clear any
                // obstacle/encounter roll on its tile so it's always visible
                // and walkable for testing.
                tile.SetBlocked(false);
                tile.SetEncounter(false);
                tile.SetLandmark(PlaceholderLandmarks[i]);
            }
        }

        // Public so editor tooling can spawn it for a headless screenshot
        // check without entering Play mode (Start() doesn't run there).
        public void SpawnToken()
        {
            _board ??= GetComponent<HexBoard>();
            if (_board.UsesWorldMap)
            {
                PlaceAtStartIfNew();
                _currentCoord = BoardSession.Instance.PlayerPosition;
            }

            // 판 위 말 = 파티장(1번) 인물 모델 축소 (2026-11-07, AssetList "파티장 캐릭터 미니어처").
            // 루트 "Token"은 칸 윗면에 서고, 모델은 그 자식. 파티가 없으면(에디터 미리보기 등) 예전 빨간 공.
            var go = new GameObject("Token");
            _token = go.transform;
            _token.rotation = Quaternion.Euler(0f, 180f, 0f); // 사신 말과 같은 방향 (카메라 쪽)
            RefreshLeaderFigure();
            _token.position = TokenSurfacePosition(_currentCoord);
        }

        private const float LeaderFigureScale = 0.8f; // 11/10 기획 3-1 B: 파티장 말 0.8배 (사신 말은 0.6배 그대로)
        private const float TurnSeconds = 0.18f;      // 첫 칸 출발 전 몸 돌리는 시간 (11/13: 0.12 → 0.25는 느림 → 중간 0.18)
        private GameObject _tokenFigure;
        private JobDefinition _tokenFigureJob;
        private bool _tokenIsSphere;
        private float _tokenFigureHeight;

        // 파티장이 바뀌면(쓰러짐·영구 사망 → 다음 순번 승계) 말 모델도 바꾼다.
        private void RefreshLeaderFigure()
        {
            if (_token == null) return;
            var job = Leader?.Job;
            if (_tokenFigure != null && job == _tokenFigureJob && (job != null || _tokenIsSphere))
            { Fantasia.UI.HeldWeapon.Show(_tokenFigure, Leader); Fantasia.UI.HandGrip.Attach(_tokenFigure); return; } // 같은 사람이어도 무기는 바뀌었을 수 있음
            if (_tokenFigure != null) DestroySafe(_tokenFigure);

            _tokenFigureJob = job;
            _tokenIsSphere = job == null;
            if (_tokenIsSphere)
            {
                _tokenFigure = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _tokenFigure.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };
                _tokenFigure.transform.SetParent(_token, false);
                _tokenFigure.transform.localPosition = Vector3.up * tokenRadius;
            }
            else
            {
                _tokenFigure = Fantasia.UI.CharacterModels.Create(job, out _tokenFigureHeight); // 받은 모델이 있으면 그것, 없으면 임시 인형
                _tokenFigure.transform.SetParent(_token, false);
                _tokenFigure.transform.localPosition = Vector3.zero;
                _tokenFigure.transform.localRotation = Quaternion.identity;
                _tokenFigure.transform.localScale = Vector3.one * LeaderFigureScale;
            }
            _tokenFigure.name = "LeaderFigure";
            // 칸 클릭(레이캐스트)을 말이 가로막지 않게 충돌체 제거
            foreach (var col in _tokenFigure.GetComponentsInChildren<Collider>(true)) DestroySafe(col);
            if (!_tokenIsSphere) Fantasia.UI.BlobShadow.Attach(_tokenFigure, _tokenFigureHeight); // 발밑 흐린 그림자 (11/10 기획 1A)
            Fantasia.UI.HeldWeapon.Show(_tokenFigure, Leader); // 11/13 장착 무기 종류에 맞는 소품만
            Fantasia.UI.HandGrip.Attach(_tokenFigure);
        }

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // 다음 칸 쪽으로 몸을 돌린다 (수평만).
        private IEnumerator FaceToward(Vector3 target)
        {
            var dir = target - _token.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) yield break;
            var from = _token.rotation;
            var to = Quaternion.LookRotation(dir, Vector3.up);
            if (Quaternion.Angle(from, to) < 1f) yield break;
            for (float t = 0f; t < TurnSeconds; t += Time.deltaTime)
            {
                _token.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / TurnSeconds));
                yield return null;
            }
            _token.rotation = to;
        }

        // Tiles are positioned base-first (see HexBoard.TileHeight), so the
        // token root sits on the tile's top surface; the figure stands on it
        // (the fallback sphere is lifted by its own radius inside RefreshLeaderFigure).
        private Vector3 TokenSurfacePosition(HexCoord coord)
        {
            var pos = _board.CoordToWorld(coord) + Vector3.up * _board.TileHeight;
            // 구조물이 있는 밟는 칸(거점·보스·우물·분수·천막·출구·보물)은 구조물 앞(입구)에 세움.
            // 2026-11-08 거점만 → 11/12 모든 구조물 칸 (HexTile.FitWalkableProp). 보이는 위치만 바뀜, 칸·이동 규칙은 그대로.
            if (_board.TryGetTile(coord, out var tile) && tile.StandFront > 0f)
                pos += Vector3.back * tile.StandFront;
            return pos;
        }

        private static int ZoneWearLevel(int region) => region switch { 1 => 4, 2 => 7, _ => 1 };
    }
}
