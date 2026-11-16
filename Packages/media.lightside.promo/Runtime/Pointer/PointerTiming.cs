using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Every number a pointer's motion is derived from. Nothing that moves a pointer may type a constant of its own.
    /// </summary>
    /// <remarks>
    /// Two kinds of duration live here and they must not be confused. <b>Pacing</b> — travel and dwell — may be
    /// tuned for a cut. <b>Perception floors</b> — the press, its release and the ripple — are how long a human
    /// needs to register that something happened, and shortening them does not make a film faster, it makes the
    /// click disappear.
    /// </remarks>
    public static class PointerTiming
    {
        /// <summary>
        /// Fitts's law in Shannon form, <c>MT = A + B·log2(D / W + 1)</c>, in seconds.
        /// </summary>
        /// <remarks>
        /// The researched screencast fit is A 0.180 / B 0.120. These are tightened because a promo trades realism
        /// for pace; restore the screencast pair for a tutorial.
        /// </remarks>
        public const float FittsA = 0.200f;
        public const float FittsB = 0.140f;

        /// <summary>Assumed target width when a beat does not name one.</summary>
        public const float TargetWidth = 64f;

        public const float MinTravel = 0.38f;
        public const float MaxTravel = 1.10f;

        /// <summary>
        /// Ceiling on peak speed, in points per second. Raising the travel time to respect it is the only automatic
        /// slowdown in the system.
        /// </summary>
        /// <remarks>
        /// Keep it high. At 1600 pt/s a 1650 pt move demands nearly two seconds and overrides Fitts entirely, and a
        /// long eased arc reads as <em>linear</em> because its acceleration is spread too thin to notice. If a move
        /// still strobes, shorten the distance in the layout rather than lengthening the time.
        /// </remarks>
        public const float PeakSpeed = 3400f;

        /// <summary>Sideways bow of a leg, as a fraction of its length and an absolute ceiling.</summary>
        public const float BowFraction = 0.05f;
        public const float BowMax = 60f;

        /// <summary>Legs shorter than this get a gentler curve, so a small hop does not read as a twitch.</summary>
        public const float ShortLeg = 260f;

        public const float DwellBefore = 0.19f;
        public const float DwellAfter = 0.24f;

        /// <summary>Hold after a click that changed the interface, so the change can be read.</summary>
        public const float DwellAfterChange = 0.46f;

        public const float KeyHold = 0.43f;

        /// <summary>Mouse-down to mouse-up on a plain click.</summary>
        public const float PressHold = 0.11f;

        /// <summary>Perception floors. Never shorten these to speed a cut up.</summary>
        public const float PressDown = 0.08f;
        public const float PressUp = 0.19f;

        /// <summary>Ripple: two rings, the second chasing the first, because one expanding circle reads as a dot.</summary>
        public const float RingSeconds = 0.46f;
        public const float RingDelay = 0.09f;
        public const float RingRadius = 86f;
        public const float RingOpacity = 0.5f;
        public const float RingSecondScale = 0.72f;

        /// <summary>
        /// Radius a ring starts at: clear of the arrow's tip, so its first frames read as a ring opening around the
        /// press rather than as a dot stuck to the tip.
        /// </summary>
        public const float RingStartRadius = 20f;

        /// <summary>Width of a ring's band, in points, whatever its radius.</summary>
        public const float RingBand = 4f;

        /// <summary>Fraction of a ring's life it takes to appear.</summary>
        public const float RingFadeIn = 0.12f;

        /// <summary>How far the pointer shrinks while held. Beyond about 15% the tip visibly drifts.</summary>
        public const float PressScale = 0.86f;

        /// <summary>Travel curve for a leg of <paramref name="distance"/> points.</summary>
        /// <remarks>
        /// A pointer is a hand, and a hand leaves at rest, hurries through the middle and settles at rest. Both
        /// curves are therefore symmetric ease-in-out, not the decelerate profile an arriving <em>object</em> wants:
        /// the long one peaks near three times its mean speed, which is the contrast that reads as haste without
        /// the arrow ever appearing to teleport.
        /// <para>
        /// Minimum-jerk is the scientifically correct reaching curve and is wrong here for the opposite reason — its
        /// velocity bell is too broad to notice, so a leg looks linear. The handles are pulled out until the middle
        /// is visibly faster than the ends.
        /// </para>
        /// </remarks>
        public static Ease CurveFor(float distance) =>
            distance < ShortLeg ? Ease.Cubic(0.5f, 0f, 0.5f, 1f) : Ease.Cubic(0.7f, 0f, 0.3f, 1f);

        /// <summary>
        /// How long a leg of <paramref name="distance"/> points onto a target <paramref name="width"/> wide takes.
        /// </summary>
        /// <remarks>
        /// The <see cref="PeakSpeed"/> floor assumes the 1.875 ratio between a minimum-jerk profile's peak and mean
        /// speed, which is close enough for any curve shaped like an arrival.
        /// </remarks>
        public static float TravelSeconds(float distance, float width)
        {
            var w = width > 0f ? width : TargetWidth;
            var fitts = FittsA + FittsB * Mathf.Log(distance / Mathf.Max(1f, w) + 1f, 2f);
            var clamped = Mathf.Clamp(fitts, MinTravel, MaxTravel);
            var speedFloor = 1.875f * distance / PeakSpeed;
            return Mathf.Max(clamped, speedFloor);
        }
    }
}
