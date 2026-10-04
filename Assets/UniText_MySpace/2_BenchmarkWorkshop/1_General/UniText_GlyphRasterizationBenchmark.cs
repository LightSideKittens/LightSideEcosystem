using System.Collections;
using System.Collections.Generic;
using LightSide;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// UniText glyph rasterization: clears all live glyph data, then times re-rasterizing the shared
/// glyph corpus (BenchmarkConfig) across the child text objects. Rasterization is async/GPU, so the
/// end-to-end time (CPU dispatch + GPU completion) is reported alongside the CPU frame time.
/// </summary>
public class UniText_GlyphRasterizationBenchmark : GlyphRasterBenchmarkBase
{
    const double PreparationTimeoutSeconds = 10.0;

    [SerializeField, Tooltip("Un-mutes the raster Cat zone so the per-pass ContourUnion phase/bail line reaches Logs/unitext.log. Needs the UNITEXT_DEBUG scripting define, else the counters read 0.")]
    bool dumpPhases;

    UniText[] targets;
    bool pendingSingleThreaded, pendingMaxStroke;
    bool abortRun;
    string completionMethod;
    bool wasParallel, wasForceST, wasMuted, wasSysDisabled, wasEmojiDisabled;
    List<(UniText text, Style style)> strokeStyles;
    readonly List<GlyphAtlas> targetAtlases = new();

    /// <summary>Wipe/heal cycles observed since the last diagnostics reset — a nonzero count during a pass means content was invalidated mid-run (fail-closed audit, device loss, target staleness) and explains missing meshes without any upload error.</summary>
    static int contentLostDuringPass;
    static bool contentLostHooked;

    static void OnAnyAtlasContentLost() => contentLostDuringPass++;

    protected override string EngineName => "UniText";
    protected override bool HasE2E => true;
    protected override string RequestedPath => "cpuGpuUpload";

    /// <summary>Runs one UniText glyph pass through the fail-closed raster and GpuUpload delivery route.</summary>
    public IEnumerator RunBenchmarkCoroutine(bool singleThreaded, bool maxStroke = false)
    {
        pendingSingleThreaded = singleThreaded;
        pendingMaxStroke = maxStroke;
        yield return RunPass((singleThreaded ? "SINGLE-THREADED" : "PARALLEL") + (maxStroke ? " + MAX-STROKE" : ""));
    }

    void Run(bool singleThreaded, bool maxStroke)
    {
        if (isRunning) return;
        StartCoroutine(RunBenchmarkCoroutine(singleThreaded, maxStroke));
    }

    [ContextMenu("Run Benchmark (Single-Threaded)")] public void RunBenchmark() => Run(true, false);
    [ContextMenu("Run Benchmark (Parallel)")] public void RunBenchmarkParallel() => Run(false, false);
    [ContextMenu("Run Benchmark (Single-Threaded + Max Stroke)")] public void RunBenchmarkMaxStroke() => Run(true, true);
    [ContextMenu("Run Benchmark (Parallel + Max Stroke)")] public void RunBenchmarkParallelMaxStroke() => Run(false, true);

    protected override void OnBeforeRun()
    {
        abortRun = false;
        completionMethod = "synchronous";
        if (!contentLostHooked)
        {
            contentLostHooked = true;
            GlyphAtlas.AnyAtlasContentLost += OnAnyAtlasContentLost;
        }
        wasParallel = UniTextBase.UseParallel;
        wasForceST = GlyphAtlas.forceSingleThreaded;
        wasMuted = CatZones.MuteAll;
        wasSysDisabled = SystemFont.Disabled;
        wasEmojiDisabled = EmojiFont.Disabled;
        foreach (var target in GetComponentsInChildren<UniText>(true))
            if (target.gameObject != gameObject)
                target.gameObject.SetActive(false);
        UniTextFont.ClearRuntimeData();
        GlyphAtlas.DisposeTextInstances();
        UniTextBase.UseParallel = !pendingSingleThreaded;
        GlyphAtlas.forceSingleThreaded = pendingSingleThreaded;
        CatZones.MuteAll = !dumpPhases;
        SystemFont.Disabled = true;
        EmojiFont.Disabled = true;
    }

    protected override void OnAfterRun()
    {
        UniTextBase.UseParallel = wasParallel;
        GlyphAtlas.forceSingleThreaded = wasForceST;
        CatZones.MuteAll = wasMuted;
        SystemFont.Disabled = wasSysDisabled;
        EmojiFont.Disabled = wasEmojiDisabled;

        if (strokeStyles != null)
        {
            foreach (var (text, style) in strokeStyles)
                text.Styles.Remove(style);
            strokeStyles = null;
        }

        UniTextFont.ClearRuntimeData();
        GlyphAtlas.DisposeTextInstances();
    }

    protected override bool CollectTargets()
    {
        var list = new List<UniText>();
        foreach (var ut in GetComponentsInChildren<UniText>(true))
            if (ut.gameObject != gameObject)
                list.Add(ut);
        targets = list.ToArray();

        if (targets.Length == 0)
        {
            SetRunStatus("skipped", "No UniText children were found");
            Debug.LogError("[UniText GlyphRaster] No UniText children found.");
            return false;
        }

        targetAtlases.Clear();
        var targetModes = new List<UniTextRenderMode>();
        foreach (var target in targets)
        {
            var mode = target.RenderMode;
            if (targetModes.Contains(mode)) continue;
            targetModes.Add(mode);
            targetAtlases.Add(GlyphAtlas.GetInstance(mode));
        }

        var glyphText = BenchmarkConfig.Instance != null ? BenchmarkConfig.Instance.GlyphRasterText : null;
        if (!string.IsNullOrEmpty(glyphText))
            foreach (var ut in targets)
                ut.Text = glyphText;

        if (pendingMaxStroke)
        {
            strokeStyles = new List<(UniText, Style)>();
            foreach (var ut in targets)
            {
                var style = Style.WholeText(new StrokeModifier { Width = UnitValue.Em(1f), Align = 1f });
                ut.Styles.Add(style);
                strokeStyles.Add((ut, style));
            }
        }
        return true;
    }

    protected override IEnumerator PrepareRun()
    {
        double timeoutAt = Time.realtimeSinceStartupAsDouble + PreparationTimeoutSeconds;
        while (true)
        {
            bool ready = true;
            string pendingReason = null;
            foreach (var atlas in targetAtlases)
            {
                GlyphAtlas.DeliveryPreparation preparation = default;
                string reason = null;
                System.Exception failure = null;
                try
                {
                    preparation = atlas.PrepareDelivery(out reason);
                }
                catch (System.Exception exception)
                {
                    failure = exception;
                }
                if (failure != null)
                {
                    SetRunStatus("failed", $"{failure.GetType().Name}: {failure.Message}");
                    Debug.LogException(failure);
                    yield break;
                }
                if (preparation == GlyphAtlas.DeliveryPreparation.Unsupported)
                {
                    SetRunStatus("unsupported", reason);
                    Debug.LogWarning($"[UniText GlyphRaster] {RequestedPath} is unsupported: {reason}.");
                    yield break;
                }
                if (preparation != GlyphAtlas.DeliveryPreparation.Preparing) continue;
                ready = false;
                pendingReason = reason;
            }

            if (ready) yield break;
            if (Time.realtimeSinceStartupAsDouble >= timeoutAt)
            {
                SetRunStatus("failed", $"{RequestedPath} preparation timed out after {PreparationTimeoutSeconds:F0} seconds"
                                       + (string.IsNullOrEmpty(pendingReason) ? "" : $": {pendingReason}"));
                Debug.LogWarning($"[UniText GlyphRaster] {RequestedPath} preparation exceeded {PreparationTimeoutSeconds:F0} seconds.");
                yield break;
            }
            yield return null;
        }
    }

    protected override void Deactivate()
    {
        if (targets == null) return;
        foreach (var ut in targets)
            ut.gameObject.SetActive(false);
    }

    protected override void ClearCaches() => UniTextFont.ClearRuntimeData();

    protected override void ResetExecutionDiagnostics()
    {
        contentLostDuringPass = 0;
        foreach (var atlas in targetAtlases)
            atlas.ResetStats();
    }

    protected override void Rasterize()
    {
        try
        {
            foreach (var ut in targets)
                ut.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
        }
        catch (System.Exception exception)
        {
            abortRun = true;
            SetRunStatus("failed", $"{exception.GetType().Name}: {exception.Message}");
            Debug.LogException(exception);
        }
    }

    protected override int CountGlyphs()
    {
        int count = 0;
        foreach (var atlas in targetAtlases)
            count += atlas.EntryCount;
        return count;
    }

    protected override IEnumerator AwaitAsyncCompletion(float cpuMs)
    {
        double dispatchStart = Time.realtimeSinceStartupAsDouble - cpuMs / 1000.0;
        completionMethod = "asyncGpuReadback";
        var textures = new List<Texture>(2);
        foreach (var atlas in targetAtlases)
            if (atlas.AtlasTexture != null)
                textures.Add(atlas.AtlasTexture);
        yield return AwaitGpuTextureCompletion(dispatchStart, textures,
            AbortGpuCompletionMeasurement);
    }

    void AbortGpuCompletionMeasurement(string reason)
    {
        abortRun = true;
        completionMethod = "unavailable";
        lastE2eMs = float.NaN;
        SetRunStatus("failed", reason);
        Debug.LogWarning($"[UniText GlyphRaster] {reason}; E2E measurement aborted.");
    }

    protected override bool ShouldAbortRun() => abortRun;

    protected override GlyphExecutionSample CaptureExecutionDiagnostics()
    {
        var sample = new GlyphExecutionSample
        {
            trigger = "GameObject.SetActive(true)",
            rasterBackend = "Cpu",
            threading = pendingSingleThreaded ? "forcedSingleThread" : "parallelAllowed",
            completion = completionMethod,
            fallback = "systemAndEmojiDisabled"
        };
        foreach (var atlas in targetAtlases)
        {
            var stats = atlas.GetStats();
            if (!ValidateAtlasDelivery(atlas, stats, out var reason) && runStatus == "measured")
            {
                abortRun = true;
                SetRunStatus("mismatch", reason);
                Debug.LogError($"[UniText GlyphRaster] {reason}");
            }
            sample.atlases.Add(ToExecutionData(atlas, stats));
        }
        return sample;
    }

    static bool ValidateAtlasDelivery(GlyphAtlas atlas, in GlyphAtlas.Stats stats,
        out string reason)
    {
        var texture = atlas.AtlasTexture;
        var material = LightSideMaterials.Ui;
        if (material == null)
        {
            reason = $"LightSideMaterials.Ui is NULL for {stats.Label} — the SDF shader was not "
                     + "resolved (UniTextSettings slot / Shader.Find in this build)";
            return false;
        }
        var samplerId = stats.Label == nameof(UniTextRenderMode.MSDF)
            ? Shader.PropertyToID("_MSDFTex")
            : Shader.PropertyToID("_MainTex");
        var bound = material.GetTexture(samplerId);
        if (texture == null || !ReferenceEquals(bound, texture))
        {
            reason = $"Atlas material binding mismatch for {stats.Label}: "
                     + $"atlas={(texture != null ? ObjectUtils.GetInstanceIdCompat(texture) : 0)}, "
                     + $"materialTexture={(bound != null ? ObjectUtils.GetInstanceIdCompat(bound) : 0)}, "
                     + $"entries={atlas.EntryCount}, pages={atlas.PageCount}, "
                     + $"contentLost={contentLostDuringPass}, shader={material.shader.name}";
            return false;
        }
        if (texture is not RenderTexture)
        {
            reason = $"Atlas storage for {stats.Label} is {texture.GetType().Name}, expected RenderTexture";
            return false;
        }
        if (stats.UploadBatches == 0)
        {
            reason = $"No GpuUpload batches were submitted for {stats.Label}"
                     + (stats.LastUploadError != null ? $" (lastError={stats.LastUploadError})" : "");
            return false;
        }
        reason = null;
        return true;
    }

    static GlyphAtlasExecutionData ToExecutionData(GlyphAtlas atlas, in GlyphAtlas.Stats stats)
    {
        return new GlyphAtlasExecutionData
        {
            mode = stats.Label,
            requestedPath = "cpuGpuUpload",
            backend = "Cpu",
            storage = atlas.AtlasTexture != null ? atlas.AtlasTexture.GetType().Name : "None",
            preparation = GlyphAtlas.forceSingleThreaded ? "SingleThreaded" : "ParallelAllowed",
            cpuMirror = false,
            gpuUploadTarget = true,
            writePaths = new List<string> { "gpuUploadRegions" },
            gpuUploadBatches = stats.UploadBatches,
            uploadedRegions = stats.UploadedRegions,
            uploadedBytes = stats.UploadedBytes,
            flushYields = stats.FlushYields,
            lastGpuUploadError = stats.LastUploadError?.ToString()
        };
    }

    protected override string Diagnostics(string label)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[Atlas {label}] ");
        foreach (var atlas in targetAtlases)
        {
            var tex = atlas.AtlasTexture;
            if (tex == null)
            {
                sb.Append($"pages={atlas.PageCount} tex=NULL  ");
                continue;
            }
            int depth = tex is RenderTexture rt ? rt.volumeDepth : tex is Texture2DArray ta ? ta.depth : 1;
            long storageMb = BenchmarkAtlasUtils.EstimatedStorageBytes(tex) / (1024 * 1024);
            int contentSlices = BenchmarkAtlasUtils.ContentSliceCount(tex, atlas.PageCount);
            string content = contentSlices >= 0 ? $" contentSlices={contentSlices}" : "";
            var stats = atlas.GetStats();
            sb.Append($"pages={atlas.PageCount} texSize={tex.width}x{tex.height}x{depth} texMB={storageMb}{content} pixelChecksum={BenchmarkAtlasUtils.Checksum(tex, atlas.PageCount)} uploadBatches={stats.UploadBatches} uploadRegions={stats.UploadedRegions} uploadMB={stats.UploadedBytes / (1024 * 1024)} flushYields={stats.FlushYields} contentLost={contentLostDuringPass} lastError={stats.LastUploadError?.ToString() ?? "none"} completion={completionMethod}  ");
        }
        return sb.ToString();
    }
}
