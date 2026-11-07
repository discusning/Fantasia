using System;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Fantasia.Editor
{
    // 자동 플레이가 저장한 장면 사진(0000.jpg ...)을 MP4로 묶는다 (-autoplay-dicevideo 팀 공유 영상용).
    // 사용: Unity.exe -batchmode -quit -projectPath ... -executeMethod Fantasia.Editor.FramesToVideo.Run -frames <폴더> -out <파일.mp4> [-fps 30] [-wav <소리.wav>]
    // -wav: 16비트 PCM WAV를 소리 트랙으로 같이 넣음 (-autoplay-soundvideo의 audio.wav, 2026-11-07)
    public static class FramesToVideo
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key, string def) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
            string dir = Arg("-frames", null), output = Arg("-out", null), wav = Arg("-wav", null);
            int fps = int.Parse(Arg("-fps", "30"));
            var files = Directory.GetFiles(dir, "*.jpg").OrderBy(f => f).ToArray();
            if (files.Length == 0) throw new Exception("사진 없음: " + dir);

            var first = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            first.LoadImage(File.ReadAllBytes(files[0]));
            int w = first.width & ~1, h = first.height & ~1; // H.264는 짝수 크기
            var attr = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps),
                width = (uint)w,
                height = (uint)h,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High,
            };

            float[] audio = null;
            int channels = 0, rate = 0;
            if (!string.IsNullOrEmpty(wav)) audio = ReadWav(wav, out channels, out rate);

            var frame = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (var encoder = audio != null
                ? new MediaEncoder(output, attr, new AudioTrackAttributes { sampleRate = new MediaRational(rate), channelCount = (ushort)channels, language = "ko" })
                : new MediaEncoder(output, attr))
            {
                int perFrame = rate / fps * channels, pos = 0;
                foreach (var f in files)
                {
                    src.LoadImage(File.ReadAllBytes(f));
                    // JPG를 읽으면 RGB24가 됨 → 인코더가 받는 RGBA32로 옮겨 담기
                    frame.SetPixels(src.GetPixels(0, 0, w, h));
                    frame.Apply();
                    encoder.AddFrame(frame);
                    if (audio == null) continue;
                    var buf = new NativeArray<float>(perFrame, Allocator.Temp);
                    for (int i = 0; i < perFrame && pos + i < audio.Length; i++) buf[i] = audio[pos + i];
                    pos += perFrame;
                    encoder.AddSamples(buf);
                    buf.Dispose();
                }
            }
            string sound = audio != null ? $", 소리 {audio.Length / channels / (float)rate:0.0}초 {rate}Hz {channels}ch" : "";
            Debug.Log($"[FramesToVideo] {files.Length}장 → {output} ({files.Length / (float)fps:0.0}초, {w}×{h}{sound})");
        }

        private static float[] ReadWav(string path, out int channels, out int rate)
        {
            using var r = new BinaryReader(File.OpenRead(path));
            r.ReadBytes(22);
            channels = r.ReadInt16();
            rate = r.ReadInt32();
            r.ReadBytes(8); // 초당 바이트·블록 크기·비트 수
            if (new string(r.ReadChars(4)) != "data") throw new Exception("WAV 형식 다름: " + path);
            int count = r.ReadInt32() / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = r.ReadInt16() / 32768f;
            return samples;
        }
    }
}
