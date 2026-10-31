using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The oversized, simplified component card the showreel adds to an object: a header and a short stack of fields.
    /// </summary>
    /// <remarks>
    /// Not a copy of the Unity inspector. It carries the three rows the film talks about and nothing else, at four
    /// times the size a real one would be, because the shot has to be read at a glance on a phone rather than
    /// inspected — an accurate reproduction would be illegible and would invite the viewer to read the parts that do
    /// not matter.
    /// </remarks>
    public sealed class Inspector
    {
        /// <summary>One labeled field: its caption, its well, and whatever sits inside.</summary>
        public readonly struct Field
        {
            internal Field(RectTransform rect, UniText label, Widget well, StrokeLayer ring, UniText value)
            {
                Rect = rect;
                Label = label;
                Well = well;
                Ring = ring;
                Value = value;
            }

            public RectTransform Rect { get; }
            public UniText Label { get; }
            public Widget Well { get; }

            /// <summary>The well's outline, for a shot that wants the field to look focused or picked out.</summary>
            public StrokeLayer Ring { get; }

            /// <summary>The text inside the well. Empty on a field the film is about to fill.</summary>
            public UniText Value { get; }
        }

        private readonly Field[] fields;

        internal Inspector(Widget card, CanvasGroup group, UniText title, Widget icon, Field[] fields)
        {
            Card = card;
            Group = group;
            Title = title;
            Icon = icon;
            this.fields = fields;
        }

        public Widget Card { get; }

        public CanvasGroup Group { get; }

        public UniText Title { get; }

        /// <summary>The rounded square left of the title, standing in for a component icon.</summary>
        public Widget Icon { get; }

        public RectTransform Rect => Card.Rect;

        public Field this[int index] => fields[index];

        public int Count => fields.Length;

        /// <summary>
        /// Drops the card in on a spring and deals its fields out one after another.
        /// </summary>
        /// <remarks>
        /// The card leads and the rows follow, rather than everything arriving together: a stack that assembles in
        /// front of the viewer reads as being built, and a stack that appears whole reads as a screenshot.
        /// </remarks>
        public void Pose(float t)
        {
            var card = Mathf.Clamp01(Spring.Pop.Evaluate(t));
            Group.alpha = Mathf.Clamp01(t / Wake);
            Stage.Scale(Rect, Mathf.LerpUnclamped(0.86f, 1f, card));

            for (var i = 0; i < fields.Length; i++)
            {
                var row = Motion.Back.Window(t, Lead + i * Deal, RowIn);
                var rect = fields[i].Rect;

                rect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(Slide, 0f, row), rect.anchoredPosition.y);
                Stage.Alpha(fields[i].Label, row);
                Stage.Alpha(fields[i].Well.Shape, row);
                if (fields[i].Value) Stage.Alpha(fields[i].Value, row);
            }
        }

        private const float Wake = 0.12f;
        private const float Lead = 0.16f;
        private const float Deal = 0.09f;
        private const float RowIn = 0.42f;
        private const float Slide = 90f;
    }
}
