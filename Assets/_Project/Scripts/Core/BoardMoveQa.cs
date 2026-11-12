using System.Collections.Generic;
using System.Linq;
using Fantasia.Board;
using UnityEngine;

namespace Fantasia.Core
{
    // 11/12 팀장 지시: 이동 시점 · 프레임 드랍 · 구조물 반투명 QA. 자동 플레이 -autoplay-move-qa 가 붙임 (스크린샷 없음).
    // 이동하는 동안 매 프레임:
    //   ① 프레임 시간 (이동 중만)   ② 말을 가리는 구조물이 0.35초 안에 반투명이 되는지
    //   ③ 가리지 않는데 0.6초 넘게 반투명으로 남는지   ④ 카메라가 구조물 안에 들어가는지   ⑤ 카메라가 한 프레임에 크게 도는지
    // 멈출 때마다: ⑥ 말이 구조물 안에 서 있는지
    public class BoardMoveQa : MonoBehaviour
    {
        public BoardTestController Board;
        public readonly List<float> MovingMs = new();
        public int LateFade, StuckFade, CamInside, TokenInside, Stops, FadedFrames;
        public float MaxYawStep, MaxCamInsideDepth;
        private readonly Dictionary<HexTile, float> _hiddenOpaque = new(), _idleFaded = new();
        private readonly HashSet<HexTile> _lateReported = new(), _stuckReported = new();
        private bool _wasMoving;
        private float _afterStop, _lastYaw;
        private bool _hasYaw;

        private void LateUpdate()
        {
            if (Board == null || Camera.main == null) return;
            var token = Board.DevToken;
            if (token == null) return;
            bool moving = Board.DevMoving;
            if (moving) _afterStop = 0.8f; else _afterStop -= Time.unscaledDeltaTime;
            if (_wasMoving && !moving) CheckStop(token.position);
            _wasMoving = moving;
            if (_afterStop <= 0f) { _hasYaw = false; return; }

            var cam = Camera.main.transform;
            float dt = Time.unscaledDeltaTime;
            if (moving) MovingMs.Add(dt * 1000f);
            if (_hasYaw && dt > 0f) MaxYawStep = Mathf.Max(MaxYawStep, Mathf.Abs(Mathf.DeltaAngle(_lastYaw, cam.eulerAngles.y)) / dt); // 초당 각도
            _lastYaw = cam.eulerAngles.y; _hasYaw = true;

            bool anyFaded = false;
            foreach (var tile in HexTile.WithProps)
            {
                if (tile == null || !tile.PropVisible) continue;
                if ((tile.transform.position - token.position).sqrMagnitude > 100f) continue;
                var b = tile.PropBounds();
                bool hides = BoardOccluderFade.Hides(b, cam.position, token.position);
                // ② 가리는데 아직 불투명
                if (hides && tile.PropAlpha > 0.6f) { _hiddenOpaque.TryGetValue(tile, out var t); t += dt; _hiddenOpaque[tile] = t; if (t > 0.35f && _lateReported.Add(tile)) LateFade++; }
                else _hiddenOpaque.Remove(tile);
                // ③ 안 가리는데 반투명으로 남음
                if (!hides && tile.PropAlpha < 0.999f) { _idleFaded.TryGetValue(tile, out var t); t += dt; _idleFaded[tile] = t; if (t > 0.6f && _stuckReported.Add(tile)) StuckFade++; }
                else _idleFaded.Remove(tile);
                if (tile.PropAlpha < 0.999f) anyFaded = true;
                // ④ 카메라가 구조물 안
                var inner = b; inner.Expand(-0.1f);
                if (inner.Contains(cam.position)) { CamInside++; MaxCamInsideDepth = Mathf.Max(MaxCamInsideDepth, Mathf.Min(cam.position.y - inner.min.y, inner.max.y - cam.position.y)); }
            }
            if (anyFaded) FadedFrames++;
        }

        private void CheckStop(Vector3 p)
        {
            Stops++;
            foreach (var tile in HexTile.WithProps)
            {
                if (tile == null || !tile.PropVisible) continue;
                var b = tile.PropBounds(); b.Expand(-0.1f);
                if (b.Contains(p + Vector3.up * 0.4f)) { TokenInside++; Debug.Log($"[이동QA] 말이 구조물 안에 섬: {tile.name}"); }
            }
        }

        public string Summary(out bool frameOk)
        {
            var ms = MovingMs.OrderBy(x => x).ToList();
            float avg = ms.Count > 0 ? ms.Average() : 0f;
            float p99 = ms.Count > 0 ? ms[Mathf.Min(ms.Count - 1, (int)(ms.Count * 0.99f))] : 0f;
            float max = ms.Count > 0 ? ms[^1] : 0f;
            int slow = ms.Count(x => x > 33.4f);
            frameOk = ms.Count > 0 && slow <= ms.Count / 100;
            return $"이동 프레임 {ms.Count}개 · 평균 {avg:0.0}ms ({(avg > 0 ? 1000f / avg : 0):0} FPS) · 99% {p99:0.0}ms · 최대 {max:0.0}ms · 33ms 넘는 프레임 {slow}개";
        }
    }
}
