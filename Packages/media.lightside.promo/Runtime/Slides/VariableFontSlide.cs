using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// One variable font file, its weight axis swept live past the handful of cuts a static family would ship.
    /// </summary>
    /// <remarks>
    /// The ticks are the argument, not the slider. A viewer who sees only a word growing bolder reads it as bold,
    /// which every engine has; what they have to see is the handle standing <em>between</em> two named cuts, on a
    /// value no static cut provides.
    /// <para>
    /// The readout states the axis value and nothing else. A line that editorialises about the value — naming what
    /// is missing, or what the value is not — reads as a diagnostic the moment it appears where a number was, and a
    /// shot cannot carry a sentence a viewer can mistake for an error.
    /// </para>
    /// <para>
    /// Neither TextMeshPro nor UI Toolkit exposes a variation axis. TMP's shipped source carries no axis API at all,
    /// and the whole variable-font surface of TextCore — which UI Toolkit's Advanced Text Generator renders through —
    /// is the single predicate <c>IsVariableFontFace</c>. Both select from cuts baked in advance.
    /// </para>
    /// <para>
    /// The sweep writes the axis unrounded, every frame. An axis change costs a re-parse, a re-shape and a
    /// re-rasterization, which is worth knowing before animating one in a game — but the reel is captured by
    /// stepping the playhead, where there is no frame budget to protect, and a shot arguing that the axis is
    /// continuous must not step it.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(VariableFontSlide))]
    public sealed class VariableFontSlide : Slide
    {
        [SerializeField] private string headline = "Every weight, and everything between.";
        [SerializeField] private string sub = "One variable font, its wght axis driven live.";
        [SerializeField] private string word = "Weight";
        [SerializeField] private string title = "wght 100–900 · every value in between";

        /// <summary>A face carrying a <c>wght</c> axis. The sample ships Noto Sans variable.</summary>
        [SerializeField] private UniTextFont variableFont;

        /// <summary>
        /// The cuts a static family ships, at the axis values they sit on.
        /// </summary>
        /// <remarks>
        /// Six weights, and nothing at all in the gaps between them. They are drawn as ticks under the track so the
        /// handle is seen to leave them behind rather than to step from one to the next.
        /// </remarks>
        private static readonly (float weight, string name)[] Cuts =
        {
            (100f, "Thin"),
            (300f, "Light"),
            (400f, "Regular"),
            (500f, "Medium"),
            (700f, "Bold"),
            (900f, "Black")
        };

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private Widget panel;
        private CanvasGroup group;
        private UniText specimen;
        private UniText readout;
        private VariationModifier variation;
        private RectTransform fill;
        private RectTransform knob;
        private Ledger facts;

        private float trackWidth;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            var width = stage.Width * 0.56f;
            panel = stage.Panel("Axis", stage.Root);
            stage.Box(panel.Rect, new Vector2(-stage.Width * 0.18f, stage.ContentCentre),
                new Vector2(width, stage.ContentHeight * 0.94f));
            group = Stage.Group(panel.Rect);

            var head = stage.Label(panel.Rect, title, theme.Body, theme.TextSoft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -theme.PadXl), new Vector2(-theme.PadXl * 2f, theme.Body * 1.5f));
            head.WordWrap = false;

            specimen = stage.Label(panel.Rect, word, stage.ContentHeight * 0.27f, theme.Text);
            stage.Anchor(specimen.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, theme.PadXl * 1.4f), new Vector2(-theme.PadXl * 2f, -theme.PadXl * 6.4f));
            specimen.WordWrap = false;
            if (variableFont) specimen.Font = variableFont;

            variation = new VariationModifier { Weight = UnitValue.Absolute(Light) };
            specimen.Styles.Add(Style.WholeText(variation));

            trackWidth = width - theme.PadXl * 2f;
            BuildSlider(stage, theme);

            facts = stage.Ledger("Facts", stage.Root, "What the axis gives you", new[]
            {
                new LedgerEntry("Font assets", "1", "✓", theme.Pass),
                new LedgerEntry("Weights", "any 100–900", "✓", theme.Pass),
                new LedgerEntry("Set at runtime", "yes", "✓", theme.Pass),
                new LedgerEntry("TextMeshPro", "no axis", "✗", theme.Coral),
                new LedgerEntry("UI Toolkit", "no axis", "✗", theme.Coral)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            Cue("sweep", First);
            for (var i = 0; i < facts.Count; i++) Cue("tick", Rows + i * Step);
        }

        /// <summary>The track, the cuts marked along it, the knob that rides between them, and the live value.</summary>
        private void BuildSlider(Stage stage, Theme theme)
        {
            var track = stage.Node("Track", panel.Rect);
            stage.Anchor(track, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, theme.PadXl * 3.6f), new Vector2(-theme.PadXl * 2f, theme.PadSm));
            fill = stage.Bar(track);

            for (var i = 0; i < Cuts.Length; i++)
            {
                var at = (Cuts[i].weight - Light) / (Black - Light);
                var mark = stage.Shape("Cut" + i, track, ShapeKind.RoundedRect, 2f);
                stage.Box(mark.Rect, new Vector2(0f, 0.5f), new Vector2(0.5f, 1f),
                    new Vector2(at * trackWidth, -theme.PadSm), new Vector2(3f, theme.PadMd));
                Stage.Solid(mark.Fill, theme.Line);

                var name = stage.Label(track, Cuts[i].name, theme.Small * 0.85f, theme.TextSoft);
                stage.Box(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 1f),
                    new Vector2(at * trackWidth, -theme.PadMd - theme.PadSm),
                    new Vector2(theme.Small * 4f, theme.Small * 1.4f));
                name.WordWrap = false;
            }

            var dot = stage.Shape("Knob", track, ShapeKind.Circle);
            stage.Box(dot.Rect, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(theme.PadXl, theme.PadXl));
            Stage.Solid(dot.Fill, theme.Text);
            Stage.AddShadow(dot.Shape, theme.Shadow, new Vector2(0f, -4f), 12f);
            knob = dot.Rect;

            readout = stage.Label(panel.Rect, Readout(Light), theme.Body, theme.Text,
                HorizontalAlignment.Center, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(readout.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, theme.PadSm), new Vector2(-theme.PadXl * 2f, theme.Body * 1.5f));
            readout.WordWrap = false;
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            group.alpha = enter.Window(local, 0f, 0.5f);

            var sweep = Mathf.PingPong(Mathf.Max(0f, local - First) * Rate, 1f);
            var eased = Ease.Emphasized.Evaluate(sweep);
            var weight = Mathf.LerpUnclamped(Light, Black, eased);

            variation.Weight = UnitValue.Absolute(weight);
            readout.SetText(Readout(weight));

            Stage.Progress(fill, eased);
            knob.anchoredPosition = new Vector2(eased * trackWidth, 0f);

            for (var i = 0; i < facts.Count; i++)
                facts.Pose(i, enter.Window(local, Rows + i * Step, RowIn));
        }

        private static string Readout(float weight) => $"wght {Mathf.RoundToInt(weight)}";

        private const float Light = 100f;
        private const float Black = 900f;

        /// <summary>Sweeps per second. One full there-and-back takes twice this.</summary>
        private const float Rate = 0.42f;

        private const float First = 0.45f;
        private const float Rows = 1f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
