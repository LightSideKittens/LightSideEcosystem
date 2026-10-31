using System.Globalization;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>A slider as an inspector draws one: a track with a knob, and beside it the figure the knob stands for.</summary>
    public readonly struct Slider
    {
        private readonly string format;

        internal Slider(RectTransform lane, RectTransform fill, Widget knob, UniText value, float min, float max,
            string format)
        {
            Lane = lane;
            Fill = fill;
            Knob = knob;
            Value = value;
            Min = min;
            Max = max;
            this.format = format;
        }

        /// <summary>The rect the knob travels along; its left edge is the knob's zero.</summary>
        public RectTransform Lane { get; }

        public RectTransform Fill { get; }

        public Widget Knob { get; }

        public UniText Value { get; }

        public float Min { get; }

        public float Max { get; }

        /// <summary>The knob's diameter, which is what a pointer aiming at it needs to know.</summary>
        public float KnobSize => Knob.Rect.sizeDelta.x;

        /// <summary>The value at <paramref name="t"/> of the travel.</summary>
        public float At(float t) => Mathf.LerpUnclamped(Min, Max, Mathf.Clamp01(t));

        /// <summary>
        /// Where the knob's center sits at <paramref name="t"/>, in <paramref name="space"/>'s coordinates.
        /// Measured from the built lane rather than typed, so a change of card size cannot leave a pointer aiming
        /// beside the knob.
        /// </summary>
        public Vector2 KnobAt(float t, RectTransform space)
        {
            var local = new Vector2(Lane.rect.width * Mathf.Clamp01(t), 0f);
            return space.InverseTransformPoint(Lane.TransformPoint(local));
        }

        /// <summary>Puts the knob at <paramref name="t"/> of its travel and writes the figure it stands for.</summary>
        public void Pose(float t)
        {
            t = Mathf.Clamp01(t);
            Knob.Rect.anchoredPosition = new Vector2(Lane.rect.width * t, 0f);
            Stage.Progress(Fill, t);
            Value.SetText(string.Format(CultureInfo.InvariantCulture, format, At(t)));
        }
    }

    /// <summary>Builds a <see cref="Promo.Slider"/>.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A slider filling <paramref name="parent"/> — a well, usually — reading <paramref name="min"/> at its left
        /// end and <paramref name="max"/> at its right, with the figure written by <paramref name="format"/> beside
        /// the track.
        /// </summary>
        public Slider Slider(Transform parent, float textSize, float min, float max, string format)
        {
            var figureWidth = textSize * 3.4f;
            var value = Label(parent, string.Format(CultureInfo.InvariantCulture, format, min), textSize, Theme.Text,
                HAlign.Right, VAlign.Middle, stretch: false);
            Anchor(value.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-Theme.PadMd, 0f), new Vector2(figureWidth, 0f));
            value.WordWrap = false;

            var lane = Node("Lane", parent);
            Anchor(lane, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(Theme.PadLg, 0f),
                new Vector2(-(Theme.PadLg + figureWidth + Theme.PadMd * 2f + textSize * 0.5f), textSize * 0.3f));

            var fill = Bar(lane);

            var knob = Shape("Knob", lane, ShapeKind.Circle);
            Box(knob.Rect, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (textSize * 0.95f));
            Solid(knob.Fill, Color.white);
            AddShadow(knob.Shape, Theme.Shadow, new Vector2(0f, -3f), 10f);
            Cursors.Add(knob.Rect, CursorType.Link);

            return new Slider(lane, fill, knob, value, min, max, format);
        }
    }
}
