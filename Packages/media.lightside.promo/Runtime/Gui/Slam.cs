using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>A display line slammed in glyph by glyph, as the reel's headlines land.</summary>
    public static class Slam
    {
        /// <summary>
        /// A centered line of <paramref name="text"/> in the display face at <paramref name="at"/> under
        /// <paramref name="parent"/>, painted with the brand ramp when <paramref name="branded"/> and in
        /// <paramref name="color"/> otherwise, and the reveal that slams it in.
        /// </summary>
        /// <remarks>The line is laid out once at its final width, so landing glyphs never shift the ones already down.</remarks>
        public static GlyphReveal Line(Stage stage, Transform parent, string text, float size, Vector2 at, bool branded,
            Color color)
        {
            var label = stage.Label(parent, text, size, color, stretch: false, face: stage.Theme.DisplayFace);
            label.WordWrap = false;
            stage.Box(label.rectTransform, at, new Vector2(stage.Width * 1.5f, size * 1.5f));
            if (branded) label.Styles.Add(Style.Range(stage.BrandFill(), 0, text.Length));
            return stage.Reveal(label, 0, text.Length, new SlamRevealHandler(size * Height), InFlight);
        }

        /// <summary>How far above its place a glyph starts its fall, per unit of the line's size.</summary>
        private const float Height = 0.76f;

        /// <summary>How many glyphs are falling at once: enough that a word lands nearly as one.</summary>
        private const float InFlight = 14f;
    }
}
