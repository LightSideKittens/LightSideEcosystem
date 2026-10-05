using System.Collections;
using System.Collections.Generic;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Glyph-rasterization comparison across UniText, TextMeshPro and UI Toolkit, repeated per font offered
/// by <see cref="BenchmarkFontSelector"/>; TMP and UI Toolkit run once per SDF spread of the pair.
/// </summary>
public sealed class GlyphRasterizationSuite : MonoBehaviour, IBenchmarkSuite
{
    readonly Dictionary<string, Dictionary<string, GlyphRasterData>> results = new();

    public string SuiteId => "glyph";

    public string Section => "glyphRasterization";

    public string StreamGlobal => "__unitextGlyphRuns";

    public int Scenario => 3;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("glyphRasterization",
            "Every engine starts with cleared glyph/character tables, retained allocated atlas storage, and a disabled pre-created text component; rasterization is triggered only by enabling that component. TMP and UI Toolkit run twice per font: with a plain-text SDF spread (6 px at 64 pt; keys tmp, uiToolkit), the counterpart of UniText's effect-free passes, and with a 0.5 em spread (32 px at 64 pt; keys tmpOutline, uiToolkitOutline), the counterpart of UniText's max-stroke passes. CPU trigger/dispatch is reported where the trigger runs the work; UI Toolkit schedules it for the panel render and reports none. Every engine uses the same one-texel-per-atlas-layer AsyncGPUReadback boundary for component-to-GPU-atlas-ready latency, the comparable number across engines. The recorded execution samples are authoritative for CPU/GPU raster, atlas write path, CPU mirror residency, GpuUpload use, and completion method."),
        new KeyValuePair<string, string>("fontIsolation",
            "UI Toolkit uses explicit Panel Text Settings with local/global/default/sprite/emoji/Dynamic OS fallbacks disabled and validated; TMP temporarily disables local/global/default/sprite/emoji fallbacks; UniText disables system-font and emoji fallback sources for the glyph suite.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        var fontSelector = ObjectUtils.FindAny<BenchmarkFontSelector>();
        WarnIfSceneNotSterile(context, fontSelector);

        if (fontSelector != null && fontSelector.Fonts.Count > 0)
        {
            foreach (var pair in fontSelector.Fonts)
            {
                fontSelector.Apply(pair, false);
                yield return null;
                yield return RunGlyphForFont(context, pair.Name, fontSelector, pair);
                if (!context.Alive) yield break;
            }
        }
        else
        {
            yield return RunGlyphForFont(context, "default", null, default);
        }
    }

    public JObject Serialize() => BenchmarkJsonSerializer.SerializeGlyphRasterization(results);

    public bool Measured(out string reason)
    {
        reason = null;
        var measured = false;
        foreach (var engine in results.Values)
        {
            foreach (var result in engine.Values)
            {
                measured |= result.status == "measured";
                if (result.status is "failed" or "partial" or "mismatch" or "measuring")
                {
                    reason = $"Requested glyph suite ended with result status '{result.status}'.";
                    return false;
                }
            }
        }
        return measured;
    }

    /// <summary>
    /// Glyph benchmarks count atlas deltas, so an enabled text drawing from a measured atlas turns them into
    /// warm cache hits: every UniText text, which all share one atlas, and every TMP text using a measured
    /// font asset. Texts with other TMP font assets, such as the font selector's own labels, cannot reach them.
    /// </summary>
    void WarnIfSceneNotSterile(BenchmarkContext context, BenchmarkFontSelector fontSelector)
    {
        var measuredTmpFonts = new HashSet<TMPro.TMP_FontAsset>();
        if (fontSelector != null)
            foreach (var pair in fontSelector.Fonts)
            {
                if (pair.tmpFont != null)
                    measuredTmpFonts.Add(pair.tmpFont);
                if (pair.tmpOutlineFont != null)
                    measuredTmpFonts.Add(pair.tmpOutlineFont);
            }
        var tmpGlyph = ObjectUtils.FindAny<TMP_GlyphRasterizationBenchmark>();
        if (tmpGlyph != null)
            foreach (var text in tmpGlyph.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (text.font != null)
                    measuredTmpFonts.Add(text.font);

        int live = 0;
        foreach (var text in ObjectUtils.FindAll<UniTextBase>())
            if (text.isActiveAndEnabled)
                live++;
        foreach (var text in ObjectUtils.FindAll<TMPro.TMP_Text>())
            if (text.isActiveAndEnabled && measuredTmpFonts.Contains(text.font))
                live++;
        if (live == 0) return;
        context.Error($"{live} enabled text component(s) alive before glyph phase — counts may be skewed");
        Debug.LogWarning($"[GlyphRasterizationSuite] {live} enabled text component(s) alive before glyph phase");
    }

    IEnumerator RunGlyphForFont(BenchmarkContext context, string font, BenchmarkFontSelector fontSelector,
        BenchmarkFontSelector.BenchmarkFontPair pair)
    {
        GlyphRasterBenchmarkBase.CurrentFontLabel = font;
        var uniGlyph = ObjectUtils.FindAny<UniText_GlyphRasterizationBenchmark>();
        if (uniGlyph != null)
        {
            var variants = new (string key, bool singleThreaded, bool maxStroke)[]
            {
                ("unitextSingleThreaded", true, false),
                ("unitextParallel", false, false),
                ("unitextSingleThreadedMaxStroke", true, true),
                ("unitextParallelMaxStroke", false, true),
            };
            foreach (var v in variants)
            {
                Debug.Log($"[GlyphRasterizationSuite] Running UniText Glyph Rasterization ({v.key}, {font})...");
                yield return context.ThermalSettle();
                yield return context.Run($"unitextGlyph.{v.key}.{font}",
                    () => uniGlyph.RunBenchmarkCoroutine(v.singleThreaded, v.maxStroke),
                    () => Store(v.key, font, uniGlyph.LastResults));
                if (!context.Alive) yield break;
            }
        }

        var tmpGlyph = ObjectUtils.FindAny<TMP_GlyphRasterizationBenchmark>();
        if (tmpGlyph != null)
            foreach (var outline in Spreads)
            {
                if (outline && (fontSelector == null || pair.tmpOutlineFont == null)) continue;
                if (fontSelector != null) fontSelector.Apply(pair, outline);
                var key = outline ? "tmpOutline" : "tmp";
                Debug.Log($"[GlyphRasterizationSuite] Running TMP Glyph Rasterization ({key}, {font})...");
                yield return context.ThermalSettle();
                yield return context.Run($"tmpGlyph.{key}.{font}",
                    () => tmpGlyph.RunBenchmarkCoroutine(),
                    () => Store(key, font, tmpGlyph.LastResults));
                if (!context.Alive) yield break;
            }

        var uitkGlyph = ObjectUtils.FindAny<UIToolkit_GlyphRasterizationBenchmark>();
        if (uitkGlyph != null)
            foreach (var outline in Spreads)
            {
                if (outline && (fontSelector == null || pair.uiToolkitOutlineFont == null)) continue;
                if (fontSelector != null) fontSelector.Apply(pair, outline);
                var key = outline ? "uiToolkitOutline" : "uiToolkit";
                Debug.Log($"[GlyphRasterizationSuite] Running UIToolkit Glyph Rasterization ({key}, {font})...");
                yield return context.ThermalSettle();
                yield return context.Run($"uiToolkitGlyph.{key}.{font}",
                    () => uitkGlyph.RunBenchmarkCoroutine(),
                    () => Store(key, font, uitkGlyph.LastResults));
                if (!context.Alive) yield break;
            }

        if (fontSelector != null) fontSelector.Apply(pair, false);
    }

    static readonly bool[] Spreads = { false, true };

    void Store(string engineKey, string font, GlyphRasterData result)
    {
        if (result == null) return;
        if (!results.TryGetValue(engineKey, out var byFont))
            results[engineKey] = byFont = new Dictionary<string, GlyphRasterData>();
        byFont[font] = result;
    }
}
