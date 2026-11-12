using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Fantasia.Core
{
    // 11/12 팀장 지적: 전투에서 캐릭터가 도는 듯한 동작 때 렉이 심함.
    // 전투 중 매 프레임 시간을 "그때 재생 중인 동작"별로 모아 평균을 냄 → 어떤 동작이 느린지.
    // 느린 프레임(25ms 넘게)은 따로 기록. 자동 플레이 -autoplay-spikes 일 때만 붙음. player.log "[느린 프레임]" · "[동작별 프레임]".
    public class FrameSpikeLog : MonoBehaviour
    {
        public const float SpikeMs = 25f;
        public static int LastShotFrame = -100; // 자동 플레이 스크린샷 저장 프레임은 빼기 (그림 저장이 느림)
        private readonly Dictionary<string, (double ms, int n)> _byMotion = new();
        private bool _inBattle;
        private static readonly bool Uncapped = System.Environment.GetCommandLineArgs().Contains("-autoplay-uncapped");

        private void Update()
        {
            if (Uncapped) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; } // 화면 주기(60)에 묶이지 않은 실제 시간
            bool battle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "CombatTest";
            if (_inBattle && !battle) Report();
            _inBattle = battle;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (!battle || Time.frameCount < 30 || Time.frameCount - LastShotFrame < 4 || ms > 400f) return;

            var playing = new List<string>();
            foreach (var anim in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!anim.isActiveAndEnabled) continue;
                var clips = anim.GetCurrentAnimatorClipInfo(0);
                if (clips.Length == 0) continue;
                string n = clips[0].clip.name;
                int at = n.IndexOf('@');
                string motion = at >= 0 ? n.Substring(at + 1) : n;
                if (motion != "Idle") playing.Add(motion);
            }
            string key = playing.Count == 0 ? "(모두 서 있음)" : string.Join("+", playing.Distinct().OrderBy(s => s));
            foreach (var k in playing.Count == 0 ? new[] { key } : playing.Distinct().ToArray())
            {
                _byMotion.TryGetValue(k, out var v);
                _byMotion[k] = (v.ms + ms, v.n + 1);
            }
            if (ms >= SpikeMs) Debug.Log($"[느린 프레임] {ms:0.0}ms · {key}");
        }

        private void Report()
        {
            var sb = new StringBuilder("[동작별 프레임]");
            foreach (var kv in _byMotion.OrderByDescending(k => k.Value.ms / k.Value.n))
                sb.Append($"\n  {kv.Key}: 평균 {kv.Value.ms / kv.Value.n:0.0}ms ({kv.Value.n}프레임)");
            Debug.Log(sb.ToString());
            _byMotion.Clear();
        }

        private void OnApplicationQuit() { if (_byMotion.Count > 0) Report(); }
    }
}
