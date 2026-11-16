using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A scene's own copy of a UniGUI palette: the real control prefabs a scene spawns paint from it instead of the
    /// project's, so the scene recolors them every frame the way a project would — by writing swatches — without
    /// touching the project's settings or assets.
    /// </summary>
    /// <remarks>
    /// Shapes that take the project's global palette are pointed at this one when adopted. A text colored from a
    /// global swatch is taken over instead: its swatch component is disabled and <see cref="Present"/> writes its
    /// color from this palette. The copy lives until <see cref="Dispose"/>.
    /// </remarks>
    public sealed class ScenePalette : IDisposable
    {
        private readonly UniShapePaints paints;
        private readonly ShapePaintSwatch[] entries;
        private readonly Color[] written;
        private readonly List<(UniText text, int swatch, float opacity)> texts = new();

        public ScenePalette(UniShapePaints source)
        {
            paints = ScriptableObject.CreateInstance<UniShapePaints>();
            paints.name = source.name + " (Scene)";
            paints.hideFlags = HideFlags.DontSave;
            entries = new List<ShapePaintSwatch>(source.Enumerate()).ToArray();
            written = new Color[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                written[i] = entries[i].paint.source.color;
                paints.Entries.Add(entries[i]);
            }
        }

        /// <summary>How many swatches the palette holds, in the source's order.</summary>
        public int Count => entries.Length;

        /// <summary>The color <paramref name="index"/> was last written, or the source's own before any write.</summary>
        public Color this[int index] => written[index];

        public Color this[string swatch] => written[IndexOf(swatch)];

        public string NameOf(int index) => entries[index].name;

        /// <summary>The position of <paramref name="swatch"/>, matched case-insensitively as shapes match it.</summary>
        /// <exception cref="ArgumentException">The palette has no such swatch.</exception>
        public int IndexOf(string swatch)
        {
            for (var i = 0; i < entries.Length; i++)
                if (string.Equals(entries[i].name, swatch, StringComparison.OrdinalIgnoreCase))
                    return i;
            throw new ArgumentException($"[Promo] Palette '{paints.name}' has no swatch '{swatch}'.", nameof(swatch));
        }

        /// <summary>
        /// Points every shape under <paramref name="root"/> that paints from the global palette at this one, and takes
        /// over every text under it colored from a global swatch.
        /// </summary>
        public void Adopt(GameObject root)
        {
            foreach (var shape in root.GetComponentsInChildren<UniShape>(true))
                if (shape.Paints is GlobalSettingsShapePaintProvider)
                    shape.Paints = new AssetShapePaintProvider { Asset = paints };

            foreach (var swatch in root.GetComponentsInChildren<UniTextSwatch>(true))
            {
                texts.Add((swatch.GetComponent<UniText>(), IndexOf(swatch.Swatch), swatch.Opacity));
                swatch.enabled = false;
            }
        }

        /// <summary>Paints <paramref name="index"/> <paramref name="color"/>; a color already written changes nothing.</summary>
        public void Write(int index, Color color)
        {
            if (color == written[index]) return;
            written[index] = color;
            var entry = entries[index];
            entry.paint.source.color = color;
            paints.Entries.Replace(index, entry);
        }

        /// <summary>
        /// Writes the film's warm livery: every neutral swatch turned to the hue of <paramref name="theme"/>'s surface at
        /// its own lightness, the accent swatches to the theme's orange and the focus ring to its coral.
        /// </summary>
        /// <remarks>
        /// A swatch counts as neutral below <see cref="NeutralSaturation"/>, which leaves status colors such as danger
        /// and success as they ship; a black swatch such as a scrim has no hue to turn.
        /// </remarks>
        public void Warm(Theme theme)
        {
            Color.RGBToHSV(theme.Surface, out var warmHue, out _, out _);
            for (var i = 0; i < entries.Length; i++)
            {
                var color = written[i];
                switch (entries[i].name)
                {
                    case "Accent":
                    case "AccentSoft":
                        color = Theme.Fade(theme.Orange, color.a);
                        break;
                    case "Focus":
                        color = Theme.Fade(theme.Coral, color.a);
                        break;
                    default:
                        Color.RGBToHSV(color, out _, out var saturation, out var value);
                        if (saturation < NeutralSaturation)
                            color = Theme.Fade(
                                Color.HSVToRGB(warmHue, Mathf.Min(saturation * WarmSaturation, MaxWarmth), value), color.a);
                        break;
                }

                Write(i, color);
            }
        }

        private const float NeutralSaturation = 0.35f;
        private const float WarmSaturation = 1.2f;
        private const float MaxWarmth = 0.3f;

        /// <summary>Colors every adopted text from its swatch as last written.</summary>
        public void Present()
        {
            foreach (var (text, swatch, opacity) in texts)
            {
                var color = written[swatch];
                color.a *= opacity;
                text.color = color;
            }
        }

        public void Dispose() => ObjectUtils.SafeDestroy(paints);
    }
}
