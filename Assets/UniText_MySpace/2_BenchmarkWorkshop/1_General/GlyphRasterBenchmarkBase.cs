using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>What the glyphs of a glyph case must be able to draw.</summary>
public enum GlyphTier
{
    /// <summary>Plain text: rival font assets at the padding of the pair's TMP asset, no effects.</summary>
    Plain,

    /// <summary>The house look of <see cref="LookCalibrationProfile"/>: rival font assets padded to hold it, every engine drawing it.</summary>
    Styled
}

/// <summary>
/// One engine's half of the glyph suite: a case opens a font pair and tier, every operation clears the case font's glyphs
/// under a hidden text, shows it, and waits for the engine's own signal that the shown text was drawn.
/// </summary>
/// <remarks>
/// Rival font assets are made per case from the source font of the pair's TMP asset at that asset's sampling size and atlas
/// size, with font features on; they are destroyed when the case ends. Cleared glyph tables keep the atlas storage they
/// grew, so every operation after a case's first rasterizes into allocated textures.
/// </remarks>
public abstract class GlyphRasterBenchmarkBase : MonoBehaviour
{
    [Header("Profiling")]
    [Tooltip("After a case's rounds, rasterize its largest text once more under a Prof capture shipped beside the results. Tree depth follows the engine's manual zones (UniText: UNITEXT_PROFILE).")]
    public bool captureProfile;

    [Tooltip("Also record per-zone allocation in that capture; it costs an allocation probe on every zone.")]
    public bool captureAlloc;

    /// <summary>The engine configurations this component measures, as arm ids.</summary>
    public abstract IReadOnlyList<string> EngineIds { get; }

    /// <summary>Why this player cannot run the engine, or null when it can.</summary>
    public virtual string UnsupportedReason => null;

    /// <summary>The settings of the rival font asset the open case made, null for UniText and outside a case.</summary>
    public JObject Rival { get; protected set; }

    /// <summary>Opens a case: the engine's texts take the pair's font in <paramref name="tier"/>, every fallback source off, and stay hidden.</summary>
    public abstract void BeginCase(GlyphBenchmarkFonts.GlyphFont pair, GlyphTier tier);

    /// <summary>Hides the texts, releases what the case made and restores what it changed.</summary>
    public abstract void EndCase();

    /// <summary>Sets the process-wide modes of <paramref name="engineId"/>.</summary>
    public abstract void SelectEngine(string engineId);

    /// <summary>Undoes the modes of <see cref="SelectEngine"/> that must not outlive one operation, because they would slow every other engine.</summary>
    public virtual void ReleaseEngine() { }

    /// <summary>Adds what an arm of <paramref name="engineId"/> ran under beyond the package's descriptor keys.</summary>
    public virtual void DescribeEngine(string engineId, JObject arm) { }

    /// <summary>Hides the texts, empties the case font's glyph tables and gives the texts <paramref name="text"/>; untimed.</summary>
    public abstract IEnumerator Clear(string text);

    /// <summary>Shows the texts: the trigger of the measured rasterization.</summary>
    public abstract void Show();

    /// <summary>Whether the engine has drawn every text shown since <see cref="Show"/>, its glyphs in the atlas; read once per frame.</summary>
    public abstract bool Drawn { get; }

    /// <summary>Glyphs in the case font's atlas.</summary>
    public abstract int GlyphCount { get; }

    /// <summary>Hides the texts.</summary>
    public abstract void Hide();

    /// <summary>Throws when the drawn glyphs reached the screen by another path than the engine's measured one.</summary>
    public virtual void ValidateDelivery() { }

    /// <summary>
    /// The sampling size, padding and atlas size of the rival font assets of <paramref name="pair"/> in
    /// <paramref name="tier"/>, also written to <see cref="Rival"/> with <paramref name="fontFeatures"/>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The pair has no dynamic TMP asset with its source font.</exception>
    protected (Font source, int pointSize, int padding, int width, int height) RivalAtlas(
        GlyphBenchmarkFonts.GlyphFont pair, GlyphTier tier, bool fontFeatures)
    {
        var template = pair.tmpFont;
        if (template == null || template.sourceFontFile == null)
            throw new System.InvalidOperationException($"Font pair '{pair.Name}' needs a TMP asset with its source font.");
        var pointSize = Mathf.RoundToInt(template.faceInfo.pointSize);
        var padding = tier == GlyphTier.Plain
            ? template.atlasPadding
            : Mathf.CeilToInt(pointSize * (float)LookCalibrationProfile.UIToolkitAtlasPadding / LookCalibrationProfile.UIToolkitSamplingPointSize);
        Rival = new JObject
        {
            ["samplingPointSize"] = pointSize,
            ["padding"] = padding,
            ["atlas"] = $"{template.atlasWidth}x{template.atlasHeight}",
            ["renderMode"] = "SDFAA",
            ["multiAtlas"] = true,
            ["fontFeatures"] = fontFeatures
        };
        return (template.sourceFontFile, pointSize, padding, template.atlasWidth, template.atlasHeight);
    }
}
