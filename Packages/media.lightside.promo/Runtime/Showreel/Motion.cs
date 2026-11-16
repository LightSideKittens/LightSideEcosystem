namespace LightSide.Promo
{
    /// <summary>
    /// The curves the showreel moves on: fast out of the gate, and past the target before settling on it.
    /// </summary>
    /// <remarks>
    /// A separate vocabulary from the Material tokens the argument reel uses, and deliberately so. Those are tuned to
    /// be unnoticed while a viewer reads; these are tuned to be noticed instead — every one either overshoots or
    /// anticipates, because an element that arrives exactly on its mark reads as placed rather than as thrown.
    /// </remarks>
    public static class Motion
    {
        /// <summary>Overshoots and settles back. The default for anything that lands.</summary>
        public static Ease Back => Ease.Cubic(0.34f, 1.56f, 0.64f, 1f);

        /// <summary>Overshoots hard. For the one element a shot is about.</summary>
        public static Ease Punch => Ease.Cubic(0.22f, 2.1f, 0.36f, 1f);

        /// <summary>Pulls back before it goes, then throws. For anything leaving or being launched.</summary>
        public static Ease Whip => Ease.Cubic(0.62f, -0.45f, 0.2f, 1.2f);

        /// <summary>Almost all the distance in the first fifth, then a long glide. For travel across the frame.</summary>
        public static Ease Snap => Ease.Cubic(0.12f, 0.94f, 0.2f, 1f);

        /// <summary>
        /// A camera whip: it gathers speed, crosses nearly all the distance in a blur through the middle and overshoots a
        /// little before it settles. For whole compositions thrown across the frame, one leaving as the next arrives.
        /// </summary>
        public static Ease Swish => Ease.Cubic(0.7f, 0f, 0.16f, 1.06f);

        /// <summary>A held, even climb — for a counter or a bar, where character would read as inaccuracy.</summary>
        public static Ease Meter => Ease.Cubic(0.4f, 0f, 0.2f, 1f);
    }
}
