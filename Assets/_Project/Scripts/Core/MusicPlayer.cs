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
    }

    public class MusicPlayer : MonoBehaviour
    {
        private static MusicPlayer _instance;
        private AudioSource _source;
        private Coroutine _fade;
        private static readonly HashSet<string> Warned = new();
        private static float _volumeScale = 1f;   // 설정의 음악 볼륨
        private float _currentEntryVolume = 0.7f;

        public static void SetVolumeScale(float scale)
        {
            _volumeScale = scale;
            if (_instance != null && _instance._source.isPlaying) _instance._source.volume = _instance._currentEntryVolume * scale;
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
                _instance._source = go.AddComponent<AudioSource>();
                _instance._source.loop = true;
                _instance._source.playOnAwake = false;
                return _instance;
            }
        }

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
            if (entry == null || entry.Clip == null)
            {
                if (Warned.Add(id)) Debug.LogWarning($"[Music] {id} 곡 없음 — Resources/MusicLibrary에 임시 트랙 연결 필요");
                yield break;
            }
            _source.clip = entry.Clip;
            _source.volume = entry.Volume * _volumeScale;
            _currentEntryVolume = entry.Volume;
            _source.Play();
        }

        private IEnumerator FadeOut(float seconds)
        {
            if (!_source.isPlaying) yield break;
            float start = _source.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(start, 0f, t / seconds);
                yield return null;
            }
            _source.Stop();
        }
    }
}
