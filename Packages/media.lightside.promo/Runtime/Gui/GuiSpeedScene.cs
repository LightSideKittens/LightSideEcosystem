using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The speed beat of the UniGUI reel: measured GPU time of the same looks against a popular UI kit, each look a pair
    /// of bars growing side by side — the other kit's to full width, UniGUI's to its share of it — and the ratio slammed
    /// in at the end of the row.
    /// </summary>
    /// <remarks>
    /// The figures are ratios of GPU time measured on one phone (Mali-G76, Vulkan) drawing thirty equal cards of each look
    /// with the same appearance; absolute times are not shown, because the phone's clock state shifts them while the
    /// ratios hold. The other kit stays unnamed.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiSpeedScene))]
    public sealed class GuiSpeedScene : Slide
    {
        [SerializeField] private string title = "Fast.";
        [SerializeField] private string subtitle = "GPU time for the same look";
        [SerializeField] private string footnote =
            "30 equal 320×160 cards · Mali-G76 phone · Vulkan · against a popular Asset Store UI kit · shorter is faster";

        [SerializeField] private string[] looks = { "Card", "Fill", "Gradient", "Inner shadow" };
        [SerializeField] private float[] ratios = { 7.2f, 17f, 11f, 122f };

        private Impacts impacts;
        private GlyphReveal titleReveal;
        private UniText subtitleLabel;
        private UniText footnoteLabel;
        private RectTransform[] theirs;
        private RectTransform[] ours;
        private GlyphReveal[] verdicts;
        private UniText[] names;
        private readonly UniShape[] swatches = new UniShape[2];
        private readonly UniText[] keys = new UniText[2];
        private float barWidth;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            var glow = stage.Shape("Glow", stage.Root, ShapeKind.Ellipse);
            stage.Box(glow.Rect, new Vector2(0f, stage.Height * 0.3f), new Vector2(stage.Width, stage.Height * 1.1f));
            Stage.Radial(glow.Fill, Theme.Fade(theme.Orange, GlowStrength), Theme.Fade(theme.Orange, 0f));

            var top = stage.Height * 0.5f;
            titleReveal = Slam.Line(stage, stage.Root, title, TitleSize, new Vector2(0f, top - TitleDrop), true, theme.Text);
            subtitleLabel = stage.Label(stage.Root, subtitle, SubtitleSize, theme.TextSoft, stretch: false);
            stage.Box(subtitleLabel.rectTransform, new Vector2(0f, top - TitleDrop - TitleSize * 0.85f),
                new Vector2(stage.Width, SubtitleSize * 1.6f));

            barWidth = stage.Width * BarShare;
            var barLeft = -stage.Width * 0.5f + stage.Width * LabelShare;
            var them = Theme.Mix(theme.Surface, theme.Coral, MutedMix);
            theirs = new RectTransform[looks.Length];
            ours = new RectTransform[looks.Length];
            verdicts = new GlyphReveal[looks.Length];
            names = new UniText[looks.Length];
            impacts = new Impacts(stage, stage.Root);

            Legend(stage, 0, new Vector2(barLeft, RowTop + LegendRise), "UniGUI", null, theme);
            Legend(stage, 1, new Vector2(barLeft + LegendStep, RowTop + LegendRise), "Popular UI kit", them, theme);

            for (var i = 0; i < looks.Length; i++)
            {
                var y = RowTop - i * RowStep;
                names[i] = stage.Label(stage.Root, looks[i], NameSize, theme.Text, HorizontalAlignment.Right, stretch: false,
                    face: theme.DisplayFace);
                Hang(names[i].rectTransform, new Vector2(barLeft - NameGap, y), 1f, new Vector2(stage.Width * 0.3f, NameSize * 1.5f));

                var other = stage.Shape("Theirs", stage.Root, ShapeKind.Capsule);
                Stage.Solid(other.Fill, them);
                theirs[i] = other.Rect;
                Hang(theirs[i], new Vector2(barLeft, y + BarGap * 0.5f + BarHeight * 0.5f), 0f, new Vector2(0f, BarHeight));

                var mine = stage.Shape("Ours", stage.Root, ShapeKind.Capsule);
                Stage.Ramped(mine.Fill, theme.Brand, PaintProjectionKind.Linear, 0f);
                ours[i] = mine.Rect;
                Hang(ours[i], new Vector2(barLeft, y - BarGap * 0.5f - BarHeight * 0.5f), 0f, new Vector2(0f, BarHeight));

                var verdict = Ratio(ratios[i]);
                verdicts[i] = Slam.Line(stage, stage.Root, verdict, VerdictSize,
                    new Vector2(barLeft + barWidth + VerdictGap + VerdictSize * verdict.Length * 0.28f, y), true, theme.Text);
                var land = RowAt(i) + GrowFor;
                impacts.Hit(land, new Vector2(barLeft + barWidth + VerdictGap + VerdictSize * 0.6f, y), VerdictBurst);
                Cue("verdict", land);
            }

            footnoteLabel = stage.Label(stage.Root, footnote, FootSize, theme.TextSoft, stretch: false);
            stage.Box(footnoteLabel.rectTransform, new Vector2(0f, -top + FootRise), new Vector2(stage.Width, FootSize * 1.6f));

            Seconds = RowAt(looks.Length - 1) + GrowFor + Hold;
        }

        private static string Ratio(float ratio) => ratio >= 10f ? $"{ratio:0}×" : $"{ratio:0.0}×";

        private static float RowAt(int row) => RowsAt + row * RowStagger;

        private void Legend(Stage stage, int key, Vector2 at, string text, Color? solid, Theme theme)
        {
            var swatch = stage.Shape("Swatch", stage.Root, ShapeKind.Capsule);
            swatches[key] = swatch.Shape;
            Hang(swatch.Rect, at, 0f, new Vector2(SwatchWidth, BarHeight * 0.6f));
            if (solid.HasValue) Stage.Solid(swatch.Fill, solid.Value);
            else Stage.Ramped(swatch.Fill, theme.Brand, PaintProjectionKind.Linear, 0f);
            var label = stage.Label(stage.Root, text, LegendSize, theme.TextSoft, HorizontalAlignment.Left, stretch: false);
            Hang(label.rectTransform, at + new Vector2(SwatchWidth + 12f, 0f), 0f, new Vector2(400f, LegendSize * 1.6f));
            keys[key] = label;
        }

        private static void Hang(RectTransform rect, Vector2 at, float edge, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
        }

        protected override void OnRender(float local)
        {
            titleReveal.Fill = GlyphReveal.Frontier.Window(local, TitleAt, TitleSlam);
            Stage.Alpha(subtitleLabel, Ease.Emphasized.Window(local, TitleAt + 0.2f, 0.4f));
            Stage.Alpha(footnoteLabel, Ease.Emphasized.Window(local, RowAt(0), 0.5f));
            var legend = Ease.Emphasized.Window(local, RowAt(0) - 0.15f, 0.35f);
            for (var i = 0; i < keys.Length; i++)
            {
                Stage.Alpha(swatches[i], legend);
                Stage.Alpha(keys[i], legend);
            }

            for (var i = 0; i < looks.Length; i++)
            {
                var grow = Ease.Emphasized.Window(local, RowAt(i), GrowFor);
                Stage.Alpha(names[i], Ease.Emphasized.Window(local, RowAt(i), 0.25f));
                theirs[i].sizeDelta = new Vector2(Mathf.Max(BarHeight, barWidth * grow), BarHeight);
                theirs[i].gameObject.SetActive(local >= RowAt(i));
                ours[i].sizeDelta = new Vector2(Mathf.Max(BarHeight, barWidth / ratios[i] * grow), BarHeight);
                ours[i].gameObject.SetActive(local >= RowAt(i));
                verdicts[i].Fill = GlyphReveal.Frontier.Window(local, RowAt(i) + GrowFor * 0.8f, VerdictSlam);
            }

            Content.anchoredPosition = impacts.Pose(local);
        }

        private const float GlowStrength = 0.08f;
        private const float TitleSize = 150f;
        private const float TitleDrop = 150f;
        private const float SubtitleSize = 32f;
        private const float TitleAt = 0.15f;
        private const float TitleSlam = 0.28f;
        private const float BarShare = 0.48f;
        private const float LabelShare = 0.285f;
        private const float MutedMix = 0.35f;
        private const float RowTop = 60f;
        private const float RowStep = 118f;
        private const float BarHeight = 26f;
        private const float BarGap = 10f;
        private const float NameSize = 40f;
        private const float NameGap = 40f;
        private const float VerdictSize = 72f;
        private const float VerdictGap = 40f;
        private const float VerdictBurst = 130f;
        private const float VerdictSlam = 0.26f;
        private const float LegendRise = 86f;
        private const float LegendStep = 230f;
        private const float LegendSize = 22f;
        private const float SwatchWidth = 44f;
        private const float FootSize = 22f;
        private const float FootRise = 70f;
        private const float RowsAt = 0.8f;
        private const float RowStagger = 0.55f;
        private const float GrowFor = 0.55f;
        private const float Hold = 1.6f;
    }
}
