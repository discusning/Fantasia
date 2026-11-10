using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 음악 반복 이음새 점검 (2026-11-10). 곡마다 앞·뒤 무음 길이, 끝과 처음 음량, 이음새에서 튀는 정도를 잼.
    // MP3는 만들 때 앞뒤에 짧은 무음이 붙는 경우가 많아, AudioSource.loop로 반복하면 그만큼 끊겨 들림.
    // 사용: Unity.exe -batchmode -quit -projectPath ... -executeMethod Fantasia.Editor.MusicLoopCheck.Run → Logs/music_loop_check.txt
    public static class MusicLoopCheck
    {
        private const float Silence = 0.001f; // 약 −60dB 아래는 무음으로 봄
        private static readonly Dictionary<string, double> Loop = new() // MusicPlayer.LoopSeconds와 같음
        {
            ["BGM_Field_London"] = 112.941333, ["BGM_Battle"] = 96.0, ["BGM_Boss_Abel"] = 102.858, ["BGM_Menu"] = 90.0,
        };

        [MenuItem("Fantasia/Audio/Check Music Loops")]
        public static void Run()
        {
            var lines = new List<string> { "곡 | 길이 | 앞 무음 | 뒤 무음 | 끝 50ms 음량 | 처음 50ms 음량 | 이음새 튐 | 판정" };
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Resources/Music" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                int ch = clip.channels, n = clip.samples, rate = clip.frequency;
                var data = new float[n * ch];
                if (!clip.GetData(data, 0)) { lines.Add($"{clip.name} | 읽기 실패 (불러오기 방식: {clip.loadType})"); continue; }

                int lead = 0; while (lead < n && Peak(data, lead, ch) < Silence) lead++;
                int tail = 0; while (tail < n && Peak(data, n - 1 - tail, ch) < Silence) tail++;
                int win = rate / 20; // 50ms
                float endRms = Rms(data, n - win, win, ch), startRms = Rms(data, Mathf.Min(lead, n - win), win, ch); // 처음 음량은 무음 뒤부터
                float jump = 0f;
                for (int c = 0; c < ch; c++) jump = Mathf.Max(jump, Mathf.Abs(data[(n - 1) * ch + c] - data[c]));

                float leadMs = lead * 1000f / rate, tailMs = tail * 1000f / rate;
                string verdict = leadMs + tailMs > 30f ? "틈 들릴 수 있음" : jump > 0.1f ? "딸깍 소리 가능" : "괜찮음";
                lines.Add($"{clip.name} | {clip.length:0.000}초 ({n}샘플 {rate}Hz) | {leadMs:0}ms | {tailMs:0}ms | {Db(endRms)} | {Db(startRms)} | {jump:0.000} | {verdict}");

                // MusicPlayer 이음새 없는 반복 확인: [시작, 시작+안내문 길이) 구간을 이어 붙였을 때 이음새가 자연스러운지.
                // 끝 두 샘플로 다음 샘플을 예측해 실제 시작 샘플과의 차이를 잼 (곡 안 보통 샘플 차이와 비교).
                if (Loop.TryGetValue(clip.name, out double len))
                {
                    int L = (int)System.Math.Round(len * rate);
                    float Err(int s)
                    {
                        float e = 0f;
                        for (int c = 0; c < ch; c++)
                        {
                            float a = data[(s + L - 2) * ch + c], b = data[(s + L - 1) * ch + c];
                            e += Mathf.Abs(b + (b - a) - data[s * ch + c]);
                        }
                        return e / ch;
                    }
                    float typical = 0f; int cnt = 0;
                    for (int i = n / 2; i < n / 2 + rate; i++, cnt++) for (int c = 0; c < ch; c++) typical += Mathf.Abs(data[i * ch + c] - 2 * data[(i - 1) * ch + c] + data[(i - 2) * ch + c]) / ch;
                    typical /= cnt;
                    int used = (int)System.Math.Round(2257.0 / 48000.0 * rate);
                    int best = used; float bestErr = float.MaxValue;
                    for (int s = used - 400; s <= used + 400 && s + L <= n; s++) { float e = Err(s); if (e < bestErr) { bestErr = e; best = s; } }
                    lines.Add($"  └ 반복 구간 {used}~{used + L} (끝 여유 {n - used - L}샘플): 이음새 오차 {Err(used):0.0000} / 곡 안 보통 {typical:0.0000} · 오차 최소 시작점 {best} ({bestErr:0.0000})");
                }
            }
            string outPath = Path.Combine(Application.dataPath, "../Logs/music_loop_check.txt");
            File.WriteAllLines(outPath, lines);
            foreach (var l in lines) Debug.Log("[MusicLoop] " + l);
        }

        private static float Peak(float[] d, int i, int ch)
        {
            float p = 0f;
            for (int c = 0; c < ch; c++) p = Mathf.Max(p, Mathf.Abs(d[i * ch + c]));
            return p;
        }

        private static float Rms(float[] d, int start, int count, int ch)
        {
            double s = 0;
            for (int i = start * ch; i < (start + count) * ch; i++) s += d[i] * d[i];
            return Mathf.Sqrt((float)(s / (count * ch)));
        }

        private static string Db(float rms) => rms <= 0.00001f ? "무음" : $"{20f * Mathf.Log10(rms):0}dB";
    }
}
