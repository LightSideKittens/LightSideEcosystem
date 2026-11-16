using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The motion section of the UniGUI reel: Lottie animations living in the interface — reactions in a chat that play
    /// and pop under the pointer, and a season reward whose stars and party poppers burst when it is claimed.
    /// </summary>
    /// <remarks>
    /// Every animation is a <see cref="UniLottie"/> of the kit's sample animations, drawn in the same canvas as the
    /// controls. The film sets each one's frame from its own time, so a frame depends on its time alone.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiMotionScene))]
    public sealed partial class GuiMotionScene : GuiKitSection
    {
        [SerializeField] private string lead = "Animated";

        /// <summary>The headline's last word for each beat in order: the chat's reactions, the season's reward and the controls' icons.</summary>
        [SerializeField] private string[] words = { "reactions.", "rewards.", "icons." };

        [SerializeField] private string promise = "Lottie, right inside the UI.";

        /// <summary>The reactions the chat offers, in the order they stand; the third is the one picked.</summary>
        [SerializeField] private LottieAsset[] reactions = Array.Empty<LottieAsset>();

        /// <summary>The reward's animations: the party popper, then the star each earned star bursts with.</summary>
        [SerializeField] private LottieAsset[] rewards = Array.Empty<LottieAsset>();

        private static readonly Link[] links = { Link.Rise, Link.Whip, Link.Drop, Link.Whip };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildBoards(Stage stage)
        {
            if (reactions.Length < ReactionCount || rewards.Length < 2 || icons.Length < 6 || monkey.Length < 3)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' needs {ReactionCount} reactions, a party popper and a star, six action icons and three monkey animations assigned.");
            return new Part[] { new ReactionsPart(this, stage), new RewardPart(this, stage), new IconsPart(this, stage) };
        }

        private const int ReactionCount = 6;
    }
}
