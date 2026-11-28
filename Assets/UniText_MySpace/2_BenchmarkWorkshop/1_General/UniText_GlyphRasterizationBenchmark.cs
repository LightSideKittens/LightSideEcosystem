using System;
using System.Collections;
using System.Collections.Generic;
using LightSide;
using UnityEngine;

/// <summary>
/// UniText's glyph operations on the child texts: the pair's UniText font, the house look in the styled tier, system and
/// emoji fallbacks off; a text is drawn once it commits its layout, which UniText does only after its glyphs were
/// uploaded through the GPU delivery path.
/// </summary>
public class UniText_GlyphRasterizationBenchmark : GlyphRasterBenchmarkBase
{
    const double PreparationTimeoutSeconds = 10.0;

    [SerializeField, Tooltip("Un-mutes the raster Cat zone so the per-pass ContourUnion phase line reaches Logs/unitext.log. Needs the UNITEXT_DEBUG scripting define, else the counters read 0.")]
    bool dumpPhases;

    static readonly string[] Engines = { BenchmarkEngines.UniTextSingleThreaded, BenchmarkEngines.UniTextParallel };

    static int contentLost;
    static bool contentLostHooked;

    UniText[] targets;
    readonly List<GlyphAtlas> atlases = new();
    bool styled;
    bool wasParallel, wasForceSingleThreaded, wasMuted, wasSystemDisabled, wasEmojiDisabled;
    long commits, commitsAtShow;
    Action countCommit;

    public override IReadOnlyList<string> EngineIds => Engines;

    public override void BeginCase(GlyphBenchmarkFonts.GlyphFont pair, GlyphTier tier)
    {
        if (pair.uniTextFont == null)
            throw new InvalidOperationException($"Font pair '{pair.Name}' has no UniText font.");
        var list = new List<UniText>();
        foreach (var text in GetComponentsInChildren<UniText>(true))
            if (text.gameObject != gameObject)
                list.Add(text);
        if (list.Count == 0)
            throw new InvalidOperationException("The UniText glyph benchmark has no child texts.");
        targets = list.ToArray();
        if (!contentLostHooked)
        {
            contentLostHooked = true;
            GlyphAtlas.AnyAtlasContentLost += () => contentLost++;
        }

        wasParallel = UniTextBase.UseParallel;
        wasForceSingleThreaded = GlyphAtlas.forceSingleThreaded;
        wasMuted = CatZones.MuteAll;
        wasSystemDisabled = SystemFont.Disabled;
        wasEmojiDisabled = EmojiFont.Disabled;
        CatZones.MuteAll = !dumpPhases;
        SystemFont.Disabled = true;
        EmojiFont.Disabled = true;
        countCommit ??= () => commits++;

        styled = tier == GlyphTier.Styled;
        foreach (var text in targets)
        {
            text.gameObject.SetActive(false);
            text.Font = pair.uniTextFont;
            if (styled) LookCalibrationProfile.ApplyUniText(text, LookCalibrationProfile.House);
            text.LayoutCommitted += countCommit;
        }
        UniTextFont.ClearRuntimeData();
        GlyphAtlas.DisposeTextInstances();
        atlases.Clear();
        foreach (var text in targets)
        {
            var atlas = GlyphAtlas.GetInstance(text.RenderMode);
            if (!atlases.Contains(atlas)) atlases.Add(atlas);
        }
    }

    public override void EndCase()
    {
        if (targets == null) return;
        foreach (var text in targets)
        {
            text.gameObject.SetActive(false);
            text.LayoutCommitted -= countCommit;
            if (!styled) continue;
            text.ClearWholeText<StrokeModifier>();
            text.ClearWholeText<ShadowModifier>();
        }
        targets = null;
        atlases.Clear();
        UniTextBase.UseParallel = wasParallel;
        GlyphAtlas.forceSingleThreaded = wasForceSingleThreaded;
        CatZones.MuteAll = wasMuted;
        SystemFont.Disabled = wasSystemDisabled;
        EmojiFont.Disabled = wasEmojiDisabled;
        UniTextFont.ClearRuntimeData();
        GlyphAtlas.DisposeTextInstances();
    }

    public override void SelectEngine(string engineId)
    {
        var parallel = engineId switch
        {
            BenchmarkEngines.UniTextParallel => true,
            BenchmarkEngines.UniTextSingleThreaded => false,
            _ => throw new ArgumentException($"UniText measures no engine '{engineId}'.", nameof(engineId))
        };
        UniTextBase.UseParallel = parallel;
        GlyphAtlas.forceSingleThreaded = !parallel;
    }

    public override IEnumerator Clear(string text)
    {
        Hide();
        yield return null;
        UniTextFont.ClearRuntimeData();
        foreach (var target in targets)
            target.Text = text;
        yield return PrepareDelivery();
        contentLost = 0;
        foreach (var atlas in atlases)
            atlas.ResetStats();
    }

    IEnumerator PrepareDelivery()
    {
        var timeoutAt = Time.realtimeSinceStartupAsDouble + PreparationTimeoutSeconds;
        while (true)
        {
            string pending = null;
            foreach (var atlas in atlases)
            {
                var preparation = atlas.PrepareDelivery(out var reason);
                if (preparation == GlyphAtlas.DeliveryPreparation.Unsupported)
                    throw new NotSupportedException($"UniText's GPU glyph delivery is unsupported: {reason}.");
                if (preparation == GlyphAtlas.DeliveryPreparation.Preparing) pending = reason ?? "preparing";
            }
            if (pending == null) yield break;
            if (Time.realtimeSinceStartupAsDouble >= timeoutAt)
                throw new TimeoutException($"UniText's GPU glyph delivery was not ready after {PreparationTimeoutSeconds:F0} seconds: {pending}.");
            yield return null;
        }
    }

    public override void Show()
    {
        commitsAtShow = commits;
        foreach (var text in targets)
            text.gameObject.SetActive(true);
    }

    public override bool Drawn => commits - commitsAtShow >= targets.Length;

    public override int GlyphCount
    {
        get
        {
            var count = 0;
            foreach (var atlas in atlases)
                count += atlas.EntryCount;
            return count;
        }
    }

    public override void Hide()
    {
        foreach (var text in targets)
            text.gameObject.SetActive(false);
    }

    /// <summary>Throws when an atlas the texts draw from is not the bound GPU-upload render texture or received no upload batch.</summary>
    public override void ValidateDelivery()
    {
        var material = LightSideMaterials.Ui(LightSideSurfaceTier.Text);
        if (material == null)
            throw new InvalidOperationException("LightSideMaterials.Ui(Text) is null: the SDF shader was not resolved in this build.");
        foreach (var atlas in atlases)
        {
            var stats = atlas.GetStats();
            var texture = atlas.AtlasTexture;
            var bound = Shader.GetGlobalTexture(stats.Label == nameof(UniTextRenderMode.MSDF)
                ? LightSideShaderIds.GlyphMsdf
                : LightSideShaderIds.GlyphSdf);
            if (texture == null || !ReferenceEquals(bound, texture))
                throw new InvalidOperationException($"Atlas {stats.Label} is not the bound glyph texture: entries={atlas.EntryCount}, pages={atlas.PageCount}, contentLost={contentLost}.");
            if (texture is not RenderTexture)
                throw new InvalidOperationException($"Atlas {stats.Label} is a {texture.GetType().Name}, not the GPU-upload render texture.");
            if (stats.UploadBatches == 0)
                throw new InvalidOperationException($"Atlas {stats.Label} received no GPU upload batch" +
                                                    (stats.LastUploadError != null ? $" (last error {stats.LastUploadError})." : "."));
        }
    }
}
