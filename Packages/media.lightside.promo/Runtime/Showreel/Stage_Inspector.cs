using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>Builds the showreel's component card.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A component card carrying one field per entry of <paramref name="labels"/>, sized to
        /// <paramref name="size"/> and centered on <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="weights"/> divides the height left under the header between the rows; null gives them
        /// equal shares. A list-shaped field needs several times the height of a single-value one, and the caller is
        /// the only party that knows which is which.
        /// <para>
        /// A row whose <paramref name="values"/> entry is empty gets no text object at all, rather than an empty one.
        /// A <see cref="UniText"/> added in the editor seeds itself from the project's text prefab — color, size and
        /// content — so an empty label is not blank; it shows whatever that prefab says, in whatever color, for the
        /// whole shot.
        /// </para>
        /// </remarks>
        public Inspector Inspector(string name, Transform parent, string title, string[] labels,
            Vector2 position, Vector2 size, float textSize = 0f, string[] values = null, float[] weights = null)
        {
            var body = textSize > 0f ? textSize : Theme.Head;
            var card = Panel(name, parent, Theme.RadiusXxl);
            Box(card.Rect, position, size);
            var group = Group(card.Rect);

            var header = body * 1.7f;
            var icon = Shape(name + " Icon", card.Rect, ShapeKind.RoundedRect, Theme.RadiusMd);
            Anchor(icon.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Theme.PadXl, -Theme.PadXl), new Vector2(header, header));
            Ramped(icon.Fill, Theme.Brand, PaintProjectionKind.Linear, 45f);

            var head = Label(card.Rect, title, body, Theme.Text, HAlign.Left, VAlign.Middle,
                stretch: false, face: Theme.DisplayFace);
            Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2((header + Theme.PadLg) * 0.5f, -Theme.PadXl),
                new Vector2(-(Theme.PadXl * 2f + header + Theme.PadLg), header));
            head.WordWrap = false;

            var top = Theme.PadXl + header + Theme.PadXl;
            var room = size.y - top - Theme.PadXl;
            var total = 0f;

            for (var i = 0; i < labels.Length; i++) total += Weight(weights, i);

            var rows = new Inspector.Field[labels.Length];
            var y = top;

            for (var i = 0; i < labels.Length; i++)
            {
                var height = room * Weight(weights, i) / total;
                var value = values != null && i < values.Length ? values[i] : null;
                rows[i] = BuildField(card.Rect, labels[i], i, y, height, body, value);
                y += height;
            }

            return new Inspector(card, group, head, icon, rows);
        }

        private static float Weight(float[] weights, int index) =>
            weights != null && index < weights.Length && weights[index] > 0f ? weights[index] : 1f;

        private Inspector.Field BuildField(RectTransform card, string label, int index,
            float top, float rowHeight, float body, string value)
        {
            var rect = Node("Field" + index, card);
            Anchor(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -top), new Vector2(-Theme.PadXl * 2f, rowHeight));

            var gap = Mathf.Min(rowHeight * 0.16f, Theme.PadMd);
            var caption = Label(rect, label, body * 0.72f, Theme.TextSoft, HAlign.Left, VAlign.Top, stretch: false);
            Anchor(caption.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(LabelWidth * body, -gap));
            caption.WordWrap = false;

            var well = Field("Well" + index, rect, out var ring, Theme.RadiusLg);
            Anchor(well.Rect, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(LabelWidth * body * 0.5f, 0f),
                new Vector2(-LabelWidth * body, -gap));

            UniText text = null;
            if (!string.IsNullOrEmpty(value))
            {
                text = Label(well.Rect, value, body, Theme.Text, HAlign.Left, VAlign.Middle);
                Stretch(text.rectTransform, Theme.PadLg, 0f, Theme.PadLg, 0f);
                text.WordWrap = false;
            }

            return new Inspector.Field(rect, caption, well, ring, text);
        }

        /// <summary>Label column width, in multiples of the card's type size.</summary>
        private const float LabelWidth = 3.1f;
    }
}
