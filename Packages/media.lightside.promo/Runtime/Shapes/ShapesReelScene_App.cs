using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>
    /// The payoff of <see cref="ShapesReelScene"/> — a whole screen assembled from shapes — and the card the film
    /// ends on.
    /// </summary>
    public sealed partial class ShapesReelScene
    {
        /// <summary>One thing that pops onto the screen: its rect, the group that hides it until then, and when.</summary>
        private readonly struct Arrival
        {
            public Arrival(RectTransform rect, CanvasGroup group, float at)
            {
                Rect = rect;
                Group = group;
                At = at;
            }

            public RectTransform Rect { get; }
            public CanvasGroup Group { get; }
            public float At { get; }
        }

        private static readonly float[] Fractions = { 0.72f, 0.46f, 0.88f };
        private static readonly string[] StatNames = { "Steps", "Sleep", "Focus" };
        private static readonly float[] BarFractions = { 0.45f, 0.7f, 0.55f, 0.92f, 0.62f, 0.8f, 1f };
        private static readonly string[] Segments = { "Day", "Week", "Month" };

        private readonly List<Arrival> arrivals = new();
        private RectTransform app;
        private CanvasGroup appGroup;

        private PrimitiveShapeProvider[] arcs;
        private UniText[] figures;
        private float[] arcAt;

        private RectTransform[] bars;
        private float barAt;
        private float barWidth;
        private float barHeight;

        private RectTransform toggleKnob;
        private ShapePaint toggleTrack;
        private float toggleTravel;
        private float toggleAt;

        private Slider appSlider;
        private float sliderAt;

        private RectTransform segmentPill;
        private float segmentInset;
        private float segmentStep;
        private float segmentAt;

        private RectTransform endHolder;
        private CanvasGroup endGroup;
        private Widget endGlow;
        private Widget mark;
        private UniText title;
        private GlyphReveal titleReveal;
        private UniText caption;

        /// <summary>
        /// A dashboard: every panel, ring, bar, toggle and button a layer stack, every one registered to pop in on
        /// its own beat.
        /// </summary>
        /// <remarks>
        /// Nothing here carries a blend other than Normal or a texture, which is what keeps the counter above it
        /// honest: an all-Normal, untextured stack of shapes and text is one draw call.
        /// </remarks>
        private void BuildApp(Stage stage)
        {
            arrivals.Clear();
            app = stage.Node("App", world);
            stage.Stretch(app);
            appGroup = Stage.Group(app);

            var size = new Vector2(frameWidth * 0.6f, stage.ContentHeight * AppHeight);
            var screen = stage.Panel("Screen", app, theme.RadiusXxl);
            stage.Box(screen.Rect, new Vector2(0f, stage.ContentCentre - frameHeight * 0.01f), size);
            Arrive(screen.Rect);

            var pad = theme.PadXl;
            var gap = theme.PadMd;
            var inner = size.x - pad * 2f;
            var header = size.y * 0.12f;
            var stats = size.y * 0.26f;
            var third = size.y * 0.33f;
            var bottom = size.y - pad * 2f - header - stats - third - gap * 3f;
            var y = pad;

            BuildHeader(stage, screen.Rect, pad, y, inner, header);
            y += header + gap;
            BuildStats(stage, screen.Rect, pad, y, inner, stats, gap);
            y += stats + gap;

            var chartWidth = inner * 0.52f;
            BuildChart(stage, screen.Rect, pad, y, chartWidth, third);
            BuildControls(stage, screen.Rect, pad + chartWidth + gap, y, inner - chartWidth - gap, third);
            y += third + gap;

            BuildActions(stage, screen.Rect, pad, y, inner, bottom);
            BuildCounters(stage, stage.ContentTop - stage.ChipHeight * 0.5f - theme.PadMd);
        }

        /// <summary>
        /// The two figures over the screen, the last thing to arrive: the verdict on everything under them.
        /// </summary>
        private void BuildCounters(Stage stage, float y)
        {
            var counters = stage.Node("Counters", app);
            stage.Box(counters, new Vector2(0f, y), new Vector2(frameWidth * 0.5f, stage.ChipHeight));

            var callsWidth = stage.ChipWidth(drawCalls);
            var spritesWidth = stage.ChipWidth(sprites);
            var gap = theme.PadMd;
            var fill = theme.Surface;

            var calls = stage.Chip("Calls", counters, drawCalls, fill, theme.Text, out var callsLabel);
            stage.Box(calls.Rect, new Vector2(-(spritesWidth + gap) * 0.5f, 0f),
                new Vector2(callsWidth, stage.ChipHeight));
            Stage.StyleWord(callsLabel, Stage.LeadingFigure(drawCalls), stage.BrandFill(15f));

            var spritesChip = stage.Chip("Sprites", counters, sprites, fill, theme.Text, out var spritesLabel);
            stage.Box(spritesChip.Rect, new Vector2((callsWidth + gap) * 0.5f, 0f),
                new Vector2(spritesWidth, stage.ChipHeight));
            Stage.StyleWord(spritesLabel, Stage.LeadingFigure(sprites), stage.BrandFill(15f));

            Arrive(counters);
        }

        private void BuildHeader(Stage stage, RectTransform parent, float x, float y, float w, float h)
        {
            var row = stage.Node("Header", parent);
            Place(stage, row, x, y, w, h);

            var diameter = h * 0.8f;
            var avatar = stage.Node("Avatar", row);
            stage.Box(avatar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.one * (diameter * 1.22f));

            var disc = stage.Shape("Disc", avatar, ShapeKind.Circle);
            stage.Box(disc.Rect, Vector2.zero, Vector2.one * diameter);
            Stage.Radial(disc.Fill, theme.Orange, theme.Magenta, 1.2f);

            var halo = stage.Shape("Halo", avatar, ShapeKind.Ring);
            halo.Outline.Thickness = 0.12f;
            stage.Box(halo.Rect, Vector2.zero, Vector2.one * (diameter * 1.22f));
            Stage.Solid(halo.Fill, theme.Line);
            Arrive(avatar);

            var titles = stage.Node("Titles", row);
            stage.Anchor(titles, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(0f, 0.5f),
                new Vector2(diameter * 1.55f, 0f), Vector2.zero);

            var heading = stage.Label(titles, "Dashboard", theme.Head * 0.66f, theme.Text, HAlign.Left, VAlign.Middle,
                false, theme.DisplayFace);
            stage.Anchor(heading.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, h * 0.17f), new Vector2(0f, h * 0.55f));
            heading.WordWrap = false;

            var date = stage.Label(titles, "Tuesday · 2 Sep", theme.Small * 0.8f, theme.TextSoft, HAlign.Left,
                VAlign.Middle, false);
            stage.Anchor(date.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, -h * 0.27f), new Vector2(0f, h * 0.36f));
            date.WordWrap = false;
            Arrive(titles);

            var bell = stage.Shape("Bell", row, ShapeKind.RoundedRect, h * 0.28f);
            bell.Outline.Smoothing = 0.8f;
            stage.Box(bell.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.one * (h * 0.8f));
            Stage.Solid(bell.Fill, Theme.Lift(theme.Surface, TileLift * 2f));
            Stage.AddStroke(bell.Shape, theme.Line, 2f, -1f);

            var dot = stage.Shape("Dot", bell.Rect, ShapeKind.Circle);
            stage.Box(dot.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-h * 0.14f, -h * 0.14f),
                Vector2.one * (h * 0.18f));
            Stage.Solid(dot.Fill, theme.Magenta);

            var badge = stage.Chip("Badge", row, "PRO", theme.Accent, Color.white, out _);
            stage.Box(badge.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(h * 0.8f + theme.PadMd), 0f),
                new Vector2(stage.ChipWidth("PRO"), h * 0.62f));
            Stage.Ramped(badge.Fill, brand, PaintProjectionKind.Linear, 100f);
            Arrive(badge.Rect);
            Arrive(bell.Rect);
        }

        private void BuildStats(Stage stage, RectTransform parent, float x, float y, float w, float h, float gap)
        {
            var count = Fractions.Length;
            var tileWidth = (w - gap * (count - 1)) / count;
            var ringSize = h * 0.62f;

            arcs = new PrimitiveShapeProvider[count];
            figures = new UniText[count];
            arcAt = new float[count];

            for (var k = 0; k < count; k++)
            {
                var tile = Tile(stage, "Stat" + k, parent, x + k * (tileWidth + gap), y, tileWidth, h, out _);
                var content = stage.Node("Content", tile.Rect);
                stage.Stretch(content);

                var track = stage.Shape("Track", content, ShapeKind.Ring);
                track.Outline.Thickness = ArcThickness;
                stage.Box(track.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(theme.PadLg, 0f),
                    Vector2.one * ringSize);
                Stage.Solid(track.Fill, theme.Line);

                var arc = stage.Shape("Arc", content, ShapeKind.Arc);
                arc.Outline.Thickness = ArcThickness;
                arc.Outline.Start = ArcTop;
                arc.Outline.End = ArcTop;
                arc.Outline.Cap = EndCap.Round;
                stage.Box(arc.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(theme.PadLg, 0f),
                    Vector2.one * ringSize);
                Stage.Ramped(arc.Fill, brand, PaintProjectionKind.Linear, 90f);
                arcs[k] = arc.Outline;

                var left = theme.PadLg + ringSize + theme.PadMd;
                var figure = stage.Label(content, Percent(0f), theme.Head * 0.8f, theme.Text, HAlign.Left, VAlign.Middle,
                    false, theme.DisplayFace);
                stage.Anchor(figure.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(left, h * 0.12f), new Vector2(-(left + theme.PadMd), h * 0.5f));
                figure.WordWrap = false;
                figures[k] = figure;

                var caption = stage.Label(content, StatNames[k], theme.Small, theme.TextSoft, HAlign.Left, VAlign.Middle,
                    false);
                stage.Anchor(caption.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(left, -h * 0.22f), new Vector2(-(left + theme.PadMd), h * 0.35f));
                caption.WordWrap = false;

                arcAt[k] = Arrive(content);
            }
        }

        private void BuildChart(Stage stage, RectTransform parent, float x, float y, float w, float h)
        {
            var tile = Tile(stage, "Chart", parent, x, y, w, h, out barAt);
            var label = stage.Label(tile.Rect, "This week", theme.Small, theme.TextSoft, HAlign.Left, VAlign.Middle, false);
            stage.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -theme.PadLg), new Vector2(-theme.PadLg * 2f, theme.Small * 1.4f));
            label.WordWrap = false;

            var count = BarFractions.Length;
            var slot = (w - theme.PadLg * 2f) / count;
            barWidth = slot * BarShare;
            barHeight = h - theme.PadLg - theme.Small * 1.4f - theme.PadSm - theme.PadMd;

            var lane = stage.Node("Bars", tile.Rect);
            stage.Anchor(lane, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, theme.PadMd), new Vector2(-theme.PadLg * 2f, barHeight));

            bars = new RectTransform[count];
            for (var i = 0; i < count; i++)
            {
                var bar = stage.Shape("Bar" + i, lane, ShapeKind.RoundedRect, barWidth * 0.35f);
                stage.Box(bar.Rect, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(slot * (i + 0.5f), 0f), new Vector2(barWidth, 2f));
                Stage.Ramped(bar.Fill, brand, PaintProjectionKind.Linear, 90f);
                bars[i] = bar.Rect;
            }
        }

        private void BuildControls(Stage stage, RectTransform parent, float x, float y, float w, float h)
        {
            var tile = Tile(stage, "Controls", parent, x, y, w, h, out _);
            var rowHeight = (h - theme.PadLg * 2f) / 3f;
            var rowWidth = w - theme.PadLg * 2f;

            var toggleRow = stage.Node("Toggle", tile.Rect);
            Place(stage, toggleRow, theme.PadLg, theme.PadLg, rowWidth, rowHeight);
            Caption(stage, toggleRow, "Notifications");

            var trackSize = new Vector2(rowHeight * 1.9f, rowHeight * 0.86f);
            var track = stage.Shape("Track", toggleRow, ShapeKind.Capsule);
            stage.Box(track.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, trackSize);
            Stage.Solid(track.Fill, theme.Line);
            toggleTrack = track.Fill;

            var knobSize = trackSize.y * 0.8f;
            toggleTravel = (trackSize.x - knobSize) * 0.5f - trackSize.y * 0.1f;
            var knob = stage.Shape("Knob", track.Rect, ShapeKind.Circle);
            stage.Box(knob.Rect, new Vector2(-toggleTravel, 0f), Vector2.one * knobSize);
            Stage.Solid(knob.Fill, Color.white);
            Stage.AddShadow(knob.Shape, theme.Shadow, new Vector2(0f, -2f), 8f);
            toggleKnob = knob.Rect;
            toggleAt = Arrive(toggleRow);

            var sliderRow = stage.Node("Slider", tile.Rect);
            Place(stage, sliderRow, theme.PadLg, theme.PadLg + rowHeight, rowWidth, rowHeight);
            Caption(stage, sliderRow, "Volume");

            var lane = stage.Node("Lane", sliderRow);
            stage.Anchor(lane, new Vector2(0.34f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            appSlider = stage.Slider(lane, theme.Small, 0f, 100f, "{0:0}");
            sliderAt = Arrive(sliderRow);

            var housingHeight = rowHeight - theme.PadSm;
            var housing = stage.Shape("Segments", tile.Rect, ShapeKind.RoundedRect, theme.RadiusSm);
            Place(stage, housing.Rect, theme.PadLg, theme.PadLg + rowHeight * 2f + theme.PadSm * 0.5f, rowWidth, housingHeight);
            Stage.Solid(housing.Fill, Theme.Sink(theme.Surface, 0.25f));

            var inset = theme.PadXs * 0.7f;
            segmentInset = inset;
            segmentStep = (rowWidth - inset * 2f) / Segments.Length;
            var pill = stage.Shape("Pill", housing.Rect, ShapeKind.RoundedRect, theme.Inner(theme.RadiusSm, inset));
            stage.Box(pill.Rect, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(inset + segmentStep * 0.5f, 0f),
                new Vector2(segmentStep, housingHeight - inset * 2f));
            Stage.Ramped(pill.Fill, brand, PaintProjectionKind.Linear, 100f);
            segmentPill = pill.Rect;

            for (var i = 0; i < Segments.Length; i++)
            {
                var word = stage.Label(housing.Rect, Segments[i], theme.Small * 0.9f, theme.Text, HAlign.Center, VAlign.Middle, false);
                stage.Box(word.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(inset + segmentStep * (i + 0.5f), 0f), new Vector2(segmentStep, housingHeight));
                word.WordWrap = false;
            }

            segmentAt = Arrive(housing.Rect);
        }

        private void BuildActions(Stage stage, RectTransform parent, float x, float y, float w, float h)
        {
            var buttonHeight = h * 0.9f;
            var cta = stage.Shape("Cta", parent, ShapeKind.Capsule);
            Place(stage, cta.Rect, x, y + (h - buttonHeight) * 0.5f, w * 0.34f, buttonHeight);
            Stage.Ramped(cta.Fill, brand, PaintProjectionKind.Linear, 100f);
            Stage.AddShadow(cta.Shape, Theme.Fade(theme.Magenta, 0.45f), new Vector2(0f, -8f), 24f);
            Stage.AddStroke(cta.Shape, Theme.Fade(Color.white, 0.35f), 2f, -1f);

            var call = stage.Label(cta.Rect, "Get started", theme.Body * 0.9f, Color.white, face: theme.DisplayFace);
            call.WordWrap = false;
            Arrive(cta.Rect);

            var fab = stage.Shape("Fab", parent, ShapeKind.Circle);
            Place(stage, fab.Rect, x + w - h * 0.9f, y + h * 0.05f, h * 0.9f, h * 0.9f);
            Stage.Ramped(fab.Fill, brand, PaintProjectionKind.Linear, 135f);
            Stage.AddShadow(fab.Shape, Theme.Fade(theme.Magenta, 0.45f), new Vector2(0f, -8f), 24f);

            var across = stage.Shape("Across", fab.Rect, ShapeKind.Capsule);
            stage.Box(across.Rect, Vector2.zero, new Vector2(h * 0.36f, h * 0.07f));
            Stage.Solid(across.Fill, Color.white);
            var down = stage.Shape("Down", fab.Rect, ShapeKind.Capsule);
            stage.Box(down.Rect, Vector2.zero, new Vector2(h * 0.07f, h * 0.36f));
            Stage.Solid(down.Fill, Color.white);
            Arrive(fab.Rect);
        }

        /// <summary>
        /// A lifted card inside the screen, placed from the screen's top-left corner; <paramref name="at"/> is the
        /// beat it pops in on, for whatever it holds to come alive after.
        /// </summary>
        private Widget Tile(Stage stage, string name, RectTransform parent, float x, float y, float w, float h,
            out float at)
        {
            var tile = stage.Shape(name, parent, ShapeKind.RoundedRect, theme.Inner(theme.RadiusXxl, theme.PadXl));
            Place(stage, tile.Rect, x, y, w, h);
            Stage.Solid(tile.Fill, Theme.Lift(theme.Surface, TileLift));
            Stage.AddStroke(tile.Shape, theme.Line, 2f, -1f);
            at = Arrive(tile.Rect);
            return tile;
        }

        /// <summary>A control row's name, on its left half.</summary>
        private void Caption(Stage stage, RectTransform row, string text)
        {
            var label = stage.Label(row, text, theme.Small, theme.Text, HAlign.Left, VAlign.Middle, false);
            stage.Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);
            label.WordWrap = false;
        }

        /// <summary>Pins <paramref name="rect"/> by its top-left corner, <paramref name="x"/> across and <paramref name="y"/> down from its parent's.</summary>
        private static void Place(Stage stage, RectTransform rect, float x, float y, float w, float h) =>
            stage.Box(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));

        /// <summary>Registers <paramref name="rect"/> to pop in on the next beat of the cascade, and returns that beat.</summary>
        private float Arrive(RectTransform rect)
        {
            var at = arrivals.Count * AppStagger;
            arrivals.Add(new Arrival(rect, Stage.Group(rect), at));
            return at;
        }

        private static string Percent(float fraction) => Mathf.RoundToInt(fraction * 100f) + "%";

        private void PoseApp(float local)
        {
            var gone = Motion.Whip.Window(local, end, 0.45f);
            var open = local >= appStart - 0.05f;
            appGroup.alpha = open ? 1f - gone : 0f;
            app.anchoredPosition = new Vector2(0f, -frameHeight * 0.9f * gone);
            if (!open) return;

            for (var i = 0; i < arrivals.Count; i++)
            {
                var arrival = arrivals[i];
                var since = local - (appStart + arrival.At);
                arrival.Group.alpha = Mathf.Clamp01(since * 12f);
                Stage.Scale(arrival.Rect, Mathf.LerpUnclamped(0.55f, 1f, Spring.Pop.Evaluate(since)));
            }

            for (var k = 0; k < arcs.Length; k++)
            {
                var live = Live(local, arcAt[k]);
                arcs[k].End = ArcTop - 360f * Fractions[k] * live;
                figures[k].SetText(Percent(Fractions[k] * live));
            }

            for (var i = 0; i < bars.Length; i++)
            {
                var grown = Motion.Back.Window(local, appStart + barAt + LiveLag + i * BarStagger, 0.6f);
                bars[i].sizeDelta = new Vector2(barWidth, Mathf.Max(2f, barHeight * BarFractions[i] * grown));
            }

            var flipped = Motion.Back.Window(local, appStart + toggleAt + LiveLag, 0.45f);
            toggleKnob.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-toggleTravel, toggleTravel, flipped), 0f);
            toggleTrack.Color = Color.LerpUnclamped(theme.Line, theme.Accent, Mathf.Clamp01(flipped));

            appSlider.Pose(Mathf.LerpUnclamped(SliderFrom, SliderTo, Live(local, sliderAt)));

            var hopped = Motion.Back.Window(local, appStart + segmentAt + LiveLag, 0.5f);
            segmentPill.anchoredPosition = new Vector2(
                segmentInset + segmentStep * (0.5f + (Segments.Length - 1) * hopped), 0f);
        }

        /// <summary>How far a widget's value has run, from a beat after it landed.</summary>
        private float Live(float local, float at) => Motion.Meter.Window(local, appStart + at + LiveLag, LiveFor);

        /// <summary>The end card: the mark on a plate, the wordmark under it, the site under that.</summary>
        /// <remarks>
        /// <see cref="logo"/> is wired to the shipped mark when the rig is created. Left empty, the plate falls back to
        /// the brand ramp — a designed placeholder, not the finished lockup.
        /// </remarks>
        private void BuildEnd(Stage stage)
        {
            endHolder = stage.Node("End", world);
            stage.Stretch(endHolder);
            endGroup = Stage.Group(endHolder);

            var markSize = frameHeight * MarkFraction;
            var centre = new Vector2(0f, frameHeight * 0.11f);

            endGlow = stage.Shape("EndGlow", endHolder, ShapeKind.Circle);
            stage.Box(endGlow.Rect, centre, Vector2.one * (frameHeight * 1.1f));
            Stage.Radial(endGlow.Fill, Theme.Fade(theme.Glow, 0.55f), Theme.Fade(theme.Glow, 0f));

            mark = stage.Shape("Mark", endHolder, ShapeKind.RoundedRect, theme.RadiusXxl);
            stage.Box(mark.Rect, centre, Vector2.one * markSize);
            if (logo) Stage.Textured(mark.Fill, logo);
            else Stage.Ramped(mark.Fill, brand, PaintProjectionKind.Radial);

            title = stage.Label(endHolder, wordmark, theme.Title, theme.Text, stretch: false, face: theme.DisplayFace);
            stage.Box(title.rectTransform, new Vector2(0f, -frameHeight * 0.14f),
                new Vector2(frameWidth * 0.7f, theme.Title * 1.55f));
            title.WordWrap = false;
            titleReveal = stage.Reveal(title, new SlideRevealHandler
            {
                Offset = new Vector2(0f, -theme.Title * 0.42f),
                Easing = Ease.Of(EasingType.CubicOut)
            });

            caption = stage.Caption(endHolder, site);
            stage.Box(caption.rectTransform, new Vector2(0f, -frameHeight * 0.25f),
                new Vector2(frameWidth * 0.7f, theme.Body * 2.1f));
        }

        private void PoseEnd(float local)
        {
            var since = local - end;
            var born = Spring.Pop.Evaluate(since - 0.1f);

            endGroup.alpha = Mathf.Clamp01(since * 6f);
            Stage.Scale(mark.Rect, Mathf.LerpUnclamped(0.3f, 1f, born));
            Stage.Alpha(mark.Shape, Mathf.Clamp01(born * 3f));

            Stage.Scale(endGlow.Rect, Mathf.LerpUnclamped(0.6f, 1f, Mathf.Clamp01(born)));
            Stage.Alpha(endGlow.Shape, Mathf.Clamp01(born) * 0.6f);

            titleReveal.Fill = GlyphReveal.Frontier.Window(local, end + 0.5f, 0.8f);
            Stage.Alpha(caption, Ease.EmphasizedIn.Window(local, end + 1.1f, 0.6f));
        }

        private const float AppHeight = 0.76f;
        private const float ArcThickness = 0.17f;

        /// <summary>
        /// How far a tile is lifted off the screen, in linear light. Tiny on purpose: a tenth of the way to white in
        /// linear light is a mid grey in sRGB, and a card should read as the same surface a step nearer.
        /// </summary>
        private const float TileLift = 0.015f;

        /// <summary>How much of its slot a chart bar fills; the rest is the air that makes it a bar and not a blob.</summary>
        private const float BarShare = 0.58f;

        /// <summary>Where a progress arc begins: straight up, sweeping clockwise as its end falls below it.</summary>
        private const float ArcTop = 90f;

        private const float LiveLag = 0.3f;
        private const float LiveFor = 0.9f;
        private const float BarStagger = 0.06f;
        private const float SliderFrom = 0.25f;
        private const float SliderTo = 0.7f;

        private const float MarkFraction = 0.28f;
    }
}
