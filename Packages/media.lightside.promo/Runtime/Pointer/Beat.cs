using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>What a pointer does next.</summary>
    public enum BeatKind
    {
        /// <summary>Travel to a point.</summary>
        To,

        /// <summary>Hold still.</summary>
        Wait,

        /// <summary>Press and release where the pointer already is.</summary>
        Click,

        /// <summary>Press here, travel with the button held, release at the destination.</summary>
        Drag,

        /// <summary>A keystroke, shown as a chip above the pointer.</summary>
        Key
    }

    /// <summary>
    /// One entry in the ordered list a <see cref="PointerTimeline"/> compiles.
    /// </summary>
    /// <remarks>
    /// A scene declares beats and nothing else. Both the pointer's path and every UI reaction are read back out of
    /// the compiled timeline, so the two cannot disagree about when something happened — authoring them as two
    /// lists is how a pointer ends up clicking before it arrives.
    /// </remarks>
    public readonly struct Beat
    {
        private Beat(BeatKind kind, Vector2 point, float seconds, string id, string label, float targetWidth,
            bool settles, bool accent = false)
        {
            Kind = kind;
            Point = point;
            Seconds = seconds;
            Id = id;
            Label = label;
            TargetWidth = targetWidth;
            Settles = settles;
            Accent = accent;
        }

        public BeatKind Kind { get; }

        /// <summary>This beat aimed at <paramref name="point"/> instead, at a target <paramref name="targetWidth"/> wide.</summary>
        public Beat Retargeted(Vector2 point, float targetWidth) =>
            new(Kind, point, Seconds, Id, Label, targetWidth, Settles, Accent);

        public Vector2 Point { get; }

        public float Seconds { get; }

        /// <summary>Name this beat's span is looked up by, or null for an unnamed beat.</summary>
        public string Id { get; }

        public string Label { get; }

        /// <summary>Width of the thing being aimed at, which is half of what sets the travel time.</summary>
        public float TargetWidth { get; }

        /// <summary>Whether the click changes the interface, which earns a longer hold afterwards.</summary>
        public bool Settles { get; }

        /// <summary>Whether the click is the payoff of its beat, the press that lands with an accent.</summary>
        public bool Accent { get; }


        public static Beat To(Vector2 point, string id = null, float targetWidth = 0f) =>
            new Beat(BeatKind.To, point, 0f, id, null, targetWidth, false);

        public static Beat Wait(float seconds, string id = null) =>
            new Beat(BeatKind.Wait, default, seconds, id, null, 0f, false);

        public static Beat Click(string id = null, bool settles = false, bool accent = false) =>
            new Beat(BeatKind.Click, default, 0f, id, null, 0f, settles, accent);

        /// <summary>
        /// A press dragged to <paramref name="point"/>, held there for <paramref name="hold"/> seconds — a stick pushed
        /// and kept pushed — and let go.
        /// </summary>
        public static Beat Drag(Vector2 point, string id, float targetWidth = 0f, float hold = 0f) =>
            new Beat(BeatKind.Drag, point, hold, id, null, targetWidth, false);

        public static Beat Key(string label, string id = null) =>
            new Beat(BeatKind.Key, default, 0f, id, label, 0f, false);
    }
}
