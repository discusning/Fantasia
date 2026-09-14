using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.UI
{
    // Lives only in the DialogueTest scene. Its children are the placeholder
    // speaker capsules (named to match DialoguePanel.DialogueLine.Speaker
    // values); whichever one matches the currently-showing line stays at
    // full brightness and the rest dim — each capsule keeps its own
    // identity color (dimmed, not replaced), so a multi-character
    // conversation reads as "the camera didn't move, but you can tell who's
    // talking" without losing which character is which.
    [RequireComponent(typeof(Transform))]
    public class DialogueStageController : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const float DimFactor = 0.35f;

        private readonly Dictionary<Transform, Color> _baseColors = new Dictionary<Transform, Color>();

        private void Start()
        {
            foreach (Transform child in transform)
            {
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null) _baseColors[child] = renderer.sharedMaterial.color;
            }

            DialoguePanel.EnsureExists();
            DialoguePanel.Instance.SpeakerChanged += OnSpeakerChanged;

            // The scene may have just loaded specifically because a line is
            // already queued up to show — sync immediately instead of
            // waiting for the next SpeakerChanged.
            OnSpeakerChanged(DialoguePanel.Instance.CurrentSpeaker);
        }

        private void OnDestroy()
        {
            if (DialoguePanel.Instance != null)
            {
                DialoguePanel.Instance.SpeakerChanged -= OnSpeakerChanged;
            }
        }

        private void OnSpeakerChanged(string speakerName)
        {
            foreach (var pair in _baseColors)
            {
                var child = pair.Key;
                var baseColor = pair.Value;
                bool isSpeaking = child.name == speakerName;

                var block = new MaterialPropertyBlock();
                var renderer = child.GetComponent<Renderer>();
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, isSpeaking ? baseColor : Dim(baseColor));
                renderer.SetPropertyBlock(block);
            }
        }

        private static Color Dim(Color c) => new Color(c.r * DimFactor, c.g * DimFactor, c.b * DimFactor, c.a);
    }
}
