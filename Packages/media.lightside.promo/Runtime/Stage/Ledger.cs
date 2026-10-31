using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>One line of a <see cref="Ledger"/>, as data.</summary>
    public readonly struct LedgerEntry
    {
        public LedgerEntry(string label, string trailing = null, string badge = null, Color badgeFill = default)
        {
            Label = label;
            Trailing = trailing;
            Badge = badge;
            BadgeFill = badgeFill;
        }

        public string Label { get; }
        public string Trailing { get; }
        public string Badge { get; }
        public Color BadgeFill { get; }
    }

    /// <summary>
    /// A titled panel of labeled rows, each optionally carrying a status pill and a trailing value.
    /// </summary>
    /// <remarks>
    /// The film needs the same object three times — a source-control file list, a stack of paint layers, a payload
    /// manifest — and in each case the point is that a viewer watches rows <em>arrive</em>. Rows are therefore posed
    /// individually rather than faded as a block.
    /// </remarks>
    public sealed class Ledger
    {
        /// <summary>A built row.</summary>
        public readonly struct Row
        {
            internal Row(RectTransform rect, UniText label, UniText trailing, Widget badge, UniText badgeLabel)
            {
                Rect = rect;
                Label = label;
                Trailing = trailing;
                Badge = badge;
                BadgeLabel = badgeLabel;
            }

            public RectTransform Rect { get; }
            public UniText Label { get; }
            public UniText Trailing { get; }
            public Widget Badge { get; }
            public UniText BadgeLabel { get; }

            public bool HasBadge => BadgeLabel;
        }

        private readonly Row[] rows;

        internal Ledger(Widget surface, UniText title, Row[] rows)
        {
            Surface = surface;
            Title = title;
            this.rows = rows;
        }

        public Widget Surface { get; }

        /// <summary>The panel's height, so a second ledger can be built to match it.</summary>
        public float Height => Surface.Rect.rect.height;

        public UniText Title { get; }

        public IReadOnlyList<Row> Rows => rows;

        public Row this[int index] => rows[index];

        public int Count => rows.Length;

        /// <summary>
        /// Poses row <paramref name="index"/> at <paramref name="t"/> in 0..1 of its arrival: it slides in from the
        /// left and fades up together.
        /// </summary>
        public void Pose(int index, float t)
        {
            var row = rows[index];
            var k = Mathf.Clamp01(t);

            row.Rect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-Slide, 0f, k), row.Rect.anchoredPosition.y);
            Fade(index, k);
        }

        /// <summary>
        /// Sets row <paramref name="index"/>'s opacity, leaving it where it is.
        /// </summary>
        /// <remarks>
        /// A row that is already in place and merely quiet, rather than one arriving: a shot whose rows are a
        /// standing list the eye returns to needs the opacity without the travel that <see cref="Pose"/> couples to
        /// it.
        /// </remarks>
        public void Fade(int index, float alpha)
        {
            var row = rows[index];

            Stage.Alpha(row.Label, alpha);
            if (row.Trailing) Stage.Alpha(row.Trailing, alpha);
            if (!row.HasBadge) return;

            Stage.Alpha(row.Badge.Shape, alpha);
            Stage.Alpha(row.BadgeLabel, alpha);
        }

        /// <summary>Replaces a row's trailing value, for a count that climbs.</summary>
        public void SetTrailing(int index, string value)
        {
            var row = rows[index];
            if (row.Trailing) row.Trailing.SetText(value);
        }

        private const float Slide = 40f;
    }
}
