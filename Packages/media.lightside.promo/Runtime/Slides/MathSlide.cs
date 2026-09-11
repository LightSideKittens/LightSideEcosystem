using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Real mathematical typesetting: fractions, radicals, limits and a matrix, laid out from OpenType MATH metrics.
    /// </summary>
    /// <remarks>
    /// The formulas are set, not drawn. Rule thickness, radical clearance, the gap above and below a fraction bar and
    /// the size of each script level all come from the font's own MATH table, and the delimiters are assembled from
    /// glyph variants rather than scaled — which is why they match the height of what they enclose instead of
    /// stretching out of shape.
    /// <para>
    /// Assign <see cref="mathFont"/> to a face that carries a MATH table. An ordinary text face has none, and the
    /// layout has nothing to derive its spacing from.
    /// </para>
    /// <para>
    /// The formulas do not write on. A collapsing reveal would re-lay each one at every frontier position — a
    /// fraction reflowing around a numerator it does not yet have reads as a fault, not as typing.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(MathSlide))]
    public sealed class MathSlide : Slide
    {
        [SerializeField] private string headline = "Yes, it sets mathematics.";
        [SerializeField] private string sub = "OpenType MATH — the same machinery TeX uses.";

        /// <summary>A face carrying a MATH table. The sample ships STIX Two Math.</summary>
        [SerializeField] private UniTextFont mathFont;

        /// <summary>
        /// Four formulas in two rows.
        /// </summary>
        /// <remarks>
        /// Paired across the line rather than stacked down it. A formula is wide and short, so a column of them
        /// spends the frame's height and leaves its width empty — and the size a display formula can be set at is
        /// decided by whichever runs out first.
        /// </remarks>
        [SerializeField, TextArea(4, 10)]
        private string body =
            "<math>x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}</math>    " +
            "<math>e^{i\\pi} + 1 = 0</math>\n" +
            "<math>\\sum_{i=1}^{n} i = \\frac{n(n+1)}{2}</math>    " +
            "<math>\\int_0^{\\infty} e^{-x^2} dx = \\frac{\\sqrt{\\pi}}{2}</math>";

        [SerializeField] private string payoff = "Bars, radicals, script sizes and delimiters — all from the font.";

        private Claim claim;
        private Claim note;
        private Showcase panel;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, sub);
            note = stage.Claim(stage.Root, payoff, top: false);

            panel = stage.Showcase("Math", stage.Root, "<math> … </math>",
                body, stage.ContentHeight * 0.16f, widthFraction: 0.94f, heightFraction: 0.98f,
                horizontal: HorizontalAlignment.Center);

            panel.Body.Styles.Add(Style.FromSource(new MathParseRule(), new MathModifier
            {
                MathFont = mathFont,
                Mode = MathMode.Display
            }));

            Cue("formulas", 0.3f);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);
            note.Pose(local - Payoff);
        }

        private const float Payoff = 1.2f;
    }
}
