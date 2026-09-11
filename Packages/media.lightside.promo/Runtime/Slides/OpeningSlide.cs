using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The cold open: what the viewer is about to watch, and what it was made of.
    /// </summary>
    /// <remarks>
    /// Four paragraphs, arriving one at a time. The first is the sentence; the three under it are the claim, and each
    /// leads with its figure so the eye lands on the number before it reads the noun.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(OpeningSlide))]
    public sealed class OpeningSlide : Slide
    {
        [SerializeField, TextArea(2, 4)]
        private string intro = "This presentation was made entirely in the Unity Editor\nwith UniShapes and UniText";

        [SerializeField] private string[] claims = { "0 Textures", "0 Fonts", "1 Draw Call" };

        private readonly Ease enter = Ease.EmphasizedIn;

        private UniText lead;
        private GlyphReveal typing;
        private UniText[] lines;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            var step = theme.Title * 1.32f;
            var block = theme.Lead * 2.6f + claims.Length * step;
            var top = block * 0.5f;

            lead = stage.Label(stage.Root, intro, theme.Lead, theme.TextSoft, stretch: false);
            stage.Box(lead.rectTransform, new Vector2(0f, top - theme.Lead * 1.3f),
                new Vector2(stage.Width * 0.94f, theme.Lead * 2.6f));

            Stage.StyleWord(lead, "UniShapes", new BoldModifier());
            Stage.StyleWord(lead, "UniText", new BoldModifier());
            typing = stage.Reveal(lead, new FadeRevealHandler(), Keys);

            lines = new UniText[claims.Length];
            for (var i = 0; i < claims.Length; i++)
            {
                var y = top - theme.Lead * 2.6f - step * (i + 0.5f);
                lines[i] = stage.Label(stage.Root, claims[i], theme.Title, theme.Text, stretch: false,
                    face: theme.DisplayFace);
                stage.Box(lines[i].rectTransform, new Vector2(0f, y),
                    new Vector2(stage.Width * 0.86f, step));
                lines[i].WordWrap = false;

                Stage.StyleWord(lines[i], Stage.LeadingFigure(claims[i]), stage.BrandFill(15f));
            }

            Cue("intro", First);
            for (var i = 0; i < lines.Length; i++) Cue("claim", Claims + i * Beat);

            Seconds = Mathf.Max(Seconds, Claims + (claims.Length - 1) * Beat + Fade + Hold);
        }

        protected override void OnRender(float local)
        {
            typing.Fill = GlyphReveal.Frontier.Window(local, First, Typing);

            for (var i = 0; i < lines.Length; i++)
            {
                var t = enter.Window(local, Claims + i * Beat, Fade);
                Stage.Alpha(lines[i], t);
                Stage.Scale(lines[i].rectTransform, Mathf.LerpUnclamped(0.92f, 1f, t));
            }
        }

        private const float First = 0.25f;

        /// <summary>
        /// How long the opening sentence takes to type itself out.
        /// </summary>
        /// <remarks>
        /// The reveal does not collapse, and on a centred line it must not. Collapsed text is removed from layout, so
        /// every character typed re-centres the whole sentence and the words slide out from under the eye; here the
        /// line is laid out once and the glyphs simply arrive where they already are.
        /// </remarks>
        private const float Typing = 1.3f;

        /// <summary>Glyphs mid-arrival at once. Near one it reads as a keystroke rather than as a wave.</summary>
        private const float Keys = 1.6f;

        private const float Claims = 1.9f;
        private const float Beat = 0.55f;
        private const float Fade = 0.55f;

        /// <summary>
        /// How long the finished card is held after the last line settles.
        /// </summary>
        /// <remarks>
        /// Generous, and the first slide of a reel has to be. It is the only frame a viewer meets with no context to
        /// carry into it, and three claims cannot be read in the beat that suits a shot arriving mid-argument.
        /// </remarks>
        private const float Hold = 2.2f;
    }
}
