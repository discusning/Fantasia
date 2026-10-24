using System;
using UnityEngine;

namespace Fantasia.Core
{
    // 음악 ID → 곡 (Resources/MusicLibrary.asset). 곡이 오면 Clip만 끼우면 된다.
    [CreateAssetMenu(menuName = "Fantasia/Music Library")]
    public class MusicLibrary : ScriptableObject
    {
        [Serializable] public class Entry { public string Id; public AudioClip Clip; [Range(0f, 1f)] public float Volume = 0.7f; }
        public Entry[] Tracks = Array.Empty<Entry>();

        private static MusicLibrary _current;
        public static MusicLibrary Current => _current != null ? _current : (_current = Resources.Load<MusicLibrary>("MusicLibrary"));
        public Entry Find(string id) => Array.Find(Tracks, t => t != null && t.Id == id);
    }
}
