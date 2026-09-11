using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The typography a document needs and a game engine never has: ruby, lists, small caps and real scripts.
    /// </summary>
    /// <remarks>
    /// Ruby is the one worth the seconds. Japanese annotation sits above its base text, centres over the run it
    /// annotates, and pushes the line above it out of the way — three rules a superscript cannot satisfy, and the
    /// reason a project that needs furigana normally leaves the engine behind.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(TypographySlide))]
    public sealed class TypographySlide : Slide
    {
        [SerializeField] private string headline = "Typography that finishes the job.";
        [SerializeField] private string sub = "Ruby, lists, small caps, superscript — every one a tag.";

        [SerializeField, TextArea(4, 10)]
        private string body =
            "<ruby>漢字<rt>かんじ</rt></ruby>に<ruby>振<rt>ふ</rt></ruby>り<ruby>仮名<rt>がな</rt></ruby>。\n\n" +
            "<li>Ordered and nested lists</li>\n" +
            "<li>Indents, paragraph spacing, line height</li>\n" +
            "<li>Thai and Lao word breaking, with no spaces to go on</li>\n\n" +
            "<smallcaps>Small caps</smallcaps> · H<sub>2</sub>O · x<sup>2</sup> · <upper>upper</upper>";

        /// <summary>
        /// Point size of the body, held rather than fitted.
        /// </summary>
        /// <remarks>
        /// Every line in the well is a different feature at a different size — ruby above the type, a list beside
        /// its markers, a subscript below the baseline. Auto-fit picks one size for the block, so it is the ruby's
        /// headroom or the superscript's rise that decides how large every other line comes out; holding the size
        /// puts that back where it was authored.
        /// </remarks>
        [SerializeField, Min(1f)] private float bodySize = 50f;

        private Claim claim;
        private Showcase panel;
        private RevealModifier typewriter;
        private Ledger facts;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Typography", stage.Root, "Every line below is one tag",
                body, new Vector2(-stage.Width * 0.18f, stage.ContentCentre),
                new Vector2(stage.Width * 0.56f, stage.ContentHeight * 0.94f),
                bodySize);

            panel.Body.AutoSize = false;
            panel.Body.FontSize = bodySize;

            panel.Body.Styles.Add(Style.FromSource(new RubyParseRule(), new RubyModifier()));
            panel.Body.Styles.Add(Style.Tag(new ListModifier(), "li"));
            panel.Body.Styles.Add(Style.Tag(new SmallCapsModifier(), "smallcaps"));
            panel.Body.Styles.Add(Style.Tag(new UppercaseModifier(), "upper"));
            panel.Body.Styles.Add(Style.Tag(new ScriptPositionModifier(), "sup", "super"));
            panel.Body.Styles.Add(Style.Tag(new ScriptPositionModifier(), "sub", "sub"));

            typewriter = stage.Typewriter(panel.Body, new FadeRevealHandler());

            facts = stage.Ledger("Facts", stage.Root, "What the layout knows", new[]
            {
                new LedgerEntry("Ruby clears the line above", "yes", "✓", theme.Pass),
                new LedgerEntry("Thai, Lao, Khmer, Burmese", "yes", "✓", theme.Pass),
                new LedgerEntry("Korean line breaking", "yes", "✓", theme.Pass),
                new LedgerEntry("Justification", "script-aware", "✓", theme.Pass),
                new LedgerEntry("Arabic kashida", "yes", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            Cue("writeon", First);
            for (var i = 0; i < facts.Count; i++) Cue("tick", Rows + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);
            typewriter.Front = UnitValue.Percent(GlyphReveal.Frontier.Window(local, First, WriteOn) * 100f);

            for (var i = 0; i < facts.Count; i++)
                facts.Pose(i, Ease.EmphasizedIn.Window(local, Rows + i * Step, RowIn));
        }

        private const float First = 0.3f;
        private const float WriteOn = 2.1f;
        private const float Rows = 1.4f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
