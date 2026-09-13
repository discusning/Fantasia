using UnityEngine;

namespace Fantasia.Board
{
    // Placeholder point-of-interest data — real landmark types/effects are a
    // design decision (GDD TBD). This only proves each type can look and
    // read distinctly via Ctrl+Right-click (see LandmarkInfoPanel).
    //
    // Category is free text, not an enum: real games in this genre (Heroes
    // of Might & Magic III, Battle Brothers — see
    // Docs/References/LandmarkUI_Research_2026-09-13.md) end up with many
    // landmark types, so new ones should just be new data assets, not a
    // recompile.
    [CreateAssetMenu(fileName = "Landmark", menuName = "Fantasia/Landmark Definition")]
    public class LandmarkDefinition : ScriptableObject
    {
        public string LandmarkName;
        public string Category;
        [TextArea] public string Description;
        public Color AccentColor = Color.white;

        // 1-2 character placeholder "icon" until real art exists — color
        // alone isn't accessible/distinct enough (Slay the Spire's original
        // all-black icons are the cautionary example from the research doc).
        public string Badge = "?";
    }
}
