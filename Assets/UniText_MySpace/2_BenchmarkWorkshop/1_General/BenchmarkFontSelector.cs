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
/// drives it programmatically to measure every font in the list.
/// </summary>
public class BenchmarkFontSelector : MonoBehaviour
{
    [Serializable]
    public struct BenchmarkFontPair
    {
        public UniTextFont uniTextFont;

        [Tooltip("Dynamic TMP font asset of the same TTF.")]
        public TMP_FontAsset tmpFont;

        [Tooltip("Dynamic TextCore FontAsset for the UI Toolkit glyph benchmark, of the same TTF.")]
        public FontAsset uiToolkitFont;

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
                if (isOn) Apply(fonts[index]);
            });
            toggle.SetIsOnWithoutNotify(i == 0);
        }

        if (fonts.Count > 0)
            Apply(fonts[0]);
    }

    internal static void SetLabel(Toggle toggle, string text)
    {
        var label = toggle.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
            throw new InvalidOperationException($"Toggle template {toggle.name} requires a TMP_Text label");
        label.text = text;
    }

    /// <summary>Assigns the pair's fonts to the three glyph benchmarks.</summary>
    public void Apply(BenchmarkFontPair pair)
    {
        if (pair.uniTextFont != null)
        {
            var uniBench = ObjectUtils.FindAny<UniText_GlyphRasterizationBenchmark>();
            if (uniBench != null)
                foreach (var text in uniBench.GetComponentsInChildren<UniText>(true))
                    text.Font = pair.uniTextFont;
        }

        if (pair.tmpFont != null)
        {
            var tmpBench = ObjectUtils.FindAny<TMP_GlyphRasterizationBenchmark>();
            if (tmpBench != null)
                foreach (var text in tmpBench.GetComponentsInChildren<TMP_Text>(true))
                    text.font = pair.tmpFont;
        }

        if (pair.uiToolkitFont != null)
        {
            var uitkBench = ObjectUtils.FindAny<UIToolkit_GlyphRasterizationBenchmark>();
            if (uitkBench != null)
                uitkBench.fontAsset = pair.uiToolkitFont;
        }
    }
}
