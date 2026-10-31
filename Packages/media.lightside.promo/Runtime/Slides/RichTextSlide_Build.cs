using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Layout of <see cref="RichTextSlide"/>, and the four effects it demonstrates.</summary>
    public sealed partial class RichTextSlide
    {
        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            show = stage.Showcase("Panel", stage.Root, label, sentence,
                new Vector2(0f, stage.ContentCenter),
                new Vector2(stage.Width * 0.81f, stage.ContentHeight * 0.46f),
                theme.Head, HorizontalAlignment.Left);

            typewriter = stage.Reveal(show.Body, new SlideRevealHandler
            {
                Offset = new Vector2(0f, -theme.Head * 0.34f)
            });

            ApplyEffects(stage);

            var track = stage.Node("Progress", show.Rect);
            stage.Anchor(track, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, theme.PadXl * 0.45f), new Vector2(-theme.PadXl * 2f, theme.PadSm));
            bar = stage.Bar(track);
        }

        /// <summary>
        /// Each word wears the modifier that actually produces the thing its name claims.
        /// </summary>
        /// <remarks>
        /// <see cref="BoldModifier"/> at its default weight resolves to <c>max(700, base + 300)</c>, preferring a
        /// real bold face and synthesizing only when the resolved font has none — so the word is heavier even under
        /// the system-font cascade, where no bold cut may exist.
        /// </remarks>
        private void ApplyEffects(Stage stage)
        {
            var theme = stage.Theme;

            Stage.StyleWord(show.Body, boldWord, new BoldModifier());
            Stage.StyleWord(show.Body, colorWord, new ColorModifier { Color = theme.Coral });
            Stage.StyleWord(show.Body, gradientWord, stage.BrandFill());

            if (linkPreset) Stage.StyleWord(show.Body, linkWord, new ModifierGraphModifier { Preset = linkPreset });
        }
    }
}
