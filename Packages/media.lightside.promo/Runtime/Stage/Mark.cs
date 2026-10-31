using System;
using UnityEngine;
using UnityEngine.UI;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>What an annotation draws over the thing it is pointing at.</summary>
    public enum MarkKind
    {
        /// <summary>An ellipse around it, sized by the mark's own box.</summary>
        Ring,

        /// <summary>A stroke under it, drawn left to right.</summary>
        Underline,

        /// <summary>Words beside it, saying what is wrong.</summary>
        Note
    }

    /// <summary>What an annotation says about the thing it is pointing at.</summary>
    /// <remarks>
    /// A tone, not a color: the same two marks over the same two places carry the whole comparison, and which
    /// green or which red they are drawn in belongs to the livery rather than to the shot.
    /// </remarks>
    public enum MarkTone
    {
        /// <summary>This is wrong.</summary>
        Fault,

        /// <summary>This is right.</summary>
        Pass
    }

    /// <summary>
    /// One annotation, authored against the panel it covers rather than against the frame.
    /// </summary>
    /// <remarks>
    /// <see cref="center"/> and <see cref="size"/> are fractions of that panel, 0 at its left and bottom edges and 1
    /// at its right and top. They become the mark's anchors, so nothing is measured and a panel that changes size
    /// carries its marks with it.
    /// <para>
    /// A panel is not its text. A line the engine has aligned to one side leaves the rest of the panel empty, so the
    /// fraction that lands on the last glyph is nowhere near 0 or 1 and there is no way to reach it but to look.
    /// </para>
    /// <para>
    /// Where a mark belongs is not derivable. Which glyph an engine put in the wrong place is visible in a rendered
    /// frame and nowhere else, so these are authored by eye against a contact sheet and nudged in the inspector.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct MarkSpec
    {
        public MarkKind kind;
        public MarkTone tone;

        /// <summary>Which panel of the rig it covers, counted the way the panels are laid out.</summary>
        [Min(0)] public int panel;

        public Vector2 center;
        public Vector2 size;

        /// <summary>What a <see cref="MarkKind.Note"/> says; ignored by the other kinds.</summary>
        [TextArea(1, 3)] public string note;

        /// <summary>When it arrives, in seconds from the slide's start.</summary>
        [Min(0f)] public float at;
    }

    /// <summary>A built annotation.</summary>
    public readonly struct Mark
    {
        private readonly MarkKind kind;
        private readonly Component visual;
        private readonly RectTransform rect;

        internal Mark(MarkKind kind, Component visual, RectTransform rect)
        {
            this.kind = kind;
            this.visual = visual;
            this.rect = rect;
        }

        /// <summary>The graphic or shape the mark draws.</summary>
        public Component Visual => visual;

        /// <summary>
        /// Draws the mark over <paramref name="t"/> seconds since it was called for; nothing before 0.
        /// </summary>
        /// <remarks>
        /// A <see cref="MarkKind.Note"/> arrives on opacity alone. It is there to be read, and a line of type still
        /// settling is a line nobody starts reading until it stops.
        /// </remarks>
        public void Pose(float t)
        {
            Stage.Alpha(visual, Ease.EmphasizedIn.Window(t, 0f, Snap));

            switch (kind)
            {
                case MarkKind.Underline:
                    Stage.Progress(rect, Ease.EmphasizedIn.Window(t, 0f, Draw));
                    break;

                case MarkKind.Ring:
                    Stage.Scale(rect, Mathf.LerpUnclamped(Wide, 1f, Mathf.Clamp01(Spring.Bouncy.Evaluate(t))));
                    break;
            }
        }

        /// <summary>How far outside its resting size a ring starts, so it closes onto the glyph rather than fading on.</summary>
        private const float Wide = 1.45f;

        private const float Snap = 0.14f;
        private const float Draw = 0.32f;
    }

    /// <summary>Builds the annotations a comparison points its failures out with.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// An annotation over <paramref name="panel"/>, positioned by <paramref name="spec"/>'s fractions of it.
        /// </summary>
        /// <remarks>
        /// Build marks after whatever they annotate. Nothing here clips or sorts, so a mark created before the text
        /// it circles is drawn underneath it.
        /// </remarks>
        public Mark Mark(RectTransform panel, in MarkSpec spec)
        {
            var half = spec.size * 0.5f;
            var min = spec.center - half;
            var max = spec.center + half;
            var paint = spec.tone == MarkTone.Pass ? Theme.Green : Theme.Coral;

            switch (spec.kind)
            {
                case MarkKind.Note:
                {
                    var note = Label(panel, spec.note, Theme.Body, paint, HAlign.Left, VAlign.Middle, false);
                    Anchor(note.rectTransform, min, max, CenterAnchor, Vector2.zero, Vector2.zero);

                    note.MaxFontSize = Theme.Body;
                    note.MinFontSize = Theme.Body * NoteFit;
                    note.AutoSize = true;
                    return new Mark(MarkKind.Note, note, note.rectTransform);
                }

                case MarkKind.Underline:
                {
                    var track = Node("Underline", panel);
                    Anchor(track, min, max, CenterAnchor, Vector2.zero, Vector2.zero);

                    var stroke = Shape("Stroke", track, ShapeKind.Capsule);
                    Anchor(stroke.Rect, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                        Vector2.zero, Vector2.zero);
                    Solid(stroke.Fill, paint);

                    return new Mark(MarkKind.Underline, stroke.Shape, stroke.Rect);
                }

                default:
                {
                    var ring = Shape("Ring", panel, ShapeKind.Ring);
                    ring.Outline.Thickness = RingThickness;
                    Anchor(ring.Rect, min, max, CenterAnchor, Vector2.zero, Vector2.zero);
                    Solid(ring.Fill, paint);

                    return new Mark(MarkKind.Ring, ring.Shape, ring.Rect);
                }
            }
        }

        /// <summary>How far below <see cref="Promo.Theme.Body"/> a note may shrink to fit its box.</summary>
        /// <remarks>
        /// A note's words are authored and its box is not, so the size is a ceiling rather than a size — the same
        /// bargain a showcase body strikes, for the same reason: the wording changes long after the box was placed.
        /// </remarks>
        private const float NoteFit = 0.55f;

        /// <summary>Stroke width of a ring, as a fraction of its radius.</summary>
        /// <remarks>
        /// Heavy on purpose. A ring is drawn over live text at reading size and read as an annotation laid on top of
        /// the frame, not as part of it; a hairline reads as a stray shape instead.
        /// </remarks>
        private const float RingThickness = 0.18f;
    }
}
