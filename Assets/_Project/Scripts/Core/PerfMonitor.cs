using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Fantasia.Core
{
    // 성능·안정성 점검 (D29, 자동 플레이 -autoplay-perf): 프레임 시간·메모리·자동 저장 시간을 기록한다.
    //   perf.csv     — 2초마다: 시간, 화면(씬), 평균 fps, 가장 느린 프레임(ms), 사용 메모리, Mono 메모리, GC 횟수
    //   perf_summary — 화면별 평균 fps·하위 1% fps, 메모리 시작/최대/끝, 저장 시간 평균·최대, 씬 전환 횟수
    // 사람이 읽기 쉬운 요약을 autoplay_log에도 남긴다. 게임 동작에는 손대지 않는다.
    public class PerfMonitor : MonoBehaviour
    {
        private string _outDir;
        private readonly StringBuilder _csv = new StringBuilder("time_s,scene,fps_avg,worst_frame_ms,allocated_mb,reserved_mb,mono_mb,gc_count,saves\n");
        private readonly Dictionary<string, List<float>> _frames = new(); // 씬별 프레임 시간(ms)
        private readonly List<double> _saveMs = new();
        private int _lastSaveCount;
        private float _windowStart, _windowWorst;
        private int _windowFrames;
        private float _start;
        private long _memStart, _memMax;
        private int _sceneLoads;
        private int _gcStart;

        public static PerfMonitor Begin(string outDir)
        {
            var go = new GameObject("PerfMonitor");
            DontDestroyOnLoad(go);
            var m = go.AddComponent<PerfMonitor>();
            m._outDir = outDir;
            return m;
        }

        private void Start()
        {
            _start = _windowStart = Time.realtimeSinceStartup;
            _memStart = _memMax = Profiler.GetTotalAllocatedMemoryLong();
            _gcStart = GC.CollectionCount(0);
            _lastSaveCount = Fantasia.Core.SaveGame.SaveCount;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private int _loadFrame = -100;
        private readonly List<float> _loadHitches = new();
        private void OnSceneLoaded(Scene s, LoadSceneMode m) { _sceneLoads++; _loadFrame = Time.frameCount; }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            string scene = SceneManager.GetActiveScene().name;
            // 씬 전환 직후 첫 프레임(불러오기)은 따로 — 평균을 망치지 않게 "로딩"으로
            // 화면 전환 직후 5프레임은 "불러오기"로 따로 센다 (전환 연출 뒤라 플레이어가 끊김으로 느끼지 않음)
            bool loading = Time.frameCount - _loadFrame <= 5;
            if (loading) { if (ms > 30f) _loadHitches.Add(ms); }
            else if (Time.frameCount > 2 && ms < 1000f)
            {
                if (!_frames.TryGetValue(scene, out var list)) _frames[scene] = list = new List<float>();
                list.Add(ms);
            }
            _windowFrames++;
            _windowWorst = Mathf.Max(_windowWorst, ms);
            // 끊김 원인 찾기 (개발 빌드에서만 동작): 50ms 넘는 프레임이면 그 프레임에 오래 걸린 단계를 남긴다
            if (!loading && ms > 50f && Time.frameCount > 2 && _spikes.Count < 200)
            {
                var parts = SpikeMarkers.Select(n => (n, Recorder.Get(n))).Where(p => p.Item2.isValid && p.Item2.elapsedNanoseconds > 5_000_000)
                    .Select(p => $"{p.n} {p.Item2.elapsedNanoseconds / 1e6:0}ms");
                _spikes.Add($"{Time.realtimeSinceStartup - _start:0.0}s {scene} {ms:0}ms: {string.Join(", ", parts)}");
            }
            if (Fantasia.Core.SaveGame.SaveCount != _lastSaveCount)
            {
                _lastSaveCount = Fantasia.Core.SaveGame.SaveCount;
                _saveMs.Add(Fantasia.Core.SaveGame.LastSaveMs);
            }

            float now = Time.realtimeSinceStartup;
            if (now - _windowStart < 2f) return;
            long alloc = Profiler.GetTotalAllocatedMemoryLong();
            _memMax = Math.Max(_memMax, alloc);
            _csv.AppendLine(string.Join(",",
                (now - _start).ToString("0.0"), scene, (_windowFrames / (now - _windowStart)).ToString("0.0"), _windowWorst.ToString("0.0"),
                Mb(alloc), Mb(Profiler.GetTotalReservedMemoryLong()), Mb(Profiler.GetMonoUsedSizeLong()), GC.CollectionCount(0) - _gcStart, _saveMs.Count));
            _windowStart = now;
            _windowFrames = 0;
            _windowWorst = 0f;
        }

        private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0");

        private readonly List<string> _spikes = new();
        private static readonly string[] SpikeMarkers =
        {
            "BehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "CoroutinesDelayedCalls", "GC.Collect", "GC.Alloc",
            "Camera.Render", "Gfx.WaitForPresentOnGfxThread", "Physics.Simulate", "Physics.ProcessReports", "Loading.ReadObject",
            "Application.Integrate Assets in Background", "UIEvents.WillRenderCanvases", "Canvas.SendWillRenderCanvases",
            "TextGenerator.Populate", "Mesh.Bake Scaled Mesh PhysX CollisionData", "Physics.BakeCollisionMeshes", "File.Write",
        };

        private void OnEnable()
        {
            foreach (var n in SpikeMarkers) { var r = Recorder.Get(n); if (r.isValid) r.enabled = true; }
        }

        // 요약을 파일로 쓰고, 자동 플레이 기록에 넣을 문장을 돌려준다
        public string Finish()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"해상도 {Screen.width}x{Screen.height} · 그래픽 {SystemInfo.graphicsDeviceName} · 품질 {QualitySettings.names[QualitySettings.GetQualityLevel()]} · AA {QualitySettings.antiAliasing}x · 수직 동기화 {(QualitySettings.vSyncCount > 0 ? "켬" : "끔")}");
            foreach (var (scene, list) in _frames.OrderByDescending(p => p.Value.Count))
            {
                if (list.Count == 0) continue;
                var sorted = list.OrderByDescending(x => x).ToList();
                float avgMs = list.Average();
                float low1 = sorted.Take(Math.Max(1, sorted.Count / 100)).Average(); // 가장 느린 1% 프레임 평균
                sb.AppendLine($"{scene}: 평균 {1000f / avgMs:0} fps · 하위 1% {1000f / low1:0} fps · 최악 프레임 {sorted[0]:0} ms · 프레임 {list.Count}개");
            }
            long end = Profiler.GetTotalAllocatedMemoryLong();
            if (_loadHitches.Count > 0) sb.AppendLine($"화면 전환 불러오기: {_loadHitches.Count}번 · 평균 {_loadHitches.Average():0} ms · 최대 {_loadHitches.Max():0} ms");
            sb.AppendLine($"게임 중 50ms 넘는 끊김: {_spikes.Count}번");
            sb.AppendLine($"메모리(사용): 시작 {Mb(_memStart)} MB · 최대 {Mb(_memMax)} MB · 끝 {Mb(end)} MB · Mono {Mb(Profiler.GetMonoUsedSizeLong())} MB · GC {GC.CollectionCount(0) - _gcStart}회 · 씬 전환 {_sceneLoads}번");
            if (_saveMs.Count > 0)
                sb.AppendLine($"자동 저장: {_saveMs.Count}번 · 평균 {_saveMs.Average():0.0} ms · 최대 {_saveMs.Max():0.0} ms · 파일 {(File.Exists(SaveGame.SavePath) ? new FileInfo(SaveGame.SavePath).Length / 1024.0 : 0):0.0} KB");
            sb.AppendLine($"실제 시간 {(Time.realtimeSinceStartup - _start) / 60f:0.0}분");
            try
            {
                File.WriteAllText(Path.Combine(_outDir, "perf.csv"), _csv.ToString(), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(_outDir, "perf_summary.txt"), sb.ToString(), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(_outDir, "perf_spikes.txt"), string.Join("\n", _spikes), new UTF8Encoding(true));
            }
            catch (Exception e) { Debug.LogWarning("[Perf] 기록 실패: " + e.Message); }
            return sb.ToString();
        }
    }
}
