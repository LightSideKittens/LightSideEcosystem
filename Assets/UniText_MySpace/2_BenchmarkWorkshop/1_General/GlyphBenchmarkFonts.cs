using System;
using System.Collections.Generic;
using LightSide;
using TMPro;
using UnityEngine;

/// <summary>The fonts of the glyph suite, in the order of its cases: one font file per pair, each with the corpus it is asked for.</summary>
public class GlyphBenchmarkFonts : MonoBehaviour
{
    [Serializable]
    public struct GlyphFont
    {
        public UniTextFont uniTextFont;

        [Tooltip("Dynamic TMP font asset of the same font file: its source font, sampling size, padding and atlas size make the rival font assets of the plain tier.")]
        public TMP_FontAsset tmpFont;

        [Tooltip("Text the glyph cases of this font draw, the same for every engine. Shared takes the list set on BenchmarkConfig.")]
        public GlyphCorpus corpus;

        /// <summary>The font's name in case and participant ids.</summary>
        public string Name =>
            uniTextFont != null ? uniTextFont.name :
            tmpFont != null ? tmpFont.name : "font";
    }

    public List<GlyphFont> fonts = new();

    public IReadOnlyList<GlyphFont> Fonts => fonts;
}
