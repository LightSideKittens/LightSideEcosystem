using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A picture sitting in the middle of a sentence and being laid out as part of it.
    /// </summary>
    /// <remarks>
    /// The image is a glyph as far as layout is concerned: it takes an advance, it sits on a baseline, it wraps with
    /// the words around it, and a caret steps over it in one press. A quad parented beside the text would do none of
    /// that, which is why chat clients that fake inline media get a broken caret and a broken selection.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(InlineMediaSlide))]
    public sealed class InlineMediaSlide : Slide
    {
        [SerializeField] private string headline = "Drop a picture into a sentence.";
        [SerializeField] private string sub = "It wraps, it selects, and the caret steps over it.";

        /// <summary>The picture the sentence carries. Wired to a sample image when the rig is created.</summary>
        [SerializeField] private Sprite image;

        /// <summary>
        /// Three short lines with the picture on the middle one.
        /// </summary>
        /// <remarks>
        /// The words either side of the sprite are the same length so it lands on the frame's centre under a centred
        /// alignment. Its line is far taller than the other two — the entry reserves over three em above the
        /// baseline — and that difference is the shot: the line grew to hold it, rather than the picture being
        /// clipped to the line.
        /// </remarks>
        [SerializeField, TextArea(3, 8)]
        private string body =
            "A picture is a glyph.\n" +
            "Suleiman <sprite=cat> approves.\n" +
            "It wraps, selects and copies.";

        private Claim claim;
        private Showcase panel;
        private RevealModifier typewriter;
        private Ledger facts;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Inline", stage.Root, "<sprite=cat> — one tag, laid out as a glyph",
                body, new Vector2(-stage.Width * 0.18f, stage.ContentCentre),
                new Vector2(stage.Width * 0.56f, stage.ContentHeight * 0.94f),
                stage.ContentHeight * 0.085f, HorizontalAlignment.Center);

            if (image) BindSprite();
            typewriter = stage.Typewriter(panel.Body, new FadeRevealHandler());

            facts = stage.Ledger("Facts", stage.Root, "What inline really means", new[]
            {
                new LedgerEntry("Takes an advance", "yes", "✓", theme.Pass),
                new LedgerEntry("Wraps with the line", "yes", "✓", theme.Pass),
                new LedgerEntry("Caret steps over it", "one press", "✓", theme.Pass),
                new LedgerEntry("Included in a selection", "yes", "✓", theme.Pass),
                new LedgerEntry("Survives copy and paste", "yes", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            Cue("writeon", First);
            for (var i = 0; i < facts.Count; i++) Cue("tick", Rows + i * Step);
        }

        /// <summary>
        /// Registers the one sprite the sentence names, in a catalog the slide owns.
        /// </summary>
        /// <remarks>
        /// Inline rather than through a shared <see cref="UniTextSprites"/> asset: the reel must not depend on a
        /// catalog it did not author, and a project can hold several assets by that name.
        /// <para>
        /// The numbers are the ones authored for this picture in the sample's own sprite catalog, not measurements of
        /// the texture. A sprite's box, its advance and the room it claims above and below the line are a typographic
        /// fit to one image — where its subject sits inside its bounds, how much of it is transparent margin, how far
        /// it should ride above the baseline — and none of that is recoverable from the pixels.
        /// </para>
        /// </remarks>
        private void BindSprite()
        {
            var catalog = new InlineSpriteProvider();
            catalog.Entries.Add(new InlineSprite
            {
                Name = "cat",
                Sprite = image,
                PreserveAspect = true,
                Size = new Vector2(5.41f, 5.87f),
                BearingOffset = new Vector2(-0.04f, -0.73f),
                Advance = 3.54f,
                LineHeightAbove = 3.39f,
                LineHeightBelow = 1.14f
            });

            panel.Body.Styles.Add(Style.Tag(new SpriteModifier { Provider = catalog }, "sprite"));
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
