using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Core
{
    // 11/15 팀장 "중간에 갑자기 게임이 멈췄다가 한꺼번에 진행됨": 0.12초 넘게 멈춘 프레임을 player.log에 남김.
    // 바로 앞에 찍힌 로그 두 줄(무슨 일이 있었는지)과 씬을 같이. 배포판에서도 켜져 있어 플레이 로그로 원인을 찾을 수 있음.
    // 로그 위치: %USERPROFILE%\AppData\LocalLow\<회사>\Fantasia\Player.log  ·  "[멈춤]"으로 검색.
    public class HitchLog : MonoBehaviour
    {
        public const float HitchMs = 120f;
        private static string _last1 = "", _last2 = "";
        public static int Count { get; private set; }
        public static float WorstMs { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var go = new GameObject("HitchLog");
            DontDestroyOnLoad(go);
            int pi = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-profile-raw");
            if (pi >= 0) { var a = System.Environment.GetCommandLineArgs(); UnityEngine.Profiling.Profiler.logFile = a[pi + 1]; UnityEngine.Profiling.Profiler.enableBinaryLog = true; UnityEngine.Profiling.Profiler.maxUsedMemory = 256 * 1024 * 1024; UnityEngine.Profiling.Profiler.enabled = true; }
            go.AddComponent<FrameSplitStart>(); go.AddComponent<HitchLog>(); go.AddComponent<FrameSplitEnd>();
            Application.logMessageReceived += (msg, _, type) =>
            {
                if (msg.StartsWith("[멈춤]")) return;
                _last2 = _last1;
                _last1 = msg.Length > 120 ? msg.Substring(0, 120) : msg;
            };
            SceneManager.sceneLoaded += (_, _) => _sceneFrame = Time.frameCount;
            Font.textureRebuilt += f => Debug.Log($"[글꼴 다시 만듦] {f.name} 프레임 {Time.frameCount}");
        }

        private static int _sceneFrame;
        private double _lastReal;
        // 멈춘 프레임에 유니티 안쪽 어디서 시간이 갔는지 (개발용 빌드에서만 값이 잡힘)
        private static readonly string[] Markers =
        {
            "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "Update.ScriptRunDelayedDynamicFrameRate",
            "Canvas.SendWillRenderCanvases", "GC.Collect", "Shader.CreateGPUProgram", "Camera.Render", "Gfx.WaitForPresentOnGfxThread",
            "Loading.ReadObject", "Physics.SyncColliderTransform", "Animators.Update", "ParticleSystem.Update", "Gfx.UploadTexture",
        };
        private readonly System.Collections.Generic.List<(string, Unity.Profiling.ProfilerRecorder)> _rec = new();
        private void OnEnable()
        {
            foreach (var m in Markers)
            {
                var cat = m.StartsWith("GC") ? Unity.Profiling.ProfilerCategory.Memory : m.StartsWith("Gfx") || m.StartsWith("Camera") || m.StartsWith("Shader") ? Unity.Profiling.ProfilerCategory.Render : Unity.Profiling.ProfilerCategory.Scripts;
                _rec.Add((m, Unity.Profiling.ProfilerRecorder.StartNew(cat, m)));
            }
        }
        private void OnDisable() { foreach (var (_, r) in _rec) r.Dispose(); _rec.Clear(); }
        private string Breakdown()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (n, r) in _rec) { if (!r.Valid) continue; double ms = r.LastValue / 1e6; if (ms >= 15) sb.Append($"{n} {ms:0}ms, "); }
            return sb.Length > 0 ? " · 안쪽: " + sb : "";
        }

        private void Update()
        {
            double nowReal = Time.realtimeSinceStartupAsDouble; double realMs = _lastReal > 0 ? (nowReal - _lastReal) * 1000.0 : 0; _lastReal = nowReal;
            float ms = Time.unscaledDeltaTime * 1000f;
            // 자동 플레이 스크린샷 저장 프레임은 뺌
            if (Time.frameCount < 30 || Time.frameCount - FrameSpikeLog.LastShotFrame < 4) return;
            if (ms < HitchMs && realMs < HitchMs) return;
            Count++;
            if (ms > WorstMs) WorstMs = ms;
            bool load = Time.frameCount - _sceneFrame < 5;
            Debug.Log($"[멈춤] {ms:0}ms · {SceneManager.GetActiveScene().name}{(load ? " (씬 불러오기)" : "")} · 앞: {_last2} | {_last1} · 실제 {realMs:0}ms · 스크립트 {FrameSplitStart.PrevScriptMs:0}ms · 그 밖 {FrameSplitStart.PrevOtherMs:0}ms{Breakdown()}");
        }
    }
}

