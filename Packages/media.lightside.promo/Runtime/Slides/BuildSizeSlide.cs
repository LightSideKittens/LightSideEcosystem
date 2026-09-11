using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// What a full CJK typeface costs in a shipped build, baked as an atlas against rasterized on demand.
    /// </summary>
    /// <remarks>
    /// Two levers, and the shot separates them because they are independent: nothing is baked, so no atlas texture is
    /// in the build at all; and what does ship — the font file — is Zstandard-compressed at import and decompressed
    /// lazily on first access.
    /// <para>
    /// The bars are drawn to a compressed scale, not to the ratio. At true scale the UniText bar is a hairline beside
    /// the atlas and the panel reads as a rendering fault rather than as a comparison.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(BuildSizeSlide))]
    public sealed class BuildSizeSlide : Slide
    {
        [SerializeField] private string headline = "Your build gets lighter.";
        [SerializeField] private string sub = "No baked textures, and the fonts themselves ship smaller.";
        [SerializeField] private string payoff = "2.7× smaller font data, and no atlas at all.";

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private Claim note;
        private Meters payload;
        private Meters compression;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, sub);
            note = stage.Claim(stage.Root, payoff, top: false);

            var column = stage.Width * 0.42f;
            var offset = (column + stage.Width * 0.03f) * 0.5f;

            payload = stage.Meters("Payload", stage.Root, "A full CJK face, in the build", new[]
            {
                new MeterEntry("TextMeshPro — baked SDF atlas", "80–250 MB", 1f),
                new MeterEntry("UniText — nothing baked", "a few MB", 0.06f, true)
            }, new Vector2(-offset, stage.ContentCentre), column);

            compression = stage.Meters("Compression", stage.Root, "Latin + Arabic font data", new[]
            {
                new MeterEntry("On disk, uncompressed", "12 MB", 1f),
                new MeterEntry("In the build, Zstd level 22", "4.4 MB", 0.37f, true)
            }, new Vector2(offset, stage.ContentCentre), column, minHeight: payload.Height);

            for (var i = 0; i < payload.Count; i++) Cue("bar", First + i * Step);
            for (var i = 0; i < compression.Count; i++) Cue("bar", Second + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            for (var i = 0; i < payload.Count; i++)
                payload.Pose(i, enter.Window(local, First + i * Step, BarIn));

            for (var i = 0; i < compression.Count; i++)
                compression.Pose(i, enter.Window(local, Second + i * Step, BarIn));

            note.Pose(local - (Second + compression.Count * Step + 0.5f));
        }

        private const float First = 0.45f;
        private const float Second = 1.35f;
        private const float Step = 0.3f;
        private const float BarIn = 0.7f;
    }
}
