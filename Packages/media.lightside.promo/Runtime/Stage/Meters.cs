using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>One bar of a <see cref="Meters"/> panel, as data.</summary>
    public readonly struct MeterEntry
    {
        /// <summary>
        /// A labeled bar. <paramref name="fraction"/> is how full it settles, and
        /// <paramref name="highlight"/> marks the row the shot is arguing for.
        /// </summary>
        public MeterEntry(string label, string value, float fraction, bool highlight = false,
            Color fill = default)
        {
            Label = label;
            Value = value;
            Fraction = fraction;
            Highlight = highlight;
            Fill = fill;
        }

        public string Label { get; }
        public string Value { get; }
        public float Fraction { get; }
        public bool Highlight { get; }

        /// <summary>
        /// The bar's own color, or a default one to take the brand ramp instead.
        /// </summary>
        /// <remarks>
        /// For the rare row whose color carries meaning of its own — a cost against a saving — rather than simply
        /// marking which row the shot argues for.
        /// </remarks>
        public Color Fill { get; }

        internal bool HasFill => Fill.a > 0f;
    }

    /// <summary>
    /// A titled panel of labeled bars: the shape every quantitative claim in the film takes.
    /// </summary>
    /// <remarks>
    /// Bars rather than numbers alone because a ratio is the claim. A viewer reads two bar lengths in one glance and
    /// two figures in three, and the film has neither the seconds nor the viewer's patience for the second.
    /// </remarks>
    public sealed class Meters
    {
        /// <summary>A built bar.</summary>
        public readonly struct Row
        {
            internal Row(RectTransform rect, UniText label, UniText value, RectTransform fill, float fraction)
            {
                Rect = rect;
                Label = label;
                Value = value;
                Fill = fill;
                Fraction = fraction;
            }

            public RectTransform Rect { get; }
            public UniText Label { get; }
            public UniText Value { get; }

            /// <summary>The bar's fill rect, driven through <see cref="Stage.Progress"/>.</summary>
            public RectTransform Fill { get; }

            /// <summary>How full this bar settles.</summary>
            public float Fraction { get; }
        }

        private readonly Row[] rows;

        internal Meters(Widget surface, UniText title, Row[] rows)
        {
            Surface = surface;
            Title = title;
            this.rows = rows;
        }

        public Widget Surface { get; }

        /// <summary>The panel's height, so a second panel can be built to match it.</summary>
        public float Height => Surface.Rect.rect.height;

        public UniText Title { get; }

        public IReadOnlyList<Row> Rows => rows;

        public Row this[int index] => rows[index];

        public int Count => rows.Length;

        /// <summary>Fills bar <paramref name="index"/> to <paramref name="t"/> of its settled length, fading its labels in with it.</summary>
        public void Pose(int index, float t)
        {
            var row = rows[index];
            var k = Mathf.Clamp01(t);

            Stage.Progress(row.Fill, row.Fraction * k);
            Stage.Alpha(row.Label, k);
            if (row.Value) Stage.Alpha(row.Value, k);
        }
    }

    /// <summary>Builds a <see cref="Promo.Meters"/>.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A titled panel of labeled bars, centered on <paramref name="position"/> and <paramref name="width"/> wide.
        /// </summary>
        /// <remarks>
        /// Sizes its own height from its content, for the same reason <see cref="Ledger"/> does: a caller cannot know
        /// how tall a list of rows is without repeating the arithmetic, and a guessed height leaves the last row
        /// hanging outside the panel it belongs to.
        /// <para>
        /// Fractions are the caller's, not derived from the values: a bar whose numbers span three orders of
        /// magnitude is unreadable drawn to scale, and a film that draws it to scale shows an empty panel with one
        /// full bar in it.
        /// </para>
        /// </remarks>
        public Meters Meters(string name, Transform parent, string title, MeterEntry[] entries,
            Vector2 position, float width, bool onBrand = true, float textSize = 0f, float minHeight = 0f)
        {
            var size = textSize > 0f ? textSize : Theme.Small;
            var barHeight = size * 0.62f;
            var rowHeight = size * 3.1f;
            var headHeight = size * 1.7f;
            var ink = onBrand ? Theme.Text : Theme.Ink;
            var inkSoft = onBrand ? Theme.TextSoft : Theme.InkSoft;

            var height = Mathf.Max(minHeight,
                Theme.PadXl + headHeight + Theme.PadMd + entries.Length * rowHeight + Theme.PadXl);
            var surface = onBrand ? Panel(name, parent) : Card(name, parent);
            Box(surface.Rect, position, new Vector2(width, height));

            var head = Label(surface.Rect, title, size, inkSoft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -Theme.PadXl), new Vector2(-Theme.PadXl * 2f, headHeight));

            var list = Node("Bars", surface.Rect);
            Anchor(list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(Theme.PadXl + headHeight + Theme.PadMd)),
                new Vector2(-Theme.PadXl * 2f, entries.Length * rowHeight));

            var rows = new Meters.Row[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                rows[i] = BuildMeter(list, entries[i], i, rowHeight, barHeight, size, ink, inkSoft);

            return new Meters(surface, head, rows);
        }

        private Meters.Row BuildMeter(RectTransform list, MeterEntry entry, int index, float rowHeight,
            float barHeight, float size, Color ink, Color inkSoft)
        {
            var rect = Node("Meter" + index, list);
            Anchor(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -index * rowHeight), new Vector2(0f, rowHeight));

            var caption = size * 1.5f;
            var valueWidth = Estimate(entry.Value, size) + Theme.PadMd;

            var label = Label(rect, entry.Label, size, entry.Highlight ? ink : inkSoft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-valueWidth * 0.5f, 0f), new Vector2(-valueWidth, caption));
            label.WordWrap = false;

            UniText value = null;
            if (!string.IsNullOrEmpty(entry.Value))
            {
                value = Label(rect, entry.Value, size, entry.Highlight ? ink : inkSoft,
                    HorizontalAlignment.Right, VerticalAlignment.Middle, stretch: false);
                Anchor(value.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    Vector2.zero, new Vector2(valueWidth, caption));
            }

            var track = Node("Track", rect);
            Anchor(track, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -caption - size * 0.28f), new Vector2(0f, barHeight));

            var fill = Bar(track);
            if (fill.TryGetComponent<UniShape>(out var shape))
            {
                if (entry.HasFill) Solid(FillOf(shape), entry.Fill);
                else if (!entry.Highlight) Solid(FillOf(shape), Theme.TextSoft);
            }

            return new Meters.Row(rect, label, value, fill, Mathf.Clamp01(entry.Fraction));
        }
    }
}
