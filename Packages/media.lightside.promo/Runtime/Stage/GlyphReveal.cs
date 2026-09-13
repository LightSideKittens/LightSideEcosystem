using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Per-glyph reveal of one text, played by a <see cref="RevealHandler"/> over several glyphs at once.
    /// </summary>
    /// <remarks>
    /// The envelope is this object's alone. The modifier stays fully filled and hides nothing, so every covered glyph
    /// is drawn on every frame and owes its appearance entirely to the handler — which must therefore resolve to
    /// invisible at progress 0. Every shipped handler fades in from nothing; one built with its fade off shows the
    /// whole line at once.
    /// <para>
    /// <see cref="RevealModifier.Front"/> cannot drive this. Its frontier both gates visibility and stops at the glyph
    /// count, while a glyph at ordinal <c>k</c> keeps animating until the frontier reaches <c>k + spread</c>: wide
    /// enough to finish the last glyph and the tail is visible before it starts moving, tight enough to gate
    /// visibility and the last <c>spread</c> glyphs never finish.
    /// </para>
    /// <para>
    /// A class rather than a closure: <c>RevealGlyphHandler</c> takes its argument by <c>in</c>, which a lambda
    /// cannot infer, and a captured closure would allocate per subscription on a per-glyph path.
    /// </para>
    /// </remarks>
    public sealed class GlyphReveal
    {
        private readonly UniText text;
        private readonly RevealHandler handler;
        private readonly float spread;
        private float fill;

        internal GlyphReveal(UniText text, RevealHandler handler, float spread)
        {
            this.text = text;
            this.handler = handler;
            this.spread = Mathf.Max(1f, spread);
        }

        /// <summary>The curve a reveal frontier advances on: linear.</summary>
        /// <remarks>
        /// A frontier is a counter, not a body in motion. Easing one compresses the letters into the first moments
        /// and then crawls, so the last glyph of a line lands long after the eye has stopped waiting for it — the
        /// same curve that reads as poised on a moving panel reads as a stall on a line of text. Let the frontier
        /// advance evenly and give each glyph its character through its own reveal handler.
        /// </remarks>
        public static Ease Frontier => Ease.Linear;

        /// <summary>Revealed fraction of the text, 0..1: 0 shows nothing, 1 leaves every glyph settled.</summary>
        public float Fill
        {
            get => fill;
            set
            {
                var clamped = Mathf.Clamp01(value);
                if (fill == clamped) return;
                fill = clamped;
                text.SetDirty(UniTextDirty.Mesh);
            }
        }

        /// <summary>
        /// Restates the glyph's arrival over <see cref="spread"/> ordinals and hands it to the handler.
        /// </summary>
        /// <remarks>
        /// The frontier runs to <c>count + spread - 1</c> so the last glyph completes exactly at
        /// <see cref="Fill"/> = 1. The restated frontier reaches the handler through
        /// <see cref="RevealGlyphInfo.WithFront"/>, which re-derives progress as <c>front - ordinal</c>
        /// and so stretches each glyph's arrival over <see cref="spread"/> ordinals.
        /// </remarks>
        internal void Apply(in RevealGlyphInfo info)
        {
            var front = fill * (info.count + spread - 1f);

            handler.Apply(info.WithFront(info.ordinal + (front - info.ordinal) / spread));
        }
    }
}
