using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Colour emoji drawn from the operating system's own font: composed sequences, not a sprite sheet.
    /// </summary>
    /// <remarks>
    /// The specimen is chosen for what a sprite atlas cannot do rather than for what looks busiest. A family is one
    /// grapheme cluster built from four people and three joiners, a flag is a pair of regional indicators, and a skin
    /// tone is a base emoji plus a modifier — three sequences an engine either composes or renders as its parts.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(EmojiSlide))]
    public sealed class EmojiSlide : Slide
    {
        [SerializeField] private string headline = "Real emoji, and they cost nothing.";
        [SerializeField] private string sub = "Straight from the device. New ones arrive with the OS.";

        /// <summary>
        /// Sequences the running OS can actually draw.
        /// </summary>
        /// <remarks>
        /// No flags. Windows ships no glyphs for regional-indicator pairs, so on the machine this film is captured on
        /// they render as nothing at all — a blank row in a shot whose whole argument is that the device already has
        /// what you need.
        /// </remarks>
        [SerializeField, TextArea(4, 10)]
        private string specimen =
            "👨‍👩‍👧‍👦 👩‍💻 👨‍🚀 🧑‍🎨\n" +
            "👋🏻 👋🏼 👋🏽 👋🏾 👋🏿\n" +
            "🎉 ✨ 🔥 💜 🚀 🐈";

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private Showcase panel;
        private GlyphReveal reveal;
        private Metric cost;
        private Ledger facts;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            panel = stage.Showcase("Emoji", stage.Root, "Composed sequences, from the system font",
                specimen, new Vector2(-stage.Width * 0.215f, stage.ContentCentre),
                new Vector2(stage.Width * 0.46f, stage.ContentHeight * 0.94f),
                stage.ContentHeight * 0.12f, HorizontalAlignment.Center);

            reveal = stage.Reveal(panel.Body, new PopRevealHandler(), 3f);

            cost = stage.Metric("Cost", stage.Root, "0 KB", "added to your build",
                new Vector2(stage.Width * 0.246f, stage.ContentCentre + stage.ContentHeight * 0.24f),
                stage.Width * 0.4f, stage.ContentHeight * 0.19f);

            facts = stage.Ledger("Facts", stage.Root, "One cluster each, composed on the device", new[]
            {
                new LedgerEntry("Family", "4 people, 3 joiners", "✓", theme.Pass),
                new LedgerEntry("Profession", "person + laptop", "✓", theme.Pass),
                new LedgerEntry("Skin tone", "base + modifier", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.246f, stage.ContentCentre - stage.ContentHeight * 0.26f),
                stage.Width * 0.4f, onBrand: true);

            Cue("writeon", First);
            Cue("figure", Figure);
            for (var i = 0; i < facts.Count; i++) Cue("tick", Rows + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local - First * 0.5f);
            reveal.Fill = GlyphReveal.Frontier.Window(local, First, WriteOn);

            cost.Pose(local - Figure);
            for (var i = 0; i < facts.Count; i++)
                facts.Pose(i, enter.Window(local, Rows + i * Step, RowIn));
        }

        private const float First = 0.35f;
        private const float WriteOn = 1.6f;
        private const float Figure = 1.35f;
        private const float Rows = 1.85f;
        private const float Step = 0.2f;
        private const float RowIn = 0.36f;
    }
}
