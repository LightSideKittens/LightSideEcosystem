using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// One figure, and the two engines it is measured against.
    /// </summary>
    /// <remarks>
    /// A range rather than a single number, because the gap is not one number: it is roughly five-fold on a rebuild
    /// and twenty-fold on layout, and quoting either alone would be picking the flattering end of a measurement.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(SpeedSlide))]
    public sealed class SpeedSlide : Slide
    {
        [SerializeField] private string headline = "Faster. By a lot.";

        /// <summary>Point size of the headline, which on this shot competes with the figure rather than labels it.</summary>
        [SerializeField, Min(0f)] private float headlineSize = 140f;
        [SerializeField] private string figure = "2–20×";
        [SerializeField] private string against = "faster than TextMeshPro and UI Toolkit";
        [SerializeField] private string payoff = "And it stops allocating. No garbage, no stutter.";

        private Claim claim;
        private Claim note;
        private Metric gap;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, size: headlineSize);
            note = stage.Claim(stage.Root, payoff, top: false);

            gap = stage.Metric("Gap", stage.Root, figure, against,
                new Vector2(0f, stage.ContentCentre), stage.Width * 0.8f, stage.ContentHeight * 0.5f);

            Cue("figure", First);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            gap.Pose(local - First);
            note.Pose(local - Payoff);
        }

        private const float First = 0.45f;
        private const float Payoff = 1.6f;
    }
}
