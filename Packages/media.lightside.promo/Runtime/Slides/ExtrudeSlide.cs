using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A word thrown into relief by a stack of its own silhouette, cast in a direction that travels around it.
    /// </summary>
    /// <remarks>
    /// Every step lies in the glyph's own plane — this is a look, not geometry, and the slide claims nothing else.
    /// What turns is the offset the stack is laid along, which reads as a light moving; the letters never rotate.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(ExtrudeSlide))]
    public sealed class ExtrudeSlide : Slide
    {
        [SerializeField] private string word = "DEPTH";
        [SerializeField] private string headline = "Carve it out of the page.";
        [SerializeField] private string sub = "Extrude, bevel and a light that moves — still one draw call.";

        /// <summary>A display face. A text face has stems too thin for an extrusion to sit behind.</summary>
        [SerializeField] private UniTextFont displayFont;

        private readonly Ease enter = Ease.EmphasizedIn;

        private Claim claim;
        private UniText specimen;
        private ExtrudeModifier extrude;
        private FillModifier face;
        private Ledger stack;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            specimen = stage.Label(stage.Root, word, stage.ContentHeight * 0.38f, theme.Text);
            stage.Box(specimen.rectTransform, new Vector2(-stage.Width * 0.16f, stage.ContentCentre),
                new Vector2(stage.Width * 0.52f, stage.ContentHeight * 0.8f));
            if (displayFont) specimen.Font = displayFont;

            extrude = new ExtrudeModifier
            {
                Offset = Vector2.zero,
                NearColor = theme.Violet,
                FarColor = new Color32(18, 12, 34, 255),
                Steps = 24,
                Bevel = true
            };
            face = stage.BrandFill(90f);

            specimen.Styles.Add(Style.WholeText(extrude));
            specimen.Styles.Add(Style.WholeText(face));

            stack = stage.Ledger("Stack", stage.Root, "ExtrudeModifier", new[]
            {
                new LedgerEntry("Steps in the stack", "24", "1", theme.Violet),
                new LedgerEntry("Near face", "brand ramp", "2", theme.Violet),
                new LedgerEntry("Far face", "into the dark", "3", theme.Violet),
                new LedgerEntry("Bevelled edge", "on", "4", theme.Violet),
                new LedgerEntry("Draw calls", "1", "✓", theme.Pass)
            }, new Vector2(stage.Width * 0.3f, stage.ContentCentre), stage.Width * 0.36f, onBrand: true);

            for (var i = 0; i < stack.Count; i++) Cue("tick", First + i * Step);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            var grow = Mathf.Clamp01(Spring.Pop.Evaluate(local - Grow));
            var angle = Mathf.PI * 0.75f + Mathf.Max(0f, local - Orbit) * OrbitRate;
            extrude.Offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Depth * grow);

            for (var i = 0; i < stack.Count; i++)
                stack.Pose(i, enter.Window(local, First + i * Step, RowIn));
        }

        /// <summary>How far the far face sits from the near one, in em.</summary>
        private const float Depth = 0.16f;

        private const float Grow = 0.35f;
        private const float Orbit = 1.1f;
        private const float OrbitRate = 0.85f;
        private const float First = 0.6f;
        private const float Step = 0.24f;
        private const float RowIn = 0.36f;
    }
}
