using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Compiles an ordered list of <see cref="Beat"/> into a pointer path and the named time spans the rest of a
    /// slide reacts in.
    /// </summary>
    /// <remarks>
    /// One list, two readings. Never author a pointer path and a UI reaction separately: ask for a span or a mark
    /// by name and the two cannot drift apart.
    /// <para>
    /// A drag is a press, a travel and a release — not a click at each end. The button stays down for the whole
    /// leg and the pointer must render that held state continuously, or a selection looks like it is happening by
    /// itself.
    /// </para>
    /// </remarks>
    public sealed class PointerTimeline
    {
        /// <summary>Where the pointer is, how fast, and how far the button is pressed.</summary>
        public readonly struct Pose
        {
            public Pose(Vector2 position, float speed, float press)
            {
                Position = position;
                Speed = speed;
                Press = press;
            }

            public Vector2 Position { get; }

            /// <summary>Points per second, for anything that reacts to haste.</summary>
            public float Speed { get; }

            /// <summary>0 released, 1 fully held.</summary>
            public float Press { get; }
        }

        /// <summary>A held mouse button. A plain click is a very short one.</summary>
        public readonly struct Press
        {
            public Press(float down, float up, Vector2 point, bool accent)
            {
                Down = down;
                Up = up;
                Point = point;
                Accent = accent;
            }

            public float Down { get; }
            public float Up { get; }
            public Vector2 Point { get; }

            /// <summary>Whether the press is the payoff of its beat; see <see cref="Beat.Accent"/>.</summary>
            public bool Accent { get; }
        }

        /// <summary>A keystroke chip, placed where the pointer was when it was struck.</summary>
        public readonly struct Keystroke
        {
            public Keystroke(float at, string label, Vector2 point)
            {
                At = at;
                Label = label;
                Point = point;
            }

            public float At { get; }
            public string Label { get; }
            public Vector2 Point { get; }
        }

        private readonly struct Leg
        {
            public Leg(Vector2 from, Vector2 to, float start, float end, float bow)
            {
                From = from;
                To = to;
                Start = start;
                End = end;
                Bow = bow;
            }

            public Vector2 From { get; }
            public Vector2 To { get; }
            public float Start { get; }
            public float End { get; }
            public float Bow { get; }
        }

        private readonly List<Leg> legs = new();
        private readonly List<Press> presses = new();
        private readonly List<Keystroke> keys = new();
        private readonly Dictionary<string, Vector2> spans = new();
        private readonly Vector2 origin;
        private readonly float tempo;

        /// <summary>
        /// Compiles <paramref name="beats"/> from <paramref name="start"/>, every travel and dwell divided by
        /// <paramref name="tempo"/>: above one a practiced hand, below one a hesitant one, the motion's shape unchanged.
        /// </summary>
        public PointerTimeline(Vector2 start, IReadOnlyList<Beat> beats, float tempo = 1f)
        {
            if (!(tempo > 0f)) throw new System.ArgumentOutOfRangeException(nameof(tempo), tempo, "A pointer's tempo must be positive.");
            origin = start;
            this.tempo = tempo;

            var at = start;
            var t = 0f;
            var side = 1f;
            var auto = 0;

            for (var i = 0; i < beats.Count; i++)
            {
                var beat = beats[i];
                switch (beat.Kind)
                {
                    case BeatKind.To:
                    {
                        var end = Travel(ref at, beat.Point, beat.TargetWidth, t, ref side);
                        Put(beat.Id, t, end, "to", ref auto);

                        t = end;
                        break;
                    }

                    case BeatKind.Wait:
                        Put(beat.Id, t, t + beat.Seconds, "wait", ref auto);
                        t += beat.Seconds;
                        break;

                    case BeatKind.Click:
                    {
                        t += PointerTiming.DwellBefore / tempo;
                        var down = t;
                        t += Mathf.Max(PointerTiming.PressDown, PointerTiming.PressHold) / tempo;
                        presses.Add(new Press(down, t, at, beat.Accent));
                        Put(beat.Id, down, t, "click", ref auto);
                        t += (beat.Settles ? PointerTiming.DwellAfterChange : PointerTiming.DwellAfter) / tempo;
                        break;
                    }

                    case BeatKind.Drag:
                    {
                        t += PointerTiming.DwellBefore / tempo;
                        var down = t;
                        var from = at;
                        t += PointerTiming.PressDown / tempo;

                        var end = Travel(ref at, beat.Point, beat.TargetWidth, t, ref side) + beat.Seconds;
                        Put(beat.Id, t, end, "drag", ref auto);
                        t = end;

                        presses.Add(new Press(down, t, from, false));
                        t += PointerTiming.DwellAfter / tempo;
                        break;
                    }

                    case BeatKind.Key:
                        t += PointerTiming.DwellBefore / tempo;
                        keys.Add(new Keystroke(t, beat.Label, at));
                        Put(beat.Id, t, t + PointerTiming.KeyHold / tempo, "key", ref auto);
                        t += PointerTiming.KeyHold / tempo;
                        break;
                }
            }

            Total = t;
        }

        /// <summary>How long the whole sequence takes.</summary>
        public float Total { get; }

        public IReadOnlyList<Press> Presses => presses;

        public IReadOnlyList<Keystroke> Keys => keys;

        /// <summary>
        /// A sound cue for every press and keystroke, named <c>click</c> and <c>key</c>, on the pointer's own clock.
        /// </summary>
        /// <remarks>
        /// Emitted from the same compiled beats that move the pointer, so the sound cannot land on a different
        /// frame from the ripple it belongs to.
        /// </remarks>
        public List<Cue> Cues()
        {
            var cues = new List<Cue>(presses.Count + keys.Count);
            for (var i = 0; i < presses.Count; i++) cues.Add(new Cue("click", presses[i].Down));
            for (var i = 0; i < keys.Count; i++) cues.Add(new Cue("key", keys[i].At));
            cues.Sort((a, b) => a.At.CompareTo(b.At));
            return cues;
        }

        /// <summary>Start and end of the named beat, as x and y.</summary>
        /// <exception cref="KeyNotFoundException">The id was never declared.</exception>
        public Vector2 Span(string id)
        {
            if (spans.TryGetValue(id, out var span)) return span;
            throw new KeyNotFoundException(
                $"[Promo] Unknown pointer beat '{id}'. Declared: {string.Join(", ", spans.Keys)}");
        }

        /// <summary>When the named beat starts.</summary>
        public float Mark(string id) => Span(id).x;

        /// <summary>How far through the named beat <paramref name="seconds"/> is, eased.</summary>
        public float Progress(string id, float seconds)
        {
            var span = Span(id);
            if (span.y <= span.x) return seconds >= span.x ? 1f : 0f;
            return Ease.Emphasized.Evaluate((seconds - span.x) / (span.y - span.x));
        }

        /// <summary>Where the pointer is at <paramref name="seconds"/>, and what it is doing.</summary>
        /// <remarks>
        /// Between legs the leg search selects the leg <em>ahead</em> and clamps its parameter to zero, so speed is
        /// reported as zero outside a leg's own window rather than sampling forward into motion that has not begun.
        /// </remarks>
        public Pose Sample(float seconds)
        {
            var press = 0f;
            for (var i = 0; i < presses.Count; i++)
                press = Mathf.Max(press, PressAmount(seconds, presses[i]));

            if (legs.Count == 0) return new Pose(origin, 0f, press);

            var leg = legs[legs.Count - 1];
            var t = 1f;
            if (seconds <= legs[0].Start)
            {
                leg = legs[0];
                t = 0f;
            }
            else
            {
                for (var i = 0; i < legs.Count; i++)
                {
                    if (seconds > legs[i].End) continue;
                    leg = legs[i];
                    t = Mathf.Clamp01((seconds - leg.Start) / Mathf.Max(1e-4f, leg.End - leg.Start));
                    break;
                }
            }

            var here = Bend(leg, t);

            const float step = 1f / 120f;
            var speed = 0f;
            if (seconds >= leg.Start && seconds < leg.End)
            {
                var ahead = Bend(leg, Mathf.Clamp01(t + step / Mathf.Max(1e-4f, leg.End - leg.Start)));
                speed = Vector2.Distance(here, ahead) / step;
            }

            return new Pose(here, speed, press);
        }


        /// <summary>How far the button is pressed during <paramref name="press"/> at <paramref name="seconds"/>.</summary>
        public static float PressAmount(float seconds, Press press)
        {
            if (seconds < press.Down) return 0f;
            if (seconds < press.Down + PointerTiming.PressDown)
                return (seconds - press.Down) / PointerTiming.PressDown;
            if (seconds <= press.Up) return 1f;
            if (seconds < press.Up + PointerTiming.PressUp)
                return 1f - (seconds - press.Up) / PointerTiming.PressUp;
            return 0f;
        }

        private float Travel(ref Vector2 at, Vector2 to, float targetWidth, float start, ref float side)
        {
            var distance = Vector2.Distance(at, to);
            var duration = PointerTiming.TravelSeconds(distance, targetWidth) / tempo;
            var bow = Mathf.Min(PointerTiming.BowMax, distance * PointerTiming.BowFraction) * side;

            legs.Add(new Leg(at, to, start, start + duration, bow));
            side = -side;
            at = to;
            return start + duration;
        }

        private void Put(string id, float from, float to, string kind, ref int auto)
        {
            spans[id ?? $"{kind}{auto++}"] = new Vector2(from, to);
        }

        /// <summary>
        /// A cubic bow with both control points on the same side of the chord — opposite sides make an S, which no
        /// hand has ever drawn.
        /// </summary>
        private static Vector2 Bend(Leg leg, float t)
        {
            var eased = PointerTiming.CurveFor(Vector2.Distance(leg.From, leg.To)).Evaluate(t);

            var delta = leg.To - leg.From;
            var length = Mathf.Max(1e-4f, delta.magnitude);
            var normal = new Vector2(-delta.y / length, delta.x / length) * leg.Bow;

            var c1 = leg.From + delta / 3f + normal;
            var c2 = leg.From + delta * (2f / 3f) + normal;
            return BezierPath.EvaluateCubic(leg.From, c1, c2, leg.To, eased);
        }
    }
}
