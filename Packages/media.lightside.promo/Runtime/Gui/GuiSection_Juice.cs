namespace LightSide.Promo
{
    /// <summary>The impact a section's beats share: a burst at the press that pays a beat off.</summary>
    /// <remarks>
    /// Only presses scripted as an accent burst; every other click shows the pointer's own ripple alone. Neither clicks
    /// nor headline words jolt the frame: a device repeated as often as clicks come stops reading as an accent.
    /// </remarks>
    public abstract partial class GuiSection
    {
        private Impacts impacts;

        /// <summary>Lays a burst at every accented press of every beat's pointer, once the beats are scheduled.</summary>
        private void BuildHits()
        {
            foreach (var part in parts)
                foreach (var (at, point) in part.Accents())
                    impacts.Hit(at, point, ClickBurst);
        }

        private const float ClickBurst = 110f;
    }
}
