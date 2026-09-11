using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Opening lockup: the mark, the wordmark and one line under it, on the dark product surface.
    /// </summary>
    /// <remarks>
    /// <see cref="logo"/> is wired to the shipped mark when the rig is created. Left empty, the plate falls back to
    /// the brand ramp — a designed placeholder, not the finished lockup.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(TitleSlide))]
    public sealed class TitleSlide : Slide
    {
        [SerializeField] private Texture2D logo;
        [SerializeField] private string wordmark = "UniText Folio";
        [SerializeField] private string tagline = "Text for every writing system on Earth";

        private Widget mark;
        private Widget glow;
        private GlyphReveal titleReveal;
        private UniText caption;

        private readonly Ease rise = Ease.EmphasizedIn;
        private readonly Ease reveal = GlyphReveal.Frontier;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            var theme = stage.Theme;

            glow = stage.Shape("Glow", stage.Root, ShapeKind.Circle);
            stage.Box(glow.Rect, Vector2.zero, Vector2.one * (stage.Height * 1.02f));
            var pool = theme.Glow;
            Stage.Radial(glow.Fill, pool, Theme.Fade(pool, 0f), 1f);

            mark = stage.Shape("Mark", stage.Root, ShapeKind.RoundedRect, theme.RadiusXxl);
            stage.Box(mark.Rect, new Vector2(0f, stage.Height * 0.157f), Vector2.one * (stage.Height * 0.222f));
            if (logo) Stage.Textured(mark.Fill, logo);
            else Stage.Ramped(mark.Fill, theme.Brand, PaintProjectionKind.Radial);

            var title = stage.Label(stage.Root, wordmark, theme.Title, theme.Text, stretch: false);
            stage.Box(title.rectTransform, new Vector2(0f, -stage.Height * 0.028f),
                new Vector2(stage.Width * 0.73f, theme.Title * 1.55f));
            titleReveal = stage.Reveal(title, new SlideRevealHandler
            {
                Offset = new Vector2(0f, -theme.Title * 0.44f),
                Easing = Ease.Of(EasingType.CubicOut)
            });

            caption = stage.Caption(stage.Root, tagline);
            stage.Box(caption.rectTransform, new Vector2(0f, -stage.Height * 0.157f),
                new Vector2(stage.Width * 0.78f, theme.Body * 2.1f));
        }

        protected override void OnRender(float local)
        {
            var markIn = rise.Window(local, 0.05f, 0.7f);
            var titleIn = reveal.Window(local, 0.30f, 0.85f);
            var captionIn = rise.Window(local, 0.95f, 0.7f);

            Stage.Scale(mark.Rect, Mathf.LerpUnclamped(0.7f, 1f, markIn));
            Stage.Alpha(mark.Shape, markIn);

            Stage.Scale(glow.Rect, Mathf.LerpUnclamped(0.6f, 1f, markIn));
            Stage.Alpha(glow.Shape, markIn * 0.55f);

            titleReveal.Fill = titleIn;
            Stage.Alpha(caption, captionIn);
        }
    }
}
