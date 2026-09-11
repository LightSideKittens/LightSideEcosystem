using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Mentions and hashtags appearing on their own out of ordinary typed text.
    /// </summary>
    /// <remarks>
    /// Nothing in the string is tagged. A <see cref="TriggerWordParseRule"/> claims every word starting with the
    /// trigger character, and the style behind it is a <see cref="CompositeModifier"/> — a chip, a text colour and an
    /// interactive range — so one entry in the inspector produces the whole treatment.
    /// <para>
    /// The rule fires only at a word start, which is why the e-mail address in the third line stays plain. That
    /// detail is the shot: a naive matcher chips the domain of every address in a chat log.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(MentionsSlide))]
    public sealed class MentionsSlide : Slide
    {
        [SerializeField] private string headline = "Chat that knows what a name is.";
        [SerializeField] private string sub = "Mentions, hashtags, anything you invent — one style each.";

        [SerializeField, TextArea(3, 8)]
        private string body =
            "Morning @alice and @bob_42 — the shaping pass by @charlie landed.\n\n" +
            "Tag the review with #unicode, #typography or #rtl.";

        private Claim claim;
        private Showcase panel;
        private RevealModifier typewriter;
        private Ledger recipe;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Chat", stage.Root, "No tags in this text — the rules find the words",
                body, new Vector2(-stage.Width * 0.18f, stage.ContentCentre),
                new Vector2(stage.Width * 0.56f, stage.ContentHeight * 0.94f),
                stage.ContentHeight * 0.085f, vertical: VerticalAlignment.Top);

            BindMention(theme);
            BindHashtag(stage);

            typewriter = stage.Typewriter(panel.Body, new FadeRevealHandler());

            recipe = stage.Ledger("Recipe", stage.Root, "One mention style", new[]
            {
                new LedgerEntry("TriggerWordParseRule", "\"@\"", "1", theme.Violet),
                new LedgerEntry("HighlightModifier", "the chip", "2", theme.Violet),
                new LedgerEntry("FillModifier", "the ink", "3", theme.Violet),
                new LedgerEntry("InteractiveModifier", "the tap", "4", theme.Violet),
                new LedgerEntry("Lines of code", "0", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            Cue("writeon", First);
            for (var i = 0; i < recipe.Count; i++) Cue("tick", Rows + i * Step);
        }

        private void BindMention(Theme theme)
        {
            var graph = new CompositeModifier();
            graph.Modifiers.ReplaceAll(new BaseModifier[]
            {
                new HighlightModifier
                {
                    Paint = PaintRef.Solid(Theme.Fade(theme.Violet, 0.42f)),
                    GeometryMapping = GeometryMapping.Range,
                    Height = RangeHeight.Content,
                    Padding = new UnitVector2(new Vector2(0.22f, 0.08f), UnitKind.Em),
                    CornerRadius = UnitValue.Em(0.32f),
                    Priority = RangeDecorationPriorities.Interactive
                },
                new FillModifier { Paint = PaintRef.Solid(theme.Text) },
                new InteractiveModifier()
            });

            panel.Body.Styles.Add(Style.FromSource(new TriggerWordParseRule("@"), graph));
        }

        private void BindHashtag(Stage stage)
        {
            var graph = new CompositeModifier();
            graph.Modifiers.ReplaceAll(new BaseModifier[]
            {
                stage.BrandFill(0f),
                new UnderlineModifier(),
                new InteractiveModifier()
            });

            panel.Body.Styles.Add(Style.FromSource(new TriggerWordParseRule("#"), graph));
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);
            typewriter.Front = UnitValue.Percent(GlyphReveal.Frontier.Window(local, First, WriteOn) * 100f);

            for (var i = 0; i < recipe.Count; i++)
                recipe.Pose(i, Ease.EmphasizedIn.Window(local, Rows + i * Step, RowIn));
        }

        private const float First = 0.3f;
        private const float WriteOn = 2.2f;
        private const float Rows = 1.4f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
