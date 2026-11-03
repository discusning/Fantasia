using System;
using UnityEngine;

namespace Fantasia.Core
{
    // 효과음 ID → 소리 (Resources/SfxLibrary.asset). 소리가 오면 Clip만 끼우면 된다. 비어 있으면 Resources/Sfx/{ID} 파일을 찾는다.
    [CreateAssetMenu(menuName = "Fantasia/Sfx Library")]
    public class SfxLibrary : ScriptableObject
    {
        [Serializable] public class Entry { public string Id; public AudioClip Clip; [Range(0f, 1f)] public float Volume = 0.8f; }
        public Entry[] Sounds = Array.Empty<Entry>();

        private static SfxLibrary _current;
        public static SfxLibrary Current => _current != null ? _current : (_current = Resources.Load<SfxLibrary>("SfxLibrary"));
        public Entry Find(string id) => Array.Find(Sounds, s => s != null && s.Id == id);
    }
}
