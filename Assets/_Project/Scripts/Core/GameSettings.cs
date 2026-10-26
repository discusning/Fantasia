using System;
using System.IO;
using UnityEngine;

namespace Fantasia.Core
{
    // 설정 (세이브 추가 수정안 2): 전체·음악·효과음 볼륨, 해상도, 전체 화면 — 진행 데이터와 별도 파일 (settings.json).
    // 새 게임·전멸 뒤에도 유지. 볼륨은 바꾸는 즉시 반영·저장, 화면 설정은 15초 유지 확인 뒤 저장 (SettingsPanel).
    [Serializable]
    public class GameSettings
    {
        public const float DefaultMaster = 1f, DefaultMusic = 0.8f, DefaultSfx = 0.8f;

        public float master = DefaultMaster, music = DefaultMusic, sfx = DefaultSfx;
        public int width, height;      // 0 = 아직 정하지 않음 (지금 화면 그대로)
        public bool fullscreen = true;
        public int quality = -1;       // -1 = 자동 (그래픽 카드 보고 결정). 설정 창 항목은 기획 OK 후
        public bool vsync = true;      // 수직 동기화 — 모니터 주사율 고정 (노트북 발열·화면 찢김 방지)

        private static GameSettings _current;
        public static GameSettings Current => _current ??= Load();
        private static string PathOf => Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Defaults()
        {
            var display = Screen.currentResolution;
            return new GameSettings { width = display.width, height = display.height, fullscreen = true };
        }

        private static GameSettings Load()
        {
            try
            {
                if (File.Exists(PathOf)) return JsonUtility.FromJson<GameSettings>(File.ReadAllText(PathOf)) ?? new GameSettings();
            }
            catch (Exception e) { Debug.LogWarning("[Settings] 설정 파일을 읽지 못함 — 기본값: " + e.Message); }
            return new GameSettings();
        }

        public void Save()
        {
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogError("[Settings] 설정 저장 실패: " + e.Message); }
        }

        // 볼륨 반영: 전체 = 모든 소리, 음악 = 배경 음악 (효과음은 효과음이 생기면 같은 값으로)
        public void ApplyAudio()
        {
            AudioListener.volume = Mathf.Clamp01(master);
            MusicPlayer.SetVolumeScale(Mathf.Clamp01(music));
        }

        public void ApplyDisplay()
        {
            if (width <= 0 || height <= 0) return;
            Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        // 그래픽 품질: 자동이면 내장 그래픽은 High(그림자 있음, 안티에일리어싱 2x), 외장은 Ultra(4x)
        public static bool IsIntegratedGpu
        {
            get
            {
                string n = SystemInfo.graphicsDeviceName ?? "";
                return n.Contains("Intel") || n.Contains("Radeon(TM) Graphics") || n.Contains("Vega") || SystemInfo.graphicsMemorySize < 2048;
            }
        }

        public void ApplyQuality()
        {
            int levels = QualitySettings.names.Length;
            int level = quality >= 0 ? Mathf.Min(quality, levels - 1) : (IsIntegratedGpu ? Mathf.Min(3, levels - 1) : levels - 1);
            QualitySettings.SetQualityLevel(level, true);
            if (quality < 0 && IsIntegratedGpu) QualitySettings.antiAliasing = 2;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            Application.targetFrameRate = vsync ? -1 : 120;
        }

        // 게임을 켤 때 저장된 설정 적용
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnStart()
        {
            if (Environment.GetCommandLineArgs().Length > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-autoplay") >= 0) { Current.ApplyAudio(); Current.ApplyQuality(); return; } // 자동 플레이는 창 크기 인자 우선
            Current.ApplyAudio();
            Current.ApplyQuality();
            // 화면 설정을 한 번도 바꾸지 않았으면 모니터 해상도 전체 화면으로 — 자동 플레이 등이 남긴 작은 창 크기를 물려받지 않게
            if (Current.width <= 0 || Current.height <= 0)
            {
                var d = Defaults();
                Screen.SetResolution(d.width, d.height, FullScreenMode.FullScreenWindow);
            }
            else Current.ApplyDisplay();
        }
    }
}
