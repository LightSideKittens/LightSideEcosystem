using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A covered phrase, a finger on it, and the words underneath.
    /// </summary>
    /// <remarks>
    /// The uncovering is the shipped component's own state, set through <see cref="SpoilerModifier.SetRevealed"/> at
    /// the moment the pointer's own timeline says the press lands, and cleared by
    /// <see cref="SpoilerModifier.ConcealAll"/> when the reel seeks backwards.
    /// <para>
    /// The cover's position is <em>measured</em>, never typed: the phrase before it is measured through the text
    /// component and the point falls out of that width. An aim guessed at an average advance drifts by whole words
    /// over a sentence, which is far enough to press next to the thing being pressed.
    /// </para>
    /// <para>
    /// Nothing declares the hand. A <see cref="CursorType.Link"/> region is registered over the cover, and the
    /// pointer asks the scene what it is above — the same mechanism that turns it into a beam over a text field.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(SpoilersSlide))]
    public sealed class SpoilersSlide : Slide
    {
        [SerializeField] private string headline = "Hide it until they ask.";
        [SerializeField] private string sub = "One tag, one tap. The cover follows every wrap.";

        [SerializeField] private string lead = "The killer is ";
        [SerializeField] private string secret = "the butler";
        [SerializeField] private string tail = ", obviously.";

        private Claim claim;
        private Showcase panel;
        private RevealModifier typewriter;
        private SpoilerModifier spoiler;
        private Pointer pointer;
        private Ledger recipe;

        /// <summary>How wide the covered phrase is, taken while the body could still be measured.</summary>
        private float coverWidth;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Spoilers", stage.Root, "<spoiler> — the whole setup",
                $"{lead}<spoiler>{secret}</spoiler>{tail}",
                new Vector2(-stage.Width * 0.18f, stage.ContentCentre),
                new Vector2(stage.Width * 0.56f, stage.ContentHeight * 0.62f),
                stage.ContentHeight * 0.085f, HorizontalAlignment.Left, VerticalAlignment.Top);

            spoiler = new SpoilerModifier();
            panel.Body.Styles.Add(Style.Tag(spoiler, "spoiler"));

            var target = Cover(stage);
            typewriter = stage.Typewriter(panel.Body, new FadeRevealHandler());

            recipe = stage.Ledger("Recipe", stage.Root, "What a spoiler is made of", new[]
            {
                new LedgerEntry("SpoilerModifier", "one tag", "1", theme.Violet),
                new LedgerEntry("Cover", "a highlight", "2", theme.Violet),
                new LedgerEntry("State", "on identity", "3", theme.Violet),
                new LedgerEntry("Follows a wrap", "yes", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            pointer = stage.Pointer(stage.At(0.18f, 0.12f), new[]
            {
                Beat.To(target, targetWidth: coverWidth),
                Beat.Click("tap"),
                Beat.Wait(0.9f),
                Beat.To(stage.At(0.16f, 0.1f))
            });

            Script.Rewind(() => spoiler.ConcealAll())
                .At(Aim + pointer.Timeline.Mark("tap"), null, Uncover);

            Cue("writeon", First);
            Cue(pointer.Timeline.Cues(), Aim);
            for (var i = 0; i < recipe.Count; i++) Cue("tick", Rows + i * Step);
        }

        /// <summary>
        /// The cover's centre, and a <see cref="CursorType.Link"/> region over it so the pointer wears a hand.
        /// </summary>
        /// <remarks>
        /// Both offsets are measured. The body is aligned to the top of its rect, so the covered phrase sits half a
        /// line box below the rect's top edge and that far along the line — neither figure survives a change of
        /// alignment, face or size unless it is asked for rather than assumed.
        /// <para>
        /// Called before the typewriter is attached, and it has to be. A collapsing reveal at
        /// <see cref="RevealModifier.Fill"/> zero removes every cluster from shaping and layout, so the text has no
        /// extent to measure and the answer is zero — a point at the element's left edge, which looks like a
        /// position rather than a refusal.
        /// </para>
        /// </remarks>
        private Vector2 Cover(Stage stage)
        {
            coverWidth = Width(secret);

            var rect = panel.Body.rectTransform;
            var line = Stage.LineBox(panel.Body);
            var local = new Vector2(
                -rect.rect.width * 0.5f + Width(lead) + coverWidth * 0.5f,
                rect.rect.height * 0.5f - line * 0.5f);
            var point = (Vector2)stage.Root.InverseTransformPoint(rect.TransformPoint(local));

            var hit = stage.Node("Cover", stage.Root);
            stage.Box(hit, point, new Vector2(coverWidth, line));
            stage.Cursors.Add(hit, CursorType.Link);
            return point;
        }

        private float Width(string text) => Stage.Advance(panel.Body, text);

        private void Uncover()
        {
            var ranges = spoiler.InteractiveRanges;
            if (ranges.Length > 0) spoiler.SetRevealed(ranges[0], true);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);
            typewriter.Front = UnitValue.Percent(GlyphReveal.Frontier.Window(local, First, WriteOn) * 100f);
            pointer.Pose(local - Aim);

            for (var i = 0; i < recipe.Count; i++)
                recipe.Pose(i, Ease.EmphasizedIn.Window(local, Rows + i * Step, RowIn));
        }

        private const float First = 0.3f;
        private const float WriteOn = 1.4f;

        /// <summary>When the pointer starts travelling — after the sentence it is going to press has finished arriving.</summary>
        private const float Aim = 1.9f;

        private const float Rows = 1.1f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
