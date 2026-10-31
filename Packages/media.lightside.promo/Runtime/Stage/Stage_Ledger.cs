using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>Builds a <see cref="Ledger"/>.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A titled panel of rows, centered on <paramref name="position"/> and <paramref name="width"/> wide.
        /// </summary>
        /// <remarks>
        /// The panel sizes its own height from its content. A caller cannot know how tall a list of rows is without
        /// repeating the arithmetic, and a guessed height is how a last row ends up hanging below the panel it is
        /// supposed to be inside.
        /// <para>
        /// Rows never wrap. A row is a fixed slot in a stack laid out by index, so a label that wrapped would grow
        /// past its own slot and sit on top of the next one — the entry would still be readable and the panel would
        /// not be. Keep a label short enough for the width left beside its badge and its value.
        /// </para>
        /// </remarks>
        public Ledger Ledger(string name, Transform parent, string title, LedgerEntry[] entries,
            Vector2 position, float width, bool onBrand = false, float textSize = 0f, float minHeight = 0f)
        {
            var size = textSize > 0f ? textSize : Theme.Small;
            var rowHeight = size * 2.1f;
            var headHeight = size * 1.7f;
            var ink = onBrand ? Theme.Text : Theme.Ink;
            var inkSoft = onBrand ? Theme.TextSoft : Theme.InkSoft;

            var height = Mathf.Max(minHeight,
                Theme.PadXl + headHeight + Theme.PadMd + entries.Length * rowHeight + Theme.PadXl);
            var surface = onBrand ? Panel(name, parent) : Card(name, parent);
            Box(surface.Rect, position, new Vector2(width, height));

            var head = Label(surface.Rect, title, size, inkSoft, HAlign.Left, VAlign.Middle, false);
            Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -Theme.PadXl), new Vector2(-Theme.PadXl * 2f, headHeight));

            var list = Node("Rows", surface.Rect);
            Anchor(list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(Theme.PadXl + headHeight + Theme.PadMd)),
                new Vector2(-Theme.PadXl * 2f, entries.Length * rowHeight));

            var rows = new Ledger.Row[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                rows[i] = BuildRow(list, entries[i], i, rowHeight, size, ink, inkSoft);

            return new Ledger(surface, head, rows);
        }

        private Ledger.Row BuildRow(RectTransform list, LedgerEntry entry, int index, float rowHeight,
            float size, Color ink, Color inkSoft)
        {
            var rect = Node("Row" + index, list);
            Anchor(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -index * rowHeight), new Vector2(0f, rowHeight));

            Widget badge = default;
            UniText badgeLabel = null;
            var left = 0f;

            if (!string.IsNullOrEmpty(entry.Badge))
            {
                var badgeWidth = size * 2.2f;
                badge = Chip("Badge", rect, entry.Badge, entry.BadgeFill, Color.white, out badgeLabel);
                Box(badge.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                    new Vector2(badgeWidth, size * 1.6f));
                left = badgeWidth + Theme.PadMd;
            }

            UniText trailing = null;
            var right = 0f;
            if (!string.IsNullOrEmpty(entry.Trailing))
            {
                right = Estimate(entry.Trailing, size) + Theme.PadLg;
                trailing = Label(rect, entry.Trailing, size, inkSoft, HAlign.Right, VAlign.Middle, false);
                Anchor(trailing.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                    Vector2.zero, new Vector2(right - Theme.PadLg, 0f));
                trailing.WordWrap = false;
            }

            var label = Label(rect, entry.Label, size, ink, HAlign.Left, VAlign.Middle, false);
            Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2((left - right) * 0.5f, 0f), new Vector2(-(left + right), 0f));
            label.WordWrap = false;

            return new Ledger.Row(rect, label, trailing, badge, badgeLabel);
        }

        /// <summary>
        /// Rough width of <paramref name="text"/> at <paramref name="size"/>, for reserving space before any layout
        /// has run.
        /// </summary>
        /// <remarks>
        /// Deliberately an estimate, and deliberately generous. Measuring needs a committed layout, which does not
        /// exist while a slide is still building; reserving slightly too much leaves a gap, while reserving too
        /// little lets a label run under the value beside it.
        /// </remarks>
        public static float Estimate(string text, float size) =>
            string.IsNullOrEmpty(text) ? 0f : text.Length * size * 0.6f;
    }
}
