using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>How a comparison lays its engines out.</summary>
    public enum VersusFlow
    {
        /// <summary>Side by side. Fits a paragraph; the type has to be small to fit three of them across.</summary>
        Columns,

        /// <summary>
        /// Stacked. A row is nearly the full width, so one short line can be set several times larger.
        /// </summary>
        Rows
    }

    /// <summary>Builds the comparison rig.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// One surface per engine, laid out by <paramref name="flow"/>, each titled and carrying its verdict pill.
        /// </summary>
        /// <remarks>
        /// Every measurement derives from the content band, so the rig keeps clear of a headline instead of running
        /// under it. <paramref name="inset"/> is the margin as a fraction of the frame's height, applied only to an
        /// edge still at the frame's own — a <see cref="Claim"/> reserves its band with clearance already in it, and
        /// adding a second margin under one spends the height twice and takes it off the text.
        /// <para>
        /// The two flows are not interchangeable. Columns divide the width, so three of them leave a third of the
        /// frame each and a paragraph has to be set small; rows divide the height, so a row is nearly full width and
        /// a single line can be set large enough to read from a couch. Pick by how much text the case carries, not
        /// by taste.
        /// </para>
        /// </remarks>
        public VersusRig Versus(Transform parent, VersusEntry[] entries, VersusFlow flow = VersusFlow.Columns,
            float inset = 0.055f, float gap = 0.026f)
        {
            var margin = Height * inset;
            var gutter = Height * gap;
            var top = ContentTop - (Mathf.Approximately(ContentTop, Half.y) ? margin : 0f);
            var bottom = ContentBottom + (Mathf.Approximately(ContentBottom, -Half.y) ? margin : 0f);

            return flow == VersusFlow.Rows
                ? BuildRows(parent, entries, top, bottom, margin, gutter)
                : BuildColumns(parent, entries, top, bottom, margin, gutter);
        }

        private VersusRig BuildColumns(Transform parent, VersusEntry[] entries, float top, float bottom,
            float margin, float gutter)
        {
            var chipRow = ChipHeight + gutter;
            bottom += chipRow;

            var span = (Width - margin * 2f - gutter * (entries.Length - 1)) / entries.Length;
            var columns = new VersusRig.Column[entries.Length];

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var left = -Half.x + margin + i * (span + gutter);

                var surface = Surface(entry, "Column" + i, parent);
                Box(surface.Rect, CenterAnchor, new Vector2(0f, 0.5f), new Vector2(left, (top + bottom) * 0.5f),
                    new Vector2(span, top - bottom));

                var title = Label(surface.Rect, entry.Title, Theme.Small, InkSoftOf(entry),
                    HAlign.Center, VAlign.Middle, false);
                Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -Theme.PadLg), new Vector2(-Theme.PadLg * 2f, Theme.Small * 1.8f));

                var body = Node("Body", surface.Rect);
                Stretch(body, Theme.PadLg, Theme.PadLg, Theme.PadLg, Theme.PadLg + Theme.Small * 2.4f);

                var verdict = Chip("Verdict" + i, parent, entry.Verdict, entry.VerdictFill, Color.white, out var label);
                Box(verdict.Rect, CenterAnchor, CenterAnchor,
                    new Vector2(left + span * 0.5f, bottom - gutter - ChipHeight * 0.5f),
                    new Vector2(ChipWidth(entry.Verdict), ChipHeight));

                columns[i] = new VersusRig.Column(surface, Group(surface.Rect), body, title, verdict, label);
            }

            return new VersusRig(columns);
        }

        /// <remarks>
        /// The verdict rides in the row's own header rather than under it, and the header is exactly one pill tall.
        /// A row is short to begin with — three of them divide the content band — so every point spent on chrome is
        /// taken straight off the type, which is the whole reason to stack rather than to column.
        /// </remarks>
        private VersusRig BuildRows(Transform parent, VersusEntry[] entries, float top, float bottom,
            float margin, float gutter)
        {
            var span = (top - bottom - gutter * (entries.Length - 1)) / entries.Length;
            var head = ChipHeight;
            var edge = Theme.PadMd;
            var width = Width - margin * 2f;
            var columns = new VersusRig.Column[entries.Length];

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var chip = ChipWidth(entry.Verdict);
                var reserved = Theme.PadLg + chip + Theme.PadMd;

                var surface = Surface(entry, "Row" + i, parent);
                Box(surface.Rect, CenterAnchor, new Vector2(0.5f, 1f), new Vector2(0f, top - i * (span + gutter)),
                    new Vector2(width, span));

                var title = Label(surface.Rect, entry.Title, Theme.Small, InkSoftOf(entry),
                    HAlign.Left, VAlign.Middle, false);
                Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2((Theme.PadLg - reserved) * 0.5f, -edge),
                    new Vector2(-(Theme.PadLg + reserved), head));

                var verdict = Chip("Verdict" + i, surface.Rect, entry.Verdict, entry.VerdictFill,
                    Color.white, out var label);
                Anchor(verdict.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-Theme.PadLg, -edge), new Vector2(chip, ChipHeight));

                var body = Node("Body", surface.Rect);
                Stretch(body, Theme.PadLg, edge, Theme.PadLg, edge + head + Theme.PadSm);

                columns[i] = new VersusRig.Column(surface, Group(surface.Rect), body, title, verdict, label);
            }

            return new VersusRig(columns);
        }

        private Widget Surface(in VersusEntry entry, string name, Transform parent) =>
            entry.IsProduct ? Panel(name, parent) : Card(name, parent);

        private Color InkSoftOf(in VersusEntry entry) => entry.IsProduct ? Theme.TextSoft : Theme.InkSoft;

        private static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
    }
}
