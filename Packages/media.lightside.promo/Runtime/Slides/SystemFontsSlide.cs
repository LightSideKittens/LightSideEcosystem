using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A project with no font files in it, rendering every script the device knows.
    /// </summary>
    /// <remarks>
    /// The specimen is deliberately left without a font, so the system cascade is what resolves each script. Assigning
    /// one here would make the shot a picture of the claim rather than the claim itself — and the failure mode of a
    /// missing cascade is exactly what the shot is arguing against.
    /// <para>
    /// Browsers do not expose system fonts, so this is the one capability that has a platform hole. The ledger says so
    /// on screen: a claim a viewer later discovers to be conditional costs more than the claim was worth.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(SystemFontsSlide))]
    public sealed class SystemFontsSlide : Slide
    {
        [SerializeField] private string headline = "Ship without shipping a single font.";
        [SerializeField] private string sub = "The device already has them. UniText just asks.";

        [SerializeField, TextArea(4, 10)]
        private string specimen =
            "Hello, world\n" +
            "こんにちは世界 · 안녕하세요\n" +
            "مرحبا بالعالم · שלום עולם\n" +
            "Γειά σου Κόσμε · გამარჯობა\n" +
            "नमस्ते दुनिया · สวัสดีชาวโลก";

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private Showcase panel;
        private GlyphReveal reveal;
        private Ledger facts;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Cascade", stage.Root, "Fonts in this project: none",
                specimen, new Vector2(-stage.Width * 0.215f, stage.ContentCentre),
                new Vector2(stage.Width * 0.46f, stage.ContentHeight * 0.94f),
                stage.ContentHeight * 0.088f, HorizontalAlignment.Center);

            reveal = stage.Reveal(panel.Body, new FadeRevealHandler());

            facts = stage.Ledger("Facts", stage.Root, "What that costs", new[]
            {
                new LedgerEntry("Font assets imported", "0", "✓", theme.Pass),
                new LedgerEntry("Atlas textures baked", "0", "✓", theme.Pass),
                new LedgerEntry("Fallback chains to maintain", "0", "✓", theme.Pass),
                new LedgerEntry("Scripts the OS covers", "all of them", "✓", theme.Pass),
                new LedgerEntry("WebGL — no OS fonts", "bundle one", "!", theme.Orange)
            }, new Vector2(stage.Width * 0.246f, stage.ContentCentre), stage.Width * 0.4f, onBrand: true);

            Cue("writeon", First);
            for (var i = 0; i < facts.Count; i++) Cue("tick", Rows + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local - First * 0.5f);
            reveal.Fill = GlyphReveal.Frontier.Window(local, First, WriteOn);

            for (var i = 0; i < facts.Count; i++)
                facts.Pose(i, enter.Window(local, Rows + i * Step, RowIn));
        }

        private const float First = 0.35f;
        private const float WriteOn = 1.7f;
        private const float Rows = 1.5f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
