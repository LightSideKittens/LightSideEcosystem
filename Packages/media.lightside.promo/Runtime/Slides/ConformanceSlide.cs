using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The official Unicode conformance suites, each ticked off, beside the total they add up to.
    /// </summary>
    /// <remarks>
    /// Ticks rather than bars. A conformance suite is a gate that is either passed or not, and a bar is an instrument
    /// for a proportion — drawn under four unequal counts it reads as "this fraction of them passed", which is the
    /// opposite of the claim. The counts stay as trailing values, where they are read as sizes rather than as scores.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(ConformanceSlide))]
    public sealed class ConformanceSlide : Slide
    {
        [SerializeField] private string headline = "Every language comes out right.";
        [SerializeField] private string sub = "Checked against the official tests, not just claimed.";
        [SerializeField] private string total = "891 757";
        [SerializeField] private string totalCaption = "conformance tests · zero failures";

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private Metric metric;
        private Ledger suites;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            metric = stage.Metric("Total", stage.Root, total, totalCaption,
                new Vector2(-stage.Width * 0.265f, stage.ContentCentre), stage.Width * 0.4f,
                stage.ContentHeight * 0.3f);

            suites = stage.Ledger("Suites", stage.Root, "Unicode conformance · every suite in full", new[]
            {
                new LedgerEntry("UAX #9 · Bidirectional", "861 948", "✓", theme.Pass),
                new LedgerEntry("UAX #14 · Line breaking", "19 338", "✓", theme.Pass),
                new LedgerEntry("UAX #24 · Script detection", "9 705", "✓", theme.Pass),
                new LedgerEntry("UAX #29 · Grapheme clusters", "766", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.24f, stage.ContentCentre), stage.Width * 0.44f, onBrand: true);

            Cue("figure", First);
            for (var i = 0; i < suites.Count; i++) Cue("tick", First + Gap + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            metric.Pose(local - First);

            for (var i = 0; i < suites.Count; i++)
                suites.Pose(i, enter.Window(local, First + Gap + i * Step, RowIn));
        }

        private const float First = 0.4f;
        private const float Gap = 0.5f;
        private const float Step = 0.22f;
        private const float RowIn = 0.4f;
    }
}
