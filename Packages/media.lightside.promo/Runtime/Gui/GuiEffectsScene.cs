using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The effects section of the UniGUI reel: image effects on a live part of the interface — a reward card shining,
    /// burning away and pixelating at the pick of a switch — and a glass pause menu whose blur a slider turns up.
    /// </summary>
    /// <remarks>
    /// The effects are the kit's own, run on the card and on what lies behind the glass as the film draws them, every
    /// frame. The section opens as the shapes section's dive lands and ends diving into the glass, on into the next slide.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiEffectsScene))]
    public sealed partial class GuiEffectsScene : GuiKitSection
    {
        [SerializeField] private string lead = "Any";

        /// <summary>The headline's last word for each beat in order: the card's effects and the glass's blur.</summary>
        [SerializeField] private string[] words = { "effect.", "blur." };

        [SerializeField] private string promise = "On any part of the interface.";

        private static readonly Link[] links = { Link.Rise, Link.Whip, Link.Dive };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildBoards(Stage stage) => new Part[] { new StackPart(this, stage), new GlassPart(this, stage) };
    }
}
