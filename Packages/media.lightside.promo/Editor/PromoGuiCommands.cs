using UnityEditor;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Builds the UniGUI reel into the open scene, as a rig of its own.</summary>
    /// <remarks>
    /// The UniShapes livery, captured at twice the frame's resolution. The film's windows are recorded at a higher
    /// editor scale than any card shows them at, so a 1920 × 1080 layout rendered at 3840 × 2160 only ever resamples a
    /// captured window down.
    /// <para>
    /// The slide runs with push-in off. uGUI transforms the tangent stream by the element's local-to-canvas matrix
    /// when it batches, and a shape's parameters ride that stream: a scaled ancestor scales the subject's corner
    /// radius a second time.
    /// </para>
    /// </remarks>
    internal static class PromoGuiCommands
    {
        [MenuItem(PromoMenu.Tools.CreateGuiReel, false, 14)]
        private static void CreateGuiReel()
        {
            var reel = PromoRig.Create(PromoMenu.GuiReelObjectName, "Create Promo UniGUI Reel", out var reelRect);
            if (!reel) return;

            reel.Theme = new ShapesTheme();
            var serialized = new SerializedObject(reel);
            serialized.FindProperty("captureScale").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var opening = PromoRig.AddSlide<GuiOpeningScene>(reelRect, "00 Opening", 3f, new Cut());
            opening.PushIn = 0f;
            WireKit(opening);
            var shapes = PromoRig.AddSlide<GuiShapesScene>(reelRect, "01 Shapes", 3f, new Cut());
            shapes.PushIn = 0f;
            var text = PromoRig.AddSlide<GuiTextScene>(reelRect, "02 Text", 3f, new Cut());
            text.PushIn = 0f;
            var themes = PromoRig.AddSlide<GuiThemeScene>(reelRect, "03 Theme", 3f, new Cut());
            themes.PushIn = 0f;
            WireTheme(themes);
            var controls = PromoRig.AddSlide<GuiControlsScene>(reelRect, "04 Controls", 3f, new Cut());
            controls.PushIn = 0f;
            WireKit(controls);
            var finale = PromoRig.AddSlide<GuiFinaleScene>(reelRect, "05 Finale", 3f, new Cut());
            finale.PushIn = 0f;
            WireKit(finale);

            PromoRig.Wire(reel, "theme.displayFace", PromoRig.FindAsset<UniTextFont>("FiraSans-ExtraBold"));
            PromoRig.Wire(reel, "theme.bodyFace", PromoRig.FindAsset<UniTextFont>("FiraSans-Medium"));

            PromoRig.Finish(reel);
        }

        /// <summary>
        /// Points the theme section at the shipped palettes and control prefabs, where the UniGUI installer lays them
        /// out.
        /// </summary>
        /// <remarks>
        /// Loaded by path, not found by name: other UI kits in a project ship prefabs called Slider or Button too.
        /// </remarks>
        /// <exception cref="System.InvalidOperationException">The project has no UniGUI controls installed.</exception>
        internal static void WireTheme(GuiThemeScene scene)
        {
            var serialized = new SerializedObject(scene);
            serialized.FindProperty("darkPalette").objectReferenceValue = Load<UniShapePaints>("UniGUIPalette.asset");
            serialized.FindProperty("lightPalette").objectReferenceValue = Load<UniShapePaints>("UniGUIPalette Light.asset");
            foreach (var (field, prefab) in ThemeControls)
                serialized.FindProperty(field).objectReferenceValue = Load<GameObject>($"Controls/{prefab}.prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Points a scene that shows the kit at the shipped dark palette and every control prefab, in name order, where
        /// the UniGUI installer lays them out.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">The project has no UniGUI controls installed.</exception>
        internal static void WireKit(Slide scene)
        {
            var serialized = new SerializedObject(scene);
            serialized.FindProperty("basePalette").objectReferenceValue = Load<UniShapePaints>("UniGUIPalette.asset");
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { $"{UniGuiFolder}/Controls" });
            var paths = new System.Collections.Generic.List<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Substring(0, path.LastIndexOf('/')) == $"{UniGuiFolder}/Controls") paths.Add(path);
            }

            if (paths.Count == 0)
                throw new System.InvalidOperationException($"[Promo] No control prefabs in {UniGuiFolder}/Controls; install UniGUI's controls first.");
            paths.Sort(System.StringComparer.Ordinal);
            var kit = serialized.FindProperty("kit");
            kit.arraySize = paths.Count;
            for (var i = 0; i < paths.Count; i++)
                kit.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>($"{UniGuiFolder}/{path}")
            ?? throw new System.InvalidOperationException($"[Promo] No {typeof(T).Name} at {UniGuiFolder}/{path}; install UniGUI's controls first.");

        private const string UniGuiFolder = "Assets/UniGUI";

        private static readonly (string field, string prefab)[] ThemeControls =
        {
            ("switchPrefab", "Switch"), ("checkboxPrefab", "Checkbox"), ("sliderPrefab", "Slider"),
            ("segmentedPrefab", "Segmented Control"), ("progressPrefab", "Progress Bar"), ("buttonPrefab", "Button")
        };
    }
}
