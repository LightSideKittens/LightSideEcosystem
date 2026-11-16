using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The controls section of the UniGUI reel: the kit's real control prefabs operated by the pointer in quick
    /// succession — inputs, pickers, charts and game controls — each on a board of its own.
    /// </summary>
    /// <remarks>The section ends on the game's run, which the next slide's push carries on.</remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiControlsScene))]
    public sealed partial class GuiControlsScene : GuiKitSection
    {
        [SerializeField] private string lead = "Ready-made";

        /// <summary>The headline's last word for each board in order: two each of inputs, text fields, pickers and dates, data, charts, two of navigation, gamepad, overlays, feedback, windows, loadouts, game controls.</summary>
        [SerializeField] private string[] words =
        {
            "inputs.", "inputs.", "text fields.", "text fields.", "pickers.", "pickers.", "dates.", "dates.", "data.", "charts.",
            "navigation.", "navigation.", "gamepad UI.", "overlays.", "feedback.", "windows.", "loadouts.", "game UI."
        };

        [SerializeField] private string promise = "Real prefabs. Drop in and play.";

        /// <summary>The item icons the game boards show, found by name.</summary>
        [SerializeField] private Sprite[] items;

        private static readonly Link[] links = {
            Link.Rise, Link.Push, Link.Push, Link.Push, Link.Drop, Link.Push, Link.Whip, Link.Push, Link.Zoom, Link.Whip,
            Link.Push, Link.Push, Link.Whip, Link.Drop, Link.Zoom, Link.Whip, Link.Drop, Link.Zoom, Link.Push
        };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildBoards(Stage stage) => new Part[]
            {
                new InputsPart(this, stage), new OptionsPart(this, stage), new FieldsPart(this, stage),
                new ProfilePart(this, stage), new PickersPart(this, stage), new ColorPart(this, stage),
                new DatesPart(this, stage), new ReminderPart(this, stage), new DataPart(this, stage), new ChartsPart(this, stage),
                new NavigationPart(this, stage), new NewsPart(this, stage), new GamepadPart(this, stage), new OverlaysPart(this, stage),
                new FeedbackPart(this, stage),
                new WindowsPart(this, stage), new LoadoutPart(this, stage), new GamePart(this, stage)
            };

        /// <summary>The item icon named <paramref name="item"/>.</summary>
        /// <exception cref="InvalidOperationException">The section's items hold no such icon.</exception>
        private Sprite Item(string item)
        {
            foreach (var sprite in items)
                if (sprite && sprite.name == item)
                    return sprite;
            throw new InvalidOperationException($"[Promo] '{name}' has no item icon named '{item}'.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (art) ObjectUtils.SafeDestroy(art);
        }
    }
}
