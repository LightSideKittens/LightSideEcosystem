using UnityEditor;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Builds the UniShapes reel into the open scene, as a rig of its own.</summary>
    /// <remarks>
    /// Its own rig and its own livery. The film sells a different product on a different ramp, and a reel that had
    /// to share a slide list or a palette with the text films would serve neither.
    /// <para>
    /// The slide runs with push-in off. uGUI transforms the tangent stream by the element's local-to-canvas matrix
    /// when it batches, and a shape's parameters ride that stream: a scaled ancestor turns a star's point count
    /// into a fraction and a polygon's atlas row into a neighbor's.
    /// </para>
    /// </remarks>
    internal static class PromoShapesCommands
    {
        [MenuItem(PromoMenu.Tools.CreateShapesReel, false, 13)]
        private static void CreateShapesReel()
        {
            var reel = PromoRig.Create(PromoMenu.ShapesReelObjectName, "Create Promo Shapes Reel", out var reelRect);
            if (!reel) return;

            reel.Theme = new ShapesTheme();
            EditorUtility.SetDirty(reel);

            var scene = PromoRig.AddSlide<ShapesReelScene>(reelRect, "Shapes Reel", 5f, new Cut());
            scene.PushIn = 0f;

            PromoRig.Wire(reel, "theme.displayFace", PromoRig.FindAsset<UniTextFont>("FiraSans-ExtraBold"));
            PromoRig.Wire(reel, "theme.bodyFace", PromoRig.FindAsset<UniTextFont>("FiraSans-Medium"));
            PromoRig.Wire(scene, "logo", PromoRig.FindAsset<Texture2D>("unishapes-logo", editorToo: true));

            PromoRig.Finish(reel);
        }
    }
}
