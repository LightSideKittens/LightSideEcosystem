using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The theme section of the UniGUI reel: a settings card built from the real UniGUI control prefabs, restyled
    /// from one palette — first its accent, then the whole card from dark to light.
    /// </summary>
    /// <remarks>
    /// The controls are the shipped prefabs, unchanged but for their labels, painted from the scene's own palette
    /// (<see cref="ScenePalette"/>). Each frame writes the palette the time calls for: the dark and the light palette
    /// mixed by how far the switch to light has gone, the accent swatches replaced by the accent last picked.
    /// <para>
    /// The card stays through both beats; only the control under it changes, a row of accents and then a dark and light
    /// switch.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiThemeScene))]
    public sealed partial class GuiThemeScene : GuiSection
    {
        [SerializeField] private string lead = "New";

        /// <summary>The headline's last word for each beat in order: accent, theme.</summary>
        [SerializeField] private string[] words = { "accent.", "theme." };

        [SerializeField] private string promise = "One palette. Every control follows.";

        [SerializeField] private UniShapePaints darkPalette;
        [SerializeField] private UniShapePaints lightPalette;

        [SerializeField] private GameObject switchPrefab;
        [SerializeField] private GameObject checkboxPrefab;
        [SerializeField] private GameObject sliderPrefab;
        [SerializeField] private GameObject segmentedPrefab;
        [SerializeField] private GameObject progressPrefab;
        [SerializeField] private GameObject buttonPrefab;

        private static readonly Link[] links = { Link.Rise, Link.Match, Link.Whip };

        /// <summary>The swatches that carry the accent; every other swatch follows the dark and light palettes alone.</summary>
        private static readonly string[] AccentSwatches = { "Accent", "AccentSoft", "Focus" };

        private ScenePalette palette;
        private Color[] dark;
        private Color[] light;
        private bool[] accented;

        private Widget card;
        private readonly List<(UniText text, string swatch)> labels = new();
        private UniShapeValue progress;
        private readonly List<UniToggle> toggles = new();

        /// <summary>The value controls and the value each one holds.</summary>
        private readonly List<(UniShapeValue value, float at)> settled = new();

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildParts(Stage stage)
        {
            if (!darkPalette || !lightPalette)
                throw new InvalidOperationException($"[Promo] '{name}' needs its dark and light palettes assigned.");
            if (!switchPrefab || !checkboxPrefab || !sliderPrefab || !segmentedPrefab || !progressPrefab || !buttonPrefab)
                throw new InvalidOperationException($"[Promo] '{name}' needs every control prefab assigned.");

            BuildPalette();
            BuildCard(stage);
            var parts = new[] { BuildAccents(stage), BuildModes(stage) };
            card.Rect.SetAsLastSibling();
            return parts;
        }

        /// <summary>
        /// The scene's palette, a copy of the dark one, and the dark and light colors each of its swatches is mixed
        /// from, matched by name.
        /// </summary>
        private void BuildPalette()
        {
            palette?.Dispose();
            palette = new ScenePalette(darkPalette);

            var lights = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in lightPalette.Enumerate()) lights[entry.name] = entry.paint.source.color;

            dark = new Color[palette.Count];
            light = new Color[palette.Count];
            accented = new bool[palette.Count];
            for (var i = 0; i < palette.Count; i++)
            {
                var swatch = palette.NameOf(i);
                dark[i] = palette[i];
                light[i] = lights.TryGetValue(swatch, out var match)
                    ? match
                    : throw new InvalidOperationException(
                        $"[Promo] '{lightPalette.name}' has no swatch '{swatch}' to match '{darkPalette.name}'.");
                accented[i] = Array.IndexOf(AccentSwatches, swatch) >= 0;
            }
        }

        /// <summary>
        /// Writes the palette for a mix of <paramref name="mode"/> between dark and light and the accent
        /// <paramref name="accent"/>, and colors the texts that follow it.
        /// </summary>
        private void WritePalette(float mode, Color accent)
        {
            for (var i = 0; i < dark.Length; i++)
            {
                var color = Color.LerpUnclamped(dark[i], light[i], mode);
                if (accented[i]) color = new Color(accent.r, accent.g, accent.b, color.a);
                palette.Write(i, color);
            }

            palette.Present();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            palette?.Dispose();
        }
    }
}
