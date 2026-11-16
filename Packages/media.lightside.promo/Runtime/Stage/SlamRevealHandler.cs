using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Slams a glyph down onto its baseline: a short fall from above, stretched tall while it falls, flattened on
    /// impact and settled upright.
    /// </summary>
    /// <remarks>
    /// The stretch stands in for motion blur. The fall takes the first part of the glyph's arrival on a hard ease-out,
    /// so the glyph is already down while its neighbours are still in the air, and the squash plays on the frames it
    /// lands; the reveal reads as a word hitting the frame rather than fading onto it. Invisible at progress 0, as
    /// <see cref="GlyphReveal"/> requires.
    /// </remarks>
    public sealed partial class SlamRevealHandler : RevealHandler
    {
        private readonly float height;

        /// <summary>A slam from <paramref name="height"/> pixels above the baseline.</summary>
        public SlamRevealHandler(float height) => this.height = height;

        /// <inheritdoc/>
        public override void Apply(in RevealGlyphInfo info)
        {
            var t = info.Progress;
            var fall = Mathf.Clamp01(t / Fall);
            var down = 1f - (1f - fall) * (1f - fall) * (1f - fall);
            ApplyOffset(in info, 0f, height * (1f - down));

            var smear = (1f - down) * Smear;
            var impact = Mathf.Sin(Mathf.Clamp01((t - Fall) / (1f - Fall)) * Mathf.PI) * Squash;
            ApplyScale(in info, 1f - smear * 0.3f + impact, 1f + smear - impact, Baseline);
            ApplyFade(in info, Mathf.Clamp01(t / Appear));
        }

        private const float Fall = 0.55f;
        private const float Smear = 0.55f;
        private const float Squash = 0.16f;
        private const float Appear = 0.2f;
        private static readonly Vector2 Baseline = new Vector2(0.5f, 0f);
    }
}
