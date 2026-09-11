using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Two source-control panels side by side: the churn a baked font asset produces on every change, against a
    /// working tree that stays clean.
    /// </summary>
    /// <remarks>
    /// A baked atlas has no file of its own — the texture is serialized inside the font asset — so every glyph a
    /// project newly touches rewrites that one file, and it lands in the diff whole. UniText rasterizes on demand
    /// and serializes nothing, so there is nothing for a commit to contain.
    /// <para>
    /// This is the same argument the film's payload shot makes later, told first as a daily annoyance rather than
    /// as a size. A developer who has resolved a merge conflict inside a font asset recognises it instantly.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(AssetChurnSlide))]
    public sealed class AssetChurnSlide : Slide
    {
        [SerializeField] private string headline = "UniText keeps your repository clean.";
        [SerializeField] private string payoff = "Nothing to bake. Nothing to commit.";

        private readonly Ease enter = Ease.EmphasizedIn;

        private Ledger dirty;
        private Ledger clean;
        private Claim claim;
        private Claim closing;
        private int shown;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline);
            closing = stage.Claim(stage.Root, payoff, top: false);

            var column = stage.Width * 0.44f;
            var offset = (column + stage.Width * 0.026f) * 0.5f;
            var lift = stage.ContentCentre;

            dirty = stage.Ledger("Churn", stage.Root, "Changes — TextMeshPro", new[]
            {
                new LedgerEntry("NotoSans SDF.asset", "+2 841", "M", theme.Orange),
                new LedgerEntry("NotoSansArabic SDF.asset", "+1 902", "M", theme.Orange),
                new LedgerEntry("NotoSansThai SDF.asset", "+1 145", "M", theme.Orange),
                new LedgerEntry("NotoSansDevanagari SDF.asset", "+2 233", "M", theme.Orange),
                new LedgerEntry("NotoSansHebrew SDF.asset", "+1 664", "M", theme.Orange),
                new LedgerEntry("NotoSerifSC SDF.asset", "+9 610", "M", theme.Orange)
            }, new Vector2(-offset, lift), column);

            clean = stage.Ledger("Clean", stage.Root, "Changes — UniText", new[]
            {
                new LedgerEntry("working tree clean", null, "✓", theme.Pass)
            }, new Vector2(offset, lift), column, onBrand: true, minHeight: dirty.Height);

            shown = dirty.Count;
            for (var i = 0; i < shown; i++) Cue("tick", First + i * Step);
            Cue("clean", First + shown * Step + 0.15f);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            for (var i = 0; i < shown; i++)
                dirty.Pose(i, enter.Window(local, First + i * Step, RowIn));

            clean.Pose(0, enter.Window(local, First + shown * Step + 0.15f, RowIn));
            Stage.Alpha(clean.Title, enter.Window(local, First + shown * Step, RowIn));
            closing.Pose(local - (First + shown * Step + 0.6f));
        }

        private const float First = 0.55f;
        private const float Step = 0.16f;
        private const float RowIn = 0.34f;
    }
}
