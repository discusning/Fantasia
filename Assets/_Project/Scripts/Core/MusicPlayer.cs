using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Core
{
    // 배경 음악 (D28). 논리 ID로 부르고 실제 곡은 Resources/MusicLibrary 에셋에 연결 — 곡이 오면 에셋에 끼우기만 하면 된다.
    // 같은 곡을 다시 부르면 무시 (중복 재생 방지). 곡이 비어 있으면 무음 + 경고 1번.
    public static class MusicIds
    {
        public const string FieldLondon = "BGM_Field_London";
        public const string Battle = "BGM_Battle";
        public const string BossAbel = "BGM_Boss_Abel"; // Boss_Abel_v1.0 제안 ID
        public const string Menu = "BGM_Menu";           // 메인 메뉴 (1막 게임기획 보고서 11/3 음악 6상황)
        public static readonly string[] All = { Menu, FieldLondon, Battle, BossAbel };
    }

    public class MusicPlayer : MonoBehaviour
    {
        private static MusicPlayer _instance;
        private AudioSource _a, _b;          // 이음새 없는 반복: 두 재생기를 번갈아 예약 재생
        private Coroutine _fade;
        private static readonly HashSet<string> Warned = new();
        private static float _volumeScale = 1f;   // 설정의 음악 볼륨
        private float _currentEntryVolume = 0.7f;

        // ── 이음새 없는 반복 (2026-11-10) ──
        // 받은 MP3(11/7)는 Unity가 앞쪽 인코더 지연(약 47ms)을 무음으로 남김 → loop=true면 반복마다 그만큼 끊김.
        // 곡 내용은 그 무음 뒤부터 안내문 길이만큼 온전히 들어 있음 (Editor/MusicLoopCheck로 확인).
        // 그래서 "무음 뒤 지점 ~ 곡 길이"만 두 재생기로 이어 붙여 반복한다. 길이가 안 맞는 곡(새로 바뀐 파일 등)은 예전처럼 loop.
        private const double Mp3LeadSeconds = 2257.0 / 48000.0; // 정보 프레임 1152 + 인코더 지연 576 + 디코더 지연 529 샘플
        private static readonly Dictionary<string, double> LoopSeconds = new()
        {
            [MusicIds.FieldLondon] = 112.941333, // Docs/Audio/Sound_1107 대응표 길이_초
            [MusicIds.Battle] = 96.0,
            [MusicIds.BossAbel] = 102.858,
            [MusicIds.Menu] = 90.0,
        };
        private bool _gapless;
        private int _loopStartSamples;
        private double _loopLength, _nextSwitch;
        private bool _aIsCurrent;

        public static void SetVolumeScale(float scale)
        {
            _volumeScale = scale;
            if (_instance != null && _instance.IsPlaying) _instance.SetVolume(_instance._currentEntryVolume * scale);
        }

        public static string CurrentId { get; private set; }

        private static MusicPlayer Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("MusicPlayer");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<MusicPlayer>();
                _instance._a = go.AddComponent<AudioSource>();
                _instance._b = go.AddComponent<AudioSource>();
                _instance._a.playOnAwake = _instance._b.playOnAwake = false;
                return _instance;
            }
        }

        private bool IsPlaying => _a.isPlaying || _b.isPlaying;
        private float Volume => _a.volume;
        private void SetVolume(float v) { _a.volume = v; _b.volume = v; }

        // 곡 시작 (이미 같은 곡이면 그대로). fadeOutSeconds 동안 지금 곡을 줄인 뒤 바꾼다.
        public static void Play(string id, float fadeOutSeconds = 0.4f)
        {
            if (CurrentId == id) return;
            CurrentId = id;
            var p = Instance;
            if (p._fade != null) p.StopCoroutine(p._fade);
            p._fade = p.StartCoroutine(p.SwitchTo(id, fadeOutSeconds));
        }

        public static void Stop(float fadeOutSeconds)
        {
            CurrentId = null;
            var p = Instance;
            if (p._fade != null) p.StopCoroutine(p._fade);
            p._fade = p.StartCoroutine(p.FadeOut(fadeOutSeconds));
        }

        private IEnumerator SwitchTo(string id, float fadeOutSeconds)
        {
            yield return FadeOut(fadeOutSeconds);
            var entry = MusicLibrary.Current != null ? MusicLibrary.Current.Find(id) : null;
            // 에셋 칸이 비었으면 Resources/Music/{ID} 파일 (효과음과 같은 방식 — 파일 이름만 맞추면 됨)
            var fileClip = entry != null && entry.Clip != null ? null : Resources.Load<AudioClip>("Music/" + id);
            if (fileClip != null) entry = new MusicLibrary.Entry { Id = id, Clip = fileClip, Volume = entry != null ? entry.Volume : 0.7f };
            if (entry == null || entry.Clip == null)
            {
                if (Warned.Add(id)) Debug.LogWarning($"[Music] {id} 곡 없음 — Resources/Music/{id} 파일 또는 MusicLibrary 칸에 연결 필요");
                yield break;
            }
            _currentEntryVolume = entry.Volume;
            SetVolume(entry.Volume * _volumeScale);
            StartTrack(id, entry.Clip);
        }

        private void StartTrack(string id, AudioClip clip)
        {
            _a.clip = _b.clip = clip;
            int rate = clip.frequency;
            _gapless = LoopSeconds.TryGetValue(id, out double len)
                && Math.Abs(clip.samples - (Mp3LeadSeconds + len) * rate) < 0.02 * rate; // 길이가 MP3 지연 + 안내문 길이와 맞을 때만
            if (!_gapless)
            {
                _a.loop = true;
                _a.Play();
                _aIsCurrent = true;
                return;
            }
            _loopStartSamples = (int)Math.Round(Mp3LeadSeconds * rate);
            _loopLength = len;
            _a.loop = _b.loop = false;
            double start = AudioSettings.dspTime + 0.05;
            _a.timeSamples = _loopStartSamples;
            _a.PlayScheduled(start);
            _nextSwitch = start + _loopLength;
            _a.SetScheduledEndTime(_nextSwitch);
            _aIsCurrent = true;
            ScheduleNext();
        }

        // 다음 회차를 쉬고 있는 재생기에 예약
        private void ScheduleNext()
        {
            var next = _aIsCurrent ? _b : _a;
            next.timeSamples = _loopStartSamples;
            next.PlayScheduled(_nextSwitch);
            next.SetScheduledEndTime(_nextSwitch + _loopLength);
        }

        private void Update()
        {
            if (!_gapless) return;
            // 바꿔 탄 뒤에는 방금 끝난 재생기에 그다음 회차를 예약
            if (AudioSettings.dspTime > _nextSwitch + 0.1)
            {
                _aIsCurrent = !_aIsCurrent;
                _nextSwitch += _loopLength;
                ScheduleNext();
                Debug.Log($"[Music] {CurrentId} 이음새 없이 반복 ({_loopLength:0.0}초 구간)");
            }
        }

        private IEnumerator FadeOut(float seconds)
        {
            if (!IsPlaying) { StopAll(); yield break; }
            float start = Volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                SetVolume(Mathf.Lerp(start, 0f, t / seconds));
                yield return null;
            }
            StopAll();
        }

        private void StopAll()
        {
            _gapless = false;
            _a.Stop();
            _b.Stop();
        }
    }
}
