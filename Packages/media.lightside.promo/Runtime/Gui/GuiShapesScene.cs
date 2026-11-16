using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The shapes section of the UniGUI reel: the outline, the corners, the stroke, the shadow, the paint, the layer
    /// stack and the boolean, one beat each.
    /// </summary>
    /// <remarks>
    /// The corner, outline and shadow beats are real Inspector windows (<see cref="GuiSection.Takes"/>) that all edit
    /// one panel, joined by match cuts; the section ends by diving into its last subject.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiShapesScene))]
    public sealed partial class GuiShapesScene : GuiSection
    {
        [SerializeField] private string takesFolder = "Recordings/Takes/Shapes";

        [SerializeField] private string lead = "Any";

        /// <summary>The headline's last word for each beat in order: outline, corners, stroke, shadow, paint, layers, modifiers, masks, boolean.</summary>
        [SerializeField] private string[] words = { "shape.", "corner.", "outline.", "shadow.", "gradient.", "style.", "warp.", "mask.", "combination." };

        [SerializeField] private string promise = "No sprites. Sharp at any size.";

        private static readonly Link[] links =
        {
            Link.Rise, Link.Whip, Link.Match, Link.Match, Link.Whip, Link.Whip, Link.Zoom, Link.Drop, Link.Whip, Link.Dive
        };

        private Widget layered;
        private PrimitiveShapeProvider layeredOutline;
        private StrokeLayer layeredStroke;
        private ShadowLayer layeredShadow;

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        /// <summary>Stands <paramref name="part"/> <paramref name="magnify"/> times larger than built, about the content's middle.</summary>
        private Part Grown(Part part, float magnify)
        {
            part.Magnify = magnify;
            part.MagnifyAbout = new Vector2(0f, contentCenter);
            return part;
        }

        protected override Part[] BuildParts(Stage stage)
        {
            BuildLayered(stage);
            var parts = new[]
            {
                Grown(BuildShapes(stage), 1.25f),
                BuildCorners(stage),
                BuildOutline(stage),
                BuildShadow(stage),
                Grown(BuildGradient(stage), 1.35f),
                Grown(new StylePart(this, stage), 1.2f),
                Grown(BuildWarp(stage), 1.2f),
                Grown(BuildMask(stage), 1.2f),
                Grown(BuildBoolean(stage), 1.25f)
            };
            return parts;
        }

        /// <summary>
        /// The shared panel of the corner, outline and shadow beats: it arrives with the first of them, glides between
        /// their resting places through the match cuts, and leaves with the last.
        /// </summary>
        protected override void PoseScene(float local)
        {
            var first = Beats[LayeredFirst];
            var last = Beats[LayeredLast];
            var shown = local >= first.In && local <= last.Out + last.Leaving;
            layered.Rect.gameObject.SetActive(shown);
            if (!shown) return;

            var at = first.Take.SubjectAt;
            for (var i = LayeredFirst + 1; i <= LayeredLast; i++)
                at = Vector2.LerpUnclamped(at, Beats[i].Take.SubjectAt, Motion.Swish.Window(local, Beats[i].In, SwishFor));
            layered.Rect.anchoredPosition = at + first.Arrival(local) + last.Departure(local);
        }

        /// <summary>The beats that edit the shared panel, first and last.</summary>
        private const int LayeredFirst = 1;

        private const int LayeredLast = 3;
    }
}
