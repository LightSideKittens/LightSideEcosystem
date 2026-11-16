using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Which part of a window row a take beat aims at.</summary>
    public enum TakePart
    {
        /// <summary>The row's value box, at a fraction of its width.</summary>
        Value,

        /// <summary>The row's slider knob, where it sits at a slider value.</summary>
        Slider
    }

    /// <summary>
    /// One step of a take script: a pointer beat aimed at a row of the recorded window rather than at a point.
    /// </summary>
    /// <remarks>
    /// A row is named by its label as the window shows it. The recorder resolves it against the live window into a
    /// point of the slide's stage, so a script survives any change to the window's layout and the replayed pointer
    /// lands exactly where the driven one did.
    /// </remarks>
    public readonly struct TakeBeat
    {
        private TakeBeat(BeatKind kind, string row, TakePart part, float along, float lands, string id, float seconds,
            bool settles)
        {
            Kind = kind;
            Row = row;
            Part = part;
            Along = along;
            Lands = lands;
            Id = id;
            Seconds = seconds;
            Settles = settles;
        }

        /// <summary>What the pointer does.</summary>
        public BeatKind Kind { get; }

        /// <summary>Label of the targeted row, or null for a beat that stays where the pointer is.</summary>
        public string Row { get; }

        /// <summary>Which part of the row is aimed at.</summary>
        public TakePart Part { get; }

        /// <summary>A fraction of the value box's width, or the slider value whose knob position is aimed at.</summary>
        public float Along { get; }

        /// <summary>The number a scrub of a value box ends on.</summary>
        public float Lands { get; }

        /// <summary>Name the beat's span is looked up by, or null.</summary>
        public string Id { get; }

        /// <summary>How long a wait lasts.</summary>
        public float Seconds { get; }

        /// <summary>Whether a click changes the interface and earns the longer hold after it.</summary>
        public bool Settles { get; }

        /// <summary>Travels to a part of <paramref name="row"/>.</summary>
        public static TakeBeat To(string row, TakePart part, float along, string id = null) =>
            new TakeBeat(BeatKind.To, row, part, along, 0f, id, 0f, false);

        /// <summary>
        /// Presses where the pointer is, scrubs <paramref name="row"/>'s value box to <paramref name="along"/> of its
        /// width and releases there, the number running in whole steps to <paramref name="lands"/>.
        /// </summary>
        /// <remarks>
        /// The recorder writes the number in step with the pointer's travel, so the scrub ends on
        /// <paramref name="lands"/> however far the pointer goes.
        /// </remarks>
        public static TakeBeat Scrub(string row, float along, float lands, string id) =>
            new TakeBeat(BeatKind.Drag, row, TakePart.Value, along, lands, id, 0f, false);

        /// <summary>
        /// Presses where the pointer is, drags <paramref name="row"/>'s slider knob to where it sits at
        /// <paramref name="value"/> and releases there.
        /// </summary>
        /// <remarks>The recorder holds the slider to hundredths while it is dragged.</remarks>
        public static TakeBeat Drag(string row, float value, string id) =>
            new TakeBeat(BeatKind.Drag, row, TakePart.Slider, value, 0f, id, 0f, false);

        /// <summary>Clicks where the pointer is.</summary>
        public static TakeBeat Click(string id = null, bool settles = false) =>
            new TakeBeat(BeatKind.Click, null, TakePart.Value, 0f, 0f, id, 0f, settles);

        /// <summary>Holds still.</summary>
        public static TakeBeat Wait(float seconds, string id = null) =>
            new TakeBeat(BeatKind.Wait, null, TakePart.Value, 0f, 0f, id, seconds, false);

        /// <summary>The pointer beat this step becomes once its row has been resolved to <paramref name="point"/>.</summary>
        public Beat Resolve(Vector2 point) => Kind switch
        {
            BeatKind.To => Beat.To(point, Id),
            BeatKind.Drag => Beat.Drag(point, Id),
            BeatKind.Click => Beat.Click(Id, Settles),
            _ => Beat.Wait(Seconds, Id)
        };
    }
}
