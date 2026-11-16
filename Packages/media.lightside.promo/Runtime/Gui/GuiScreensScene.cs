using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The screens section of the UniGUI reel: whole game screens built from the same kit — a main menu, its settings, an
    /// inventory and a shop of one game — each worked through by the pointer as a player would.
    /// </summary>
    /// <remarks>
    /// Every screen is a board of the kit's real controls and the film's own artwork drawn with shapes. The section opens as
    /// the controls section's dive lands and ends diving into the pack the shop sells, on into the next slide.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiScreensScene))]
    public sealed partial class GuiScreensScene : GuiKitSection
    {
        [SerializeField] private string lead = "Whole";

        /// <summary>The headline's last word for each screen in order: the menu, the settings, the inventory and the shop.</summary>
        [SerializeField] private string[] words = { "menus.", "settings.", "inventories.", "shops." };

        [SerializeField] private string promise = "Built from the same kit.";

        private static readonly Link[] links = { Link.Rise, Link.Push, Link.Whip, Link.Push, Link.Dive };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildBoards(Stage stage) => new Part[]
        {
            new MenuPart(this, stage), new SettingsPart(this, stage), new InventoryPart(this, stage), new ShopPart(this, stage)
        };
    }
}
