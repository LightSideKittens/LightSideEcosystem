using System;
using System.Collections.Generic;
using LightSide;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

/// <summary>
/// Spawns a toggle per font pair and assigns the selected pair to the UniText, TMP and UI Toolkit
/// glyph-rasterization benchmarks, so all three run on the same font. The benchmark runner also
/// drives it programmatically to measure every font in the list, once per SDF spread.
/// </summary>
public class BenchmarkFontSelector : MonoBehaviour
{
    [Serializable]
    public struct BenchmarkFontPair
    {
        public UniTextFont uniTextFont;

        [Tooltip("Dynamic TMP font asset with the SDF spread plain text needs; the counterpart of UniText's effect-free passes.")]
        public TMP_FontAsset tmpFont;

        [Tooltip("Dynamic TMP font asset reserving a 0.5 em SDF spread for outlines; the counterpart of UniText's max-stroke passes.")]
        public TMP_FontAsset tmpOutlineFont;

        [Tooltip("Dynamic TextCore FontAsset for the UI Toolkit glyph benchmark with the SDF spread plain text needs (same TTF as the others).")]
        public FontAsset uiToolkitFont;

        [Tooltip("Dynamic TextCore FontAsset for the UI Toolkit glyph benchmark reserving a 0.5 em SDF spread for outlines.")]
        public FontAsset uiToolkitOutlineFont;

        public string Name =>
            uniTextFont != null ? uniTextFont.name :
            tmpFont != null ? tmpFont.name :
            uiToolkitFont != null ? uiToolkitFont.name : "font";
    }

    public List<BenchmarkFontPair> fonts = new();
    public Toggle togglePrefab;
    public ToggleGroup content;

    public IReadOnlyList<BenchmarkFontPair> Fonts => fonts;

    void Start()
    {
        for (int i = 0; i < fonts.Count; i++)
        {
            var toggle = Instantiate(togglePrefab, content.transform);
            toggle.group = content;

            string name = fonts[i].uniTextFont != null ? fonts[i].uniTextFont.name
                : fonts[i].tmpFont != null ? fonts[i].tmpFont.name
                : $"Font {i}";
            SetLabel(toggle, $"Font: {name}");

            int index = i;
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn) Apply(fonts[index], false);
            });
            toggle.SetIsOnWithoutNotify(i == 0);
        }

        if (fonts.Count > 0)
            Apply(fonts[0], false);
    }

    internal static void SetLabel(Toggle toggle, string text)
    {
        var label = toggle.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
            throw new InvalidOperationException($"Toggle template {toggle.name} requires a TMP_Text label");
        label.text = text;
    }

    /// <summary>Assigns the pair's fonts, the TMP and UI Toolkit ones with the plain or the outline SDF spread.</summary>
    public void Apply(BenchmarkFontPair pair, bool outlineSpread)
    {
        if (pair.uniTextFont != null)
        {
            var uniBench = ObjectUtils.FindAny<UniText_GlyphRasterizationBenchmark>();
            if (uniBench != null)
                foreach (var text in uniBench.GetComponentsInChildren<UniText>(true))
                    text.Font = pair.uniTextFont;
        }

        var tmpFont = outlineSpread ? pair.tmpOutlineFont : pair.tmpFont;
        if (tmpFont != null)
        {
            var tmpBench = ObjectUtils.FindAny<TMP_GlyphRasterizationBenchmark>();
            if (tmpBench != null)
                foreach (var text in tmpBench.GetComponentsInChildren<TMP_Text>(true))
                    text.font = tmpFont;
        }

        var uiToolkitFont = outlineSpread ? pair.uiToolkitOutlineFont : pair.uiToolkitFont;
        if (uiToolkitFont != null)
        {
            var uitkBench = ObjectUtils.FindAny<UIToolkit_GlyphRasterizationBenchmark>();
            if (uitkBench != null)
                uitkBench.fontAsset = uiToolkitFont;
        }
    }
}
