using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The text section of the UniGUI reel: every language, every emoji and every style, one beat each, all in
    /// controls a game would ship.
    /// </summary>
    /// <remarks>
    /// It opens from the white flash the shapes section dives into. Every word on screen is UniText: scripts the
    /// section's fonts lack fall back to the system's own, which is the point of the first beat.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiTextScene))]
    public sealed partial class GuiTextScene : GuiSection
    {
        [SerializeField] private string lead = "Every";

        /// <summary>The headline's last word for each beat in order: languages, emoji, styles.</summary>
        [SerializeField] private string[] words = { "language.", "emoji.", "style." };

        [SerializeField] private string promise = "Real text in every control.";

        private static readonly Link[] links = { Link.Rise, Link.Whip, Link.Whip, Link.Whip };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildParts(Stage stage)
        {
            var parts = new[] { BuildLanguages(stage), BuildChat(stage), BuildStyles(stage) };
            foreach (var part in parts)
            {
                part.Magnify = Magnify;
                part.MagnifyAbout = new Vector2(0f, contentCenter);
            }

            return parts;
        }

        /// <summary>How much larger than built every beat of the section stands on screen.</summary>
        private const float Magnify = 1.3f;

        /// <summary>
        /// A label of <paramref name="text"/> hung from <paramref name="parent"/>'s middle by the point
        /// <paramref name="edge"/> of its box, at <paramref name="at"/>, <paramref name="box"/> large.
        /// </summary>
        private UniText Text(Stage stage, Transform parent, string text, float size, Color color, HorizontalAlignment align,
            Vector2 at, Vector2 box, float edge, UniTextFont face = null)
        {
            var label = stage.Label(parent, text, size, color, align, stretch: false, face: face);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.sizeDelta = box;
            rect.anchoredPosition = at;
            return label;
        }
    }
}
