using System;
using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>The furniture a shot is built from.</summary>
    /// <remarks>
    /// Every factory constructs fresh layers and fresh paints. A layer or a paint belongs to exactly one shape, and
    /// handing the same instance to two of them throws.
    /// </remarks>
    public sealed partial class Stage
    {
        /// <summary>A bare shape with a fill layer and nothing else.</summary>
        public Widget Shape(string name, Transform parent, ShapeKind kind, float radius = 0f)
        {
            var rect = Node(name, parent);
            var shape = rect.gameObject.AddComponent<UniShape>();
            shape.Shape = new PrimitiveShapeProvider { Kind = kind, Radius = radius, Smoothing = Theme.Smoothing };
            shape.color = Color.white;
            shape.raycastTarget = false;
            return new Widget(shape);
        }

        /// <summary>A full-frame backdrop on <see cref="Promo.Theme.Background"/>.</summary>
        public Widget Backdrop(Transform parent)
        {
            var widget = Shape("Backdrop", parent, ShapeKind.RoundedRect);
            Fit(widget.Rect);
            Solid(widget.Fill, Theme.Background);
            return widget;
        }

        /// <summary>
        /// The product surface: dark, with the brand ramp along its top edge, lifted off the frame by a shadow so it
        /// reads as the nearest thing on screen.
        /// </summary>
        /// <remarks>
        /// The accent bar is inset by the panel's own radius. Nothing clips a shape's children, so a full-width bar
        /// would draw past both rounded corners and break the silhouette.
        /// <para>
        /// A panel owns child graphics, and a graphic's colour never reaches them. Fade one through
        /// <see cref="Group"/>, not <see cref="Alpha"/>.
        /// </para>
        /// </remarks>
        public Widget Panel(string name, Transform parent, float radius = -1f)
        {
            var r = radius < 0f ? Theme.RadiusXl : radius;
            var widget = Shape(name, parent, ShapeKind.RoundedRect, r);

            Solid(widget.Fill, Theme.Surface);
            AddShadow(widget.Shape, Theme.Shadow, new Vector2(0f, -10f), 44f, 4f);
            AddStroke(widget.Shape, Theme.Line, 2f, -1f);

            const float accentHeight = 6f;
            var accent = Shape("Accent", widget.Rect, ShapeKind.Capsule);
            Anchor(accent.Rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(-r * 2f, accentHeight));
            Ramped(accent.Fill, Theme.Brand, PaintProjectionKind.Linear, 0f);
            return widget;
        }

        /// <summary>A neutral card: everything on screen that is not the product.</summary>
        public Widget Card(string name, Transform parent, float radius = -1f)
        {
            var r = radius < 0f ? Theme.RadiusLg : radius;
            var widget = Shape(name, parent, ShapeKind.RoundedRect, r);

            Solid(widget.Fill, Theme.Paper);
            AddShadow(widget.Shape, new Color(0.06f, 0.06f, 0.11f, 0.20f), new Vector2(0f, -8f), 28f);
            AddInnerShadow(widget.Shape, new Color(0f, 0f, 0f, 0.06f), new Vector2(0f, 3f), 6f);
            return widget;
        }

        /// <summary>
        /// An inset well. The focus ring is a stroke layer, returned through <paramref name="ring"/> so a slide can
        /// drive its width and colour without searching the layer stack every frame.
        /// </summary>
        public Widget Field(string name, Transform parent, out StrokeLayer ring, float radius = -1f)
        {
            var r = radius < 0f ? Theme.RadiusMd : radius;
            var widget = Shape(name, parent, ShapeKind.RoundedRect, r);

            Solid(widget.Fill, Theme.Background);
            AddInnerShadow(widget.Shape, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 3f), 8f);
            ring = AddStroke(widget.Shape, Theme.Line, 3f, -1f);
            return widget;
        }

        /// <summary>A pill carrying the brand ramp, with an embossed edge.</summary>
        public Widget Button(string name, Transform parent, float radius = -1f)
        {
            var widget = radius < 0f
                ? Shape(name, parent, ShapeKind.Capsule)
                : Shape(name, parent, ShapeKind.RoundedRect, radius);

            Ramped(widget.Fill, Theme.Brand, PaintProjectionKind.Linear, 100f);
            AddShadow(widget.Shape, Promo.Theme.Fade(Theme.Violet, 0.45f), new Vector2(0f, -6f), 22f);
            AddBevel(widget.Shape, 120f, 12f, 0.9f);
            AddBevel(widget.Shape, 300f, 10f, 0.7f, true);
            return widget;
        }

        /// <summary>
        /// A capsule track filling <paramref name="parent"/>, with a gradient fill child. The returned rect is the
        /// FILL, not the track: pass it to <see cref="Progress"/>.
        /// </summary>
        public RectTransform Bar(Transform parent, float progress = 0f)
        {
            var track = Shape("Track", parent, ShapeKind.Capsule);
            Fit(track.Rect);
            Solid(track.Fill, Theme.Line);

            var fill = Shape("Fill", track.Rect, ShapeKind.Capsule);
            Anchor(fill.Rect, Vector2.zero, new Vector2(Mathf.Clamp01(progress), 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);
            Ramped(fill.Fill, Theme.Brand, PaintProjectionKind.Linear, 0f);
            return fill.Rect;
        }

        /// <summary>Text on the product surface, stretched to its parent unless told otherwise.</summary>
        /// <remarks>
        /// The initial string is assigned through <c>Text</c> rather than <c>SetText</c>: the property writes the
        /// serialized field, so the label survives a re-parse and a domain reload, while <c>SetText</c> only
        /// replaces the runtime source and leaves the serialized field empty. Per-frame changes still use
        /// <c>SetText</c>, which does not dirty the scene.
        /// </remarks>
        public UniText Label(Transform parent, string text, float size, Color color,
            HAlign horizontal = HAlign.Center, VAlign vertical = VAlign.Middle, bool stretch = true,
            UniTextFont face = null)
        {
            var rect = Node("Label", parent);
            if (stretch) Stretch(rect);

            var label = rect.gameObject.AddComponent<UniText>();
            label.raycastTarget = false;
            var chosen = face ? face : Theme.BodyFace;
            if (chosen) label.Font = chosen;
            label.FontSize = size;
            label.color = color;
            label.HorizontalAlignment = horizontal;
            label.VerticalAlignment = vertical;
            label.Text = text;
            return label;
        }

        /// <summary>
        /// A fill layer that paints text with the brand ramp.
        /// </summary>
        /// <remarks>
        /// A gradient reaches text only through a named swatch — an inline colour token cannot express one — so the
        /// modifier carries its own single-entry catalog rather than depending on project settings.
        /// </remarks>
        public FillModifier BrandFill(float angleDeg = 0f) => new FillModifier
        {
            Provider = BrandCatalog(BrandSwatch, angleDeg),
            Paint = PaintRef.Named(BrandSwatch)
        };

        /// <summary>
        /// A one-entry paint catalog naming the brand ramp, for any modifier that takes a
        /// <see cref="PaintRef"/> — a fill, a stroke, a highlight.
        /// </summary>
        /// <remarks>
        /// <paramref name="mapping"/> is how far the ramp is stretched before it repeats: across the styled range,
        /// the line, the whole block, or restarting on every glyph. It is the same gradient either way; only its
        /// frame changes.
        /// </remarks>
        public InlinePaintProvider BrandCatalog(string name, float angleDeg = 0f,
            PaintMapping mapping = PaintMapping.Range)
        {
            var paint = Paint.Default;
            paint.source.kind = PaintSourceKind.Gradient;
            paint.source.color = Color.white;
            paint.source.gradient = Theme.Brand;
            paint.projection.kind = PaintProjectionKind.Linear;
            paint.projection.angle = angleDeg;
            paint.projection.scale = 1f;

            var catalog = new InlinePaintProvider();
            catalog.Entries.Add(new PaintSwatch { name = name, paint = paint, mapping = mapping });
            return catalog;
        }

        /// <summary>The swatch name <see cref="BrandFill"/> registers.</summary>
        public const string BrandSwatch = "promo.brand";

        /// <summary>
        /// Width of <paramref name="content"/> as <paramref name="text"/> would draw it, in that component's own
        /// units. Empty content measures zero; anything else that measures zero throws.
        /// </summary>
        /// <remarks>
        /// A non-empty string has a width. Zero means the component declined to measure — <c>MeasureText</c> answers
        /// with the padding alone when it cannot work, which for the zero padding a layout query uses is zero — and
        /// nothing downstream can tell that apart from a genuinely empty prefix. Anything positioned from it lands on
        /// the element's left edge, which is a plausible-looking place and therefore the worst possible failure.
        /// <para>
        /// The size is pinned to the component's resolved one rather than left to the measure's own auto-sizing, so
        /// a body that shrank to fit its well is measured as it is drawn and not as it was asked for.
        /// </para>
        /// </remarks>
        public static float Advance(UniText text, string content)
        {
            if (string.IsNullOrEmpty(content)) return 0f;

            var size = text.CurrentFontSize;
            var advance = text.MeasureText(new TextMeasureOptions
            {
                text = content,
                wordWrap = false,
                autoSize = false,
                fontSize = size,
                padding = Vector4.zero
            }).x;

            if (advance > 0f) return advance;

            throw new InvalidOperationException(
                $"[Promo] '{text.name}' measured \"{content}\" as {advance} at size {size}. A non-empty string has " +
                "a width, so the component had nothing to shape and anything aimed at this would land on its left " +
                "edge. The usual cause is a collapsing reveal: a RevealModifier with Collapse on and Fill at zero " +
                "takes every cluster out of shaping and layout, so measure before attaching one. " +
                $"rect={text.rectTransform.rect.size}, autoSize={text.AutoSize}, " +
                $"font={(text.Font ? text.Font.name : "system cascade")}, content length={text.Text?.Length ?? 0}.");
        }

        /// <summary>
        /// Height of one line of <paramref name="text"/> at its resolved size, in that component's own units.
        /// </summary>
        /// <remarks>
        /// The vertical companion to <see cref="Advance"/>, and needed for the same reason: anything aimed at a line
        /// of type has to know where the line is, and a line box typed as a multiple of the point size drifts as
        /// soon as the face changes. A text aligned to the top of its rect puts its first line's centre half of this
        /// below the rect's top edge.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The component declined to measure.</exception>
        public static float LineBox(UniText text)
        {
            var size = text.CurrentFontSize;
            var height = text.MeasureText(new TextMeasureOptions
            {
                text = "M",
                wordWrap = false,
                autoSize = false,
                fontSize = size,
                padding = Vector4.zero
            }).y;

            if (height > 0f) return height;

            throw new InvalidOperationException(
                $"[Promo] '{text.name}' measured its line box as {height} at size {size}. A line has a height, so " +
                "the component had nothing to shape — the usual cause is a collapsing reveal, which takes every " +
                "cluster out of layout. Measure before attaching one.");
        }

        /// <summary>
        /// Applies <paramref name="modifier"/> to the first occurrence of <paramref name="word"/>, and reports
        /// whether it was found.
        /// </summary>
        /// <remarks>
        /// Ranges are located by search rather than typed as indices, so editing the sentence cannot silently move
        /// an effect onto the wrong word — which is exactly how a demonstration ends up claiming one thing and
        /// showing another.
        /// <para>
        /// Range bounds are codepoint indices. A word searched for after an astral character — most emoji — would
        /// land short; keep styled words ahead of them, or compute the range another way.
        /// </para>
        /// </remarks>
        public static bool StyleWord(UniText text, string word, BaseModifier modifier)
        {
            var source = text.Text;
            var start = string.IsNullOrEmpty(word) ? -1 : source.IndexOf(word, StringComparison.Ordinal);
            if (start < 0) return false;

            text.Styles.Add(Style.Range(modifier, start, start + word.Length));
            return true;
        }

        /// <summary>
        /// Styles the whole of <paramref name="text"/> so its glyphs arrive one after another under
        /// <see cref="GlyphReveal.Fill"/>, each played in by <paramref name="handler"/> — by default a rise and a
        /// fade.
        /// </summary>
        /// <remarks>
        /// The handler is driven through <see cref="RevealModifier.GlyphRevealing"/>, never through a
        /// <see cref="RevealHandlerEntry"/> in the modifier's provider. The two paths look interchangeable and are
        /// not: a serialized entry is played by a one-shot timeline off the system stopwatch, which cannot be
        /// scrubbed backwards and freezes at its first frame under a capture that steps frames faster than the wall
        /// clock. The event instead carries a frontier envelope derived from <see cref="GlyphReveal.Fill"/> alone,
        /// so a glyph looks the same every time that frame is composed.
        /// <para>
        /// The modifier is pinned fully filled and decides nothing; <see cref="GlyphReveal"/> owns the envelope, and
        /// a handler that does not vanish at progress 0 shows the whole text at once.
        /// </para>
        /// <para>
        /// <paramref name="spread"/> is how many glyphs are mid-animation at once. Left at one, a glyph's whole
        /// move lasts the reveal divided by its glyph count — a few dozen milliseconds on any real sentence, which
        /// reads as a pop rather than an arrival. Widening it overlaps neighbours and slows each without slowing
        /// the reveal itself. The handler's own <c>Duration</c> cannot do this: it only arms the wall-clock timeline
        /// this deliberately avoids.
        /// </para>
        /// <para>
        /// Handler offsets are in pixels and do not scale with the type. At <see cref="Promo.Theme.Hero"/> the
        /// shipped 12 px default is invisible; size the handler to the text.
        /// </para>
        /// </remarks>
        /// <summary>
        /// A typewriter that <em>removes</em> what it has not reached yet, and whose frontier is the modifier's own
        /// <see cref="RevealModifier.Front"/>.
        /// </summary>
        /// <remarks>
        /// This, never <see cref="Reveal"/>, is what text carrying range decorations needs. A highlight, a mention
        /// chip, a spoiler cover and a search hit are surfaces drawn behind a range, and they do not consult the
        /// alpha of the glyphs above them — over text that is merely faded to nothing they appear as bare shapes
        /// floating in the panel. Collapsed text is excluded from shaping and layout outright, so there is no range
        /// left for a decoration to cover.
        /// <para>
        /// The same applies to anything else that is not a plain glyph quad: inline media, ruby annotations, list
        /// markers. If the body carries one, it belongs here.
        /// </para>
        /// <para>
        /// The cost is a reflow per frame instead of a mesh rebuild, and the text moves as it arrives because the
        /// line lengths change under it. On a shot about the decorations that is the honest picture; on a shot about
        /// the type it is a distraction, and <see cref="Reveal"/> is the quieter tool.
        /// </para>
        /// </remarks>
        public RevealModifier Typewriter(UniText text, RevealHandler handler = null)
        {
            var modifier = new RevealModifier { Front = UnitValue.Percent(0f), Collapse = true };

            if (handler != null) modifier.GlyphRevealing += handler.Apply;
            text.Styles.Add(Style.WholeText(modifier));
            return modifier;
        }

        /// <summary>
        /// The same reveal over the first occurrence of <paramref name="word"/> only, so one text can carry several
        /// arrivals at once. Throws when the word is not in the text.
        /// </summary>
        /// <remarks>
        /// One <see cref="RevealModifier"/> per word rather than named entries in a shared provider. A
        /// <c>&lt;reveal=name&gt;</c> tag resolves to a <see cref="RevealHandlerEntry"/>, and an entry is played by a
        /// one-shot timeline off the system stopwatch — a shot built from those cannot be scrubbed and freezes at
        /// its first frame under a capture that steps faster than the wall clock.
        /// </remarks>
        public GlyphReveal Reveal(UniText text, string word, RevealHandler handler, float spread = 0f)
        {
            var source = text.Text;
            var start = string.IsNullOrEmpty(word) ? -1 : source.IndexOf(word, StringComparison.Ordinal);

            if (start < 0)
                throw new InvalidOperationException(
                    $"[Promo] '{text.name}' has no \"{word}\" to reveal. A range located by search cannot silently " +
                    "miss: the word would simply never arrive, which reads as a handler that does nothing.");

            var reveal = new GlyphReveal(text, handler, spread > 0f ? spread : Theme.RevealSpread);
            var modifier = new RevealModifier { Front = UnitValue.Percent(100f), Collapse = false };

            modifier.GlyphRevealing += reveal.Apply;
            text.Styles.Add(Style.Range(modifier, start, start + word.Length));
            return reveal;
        }

        public GlyphReveal Reveal(UniText text, RevealHandler handler = null, float spread = 0f)
        {
            var reveal = new GlyphReveal(text, handler ?? new SlideRevealHandler(),
                spread > 0f ? spread : Theme.RevealSpread);
            var modifier = new RevealModifier { Front = UnitValue.Percent(100f), Collapse = false };

            modifier.GlyphRevealing += reveal.Apply;
            text.Styles.Add(Style.WholeText(modifier));
            return reveal;
        }

        /// <summary>
        /// A pointer that plays <paramref name="beats"/> from <paramref name="start"/>, built above everything the
        /// slide has created so far. Pass <paramref name="art"/> to use an imported cursor image instead of the
        /// built-in vector arrow.
        /// </summary>
        /// <remarks>
        /// Build it last. It is drawn in sibling order like any other UI, and a pointer behind the thing it is
        /// operating is worse than no pointer at all.
        /// </remarks>
        public Pointer Pointer(Vector2 start, IReadOnlyList<Beat> beats, Promo.Pointer.Art art = default) =>
            new Promo.Pointer(this, new PointerTimeline(start, beats), Theme.Magenta, Theme.Accent, art);

        /// <summary>
        /// A pill with a word in it: a label, a verdict, a badge. The caller positions and sizes it, usually to
        /// <see cref="ChipWidth"/> by <see cref="ChipHeight"/>.
        /// </summary>
        /// <remarks>
        /// The text is supplied here and never assigned afterwards. Text written after construction goes through
        /// the runtime source and leaves the serialized field empty, which lasts exactly until the next re-parse and
        /// then shows an empty pill.
        /// </remarks>
        public Widget Chip(string name, Transform parent, string text, Color fill, Color ink, out UniText label)
        {
            var chip = Shape(name, parent, ShapeKind.Capsule);
            Solid(chip.Fill, fill);
            label = Label(chip.Rect, text, Theme.Small, ink);
            label.Styles.Add(Style.WholeText(new BoldModifier()));
            return chip;
        }

        /// <summary>Pill width for <paramref name="text"/> at <see cref="Promo.Theme.Small"/>.</summary>
        public float ChipWidth(string text) => Theme.PadXl * 2f + Estimate(text, Theme.Small);

        /// <summary>
        /// The digits <paramref name="text"/> opens with, or the whole text when it opens with none — the part of a
        /// figure-led line a brand paint goes on.
        /// </summary>
        /// <remarks>
        /// Taken from the string rather than authored beside it, so editing a line cannot leave the paint on a word
        /// that is no longer a number.
        /// </remarks>
        public static string LeadingFigure(string text)
        {
            var end = 0;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return end == 0 ? text : text.Substring(0, end);
        }

        public float ChipHeight => Theme.Small * 2.1f;

        /// <summary>The dominant line of a frame: one phrase, at <see cref="Promo.Theme.Hero"/>.</summary>
        public UniText Headline(Transform parent, string text) =>
            Label(parent, text, Theme.Hero, Theme.Text, face: Theme.DisplayFace);

        /// <summary>The line under a headline, never competing with it.</summary>
        public UniText Caption(Transform parent, string text) =>
            Label(parent, text, Theme.Body, Theme.TextSoft);
    }
}
