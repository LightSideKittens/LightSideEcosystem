using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using LightSide;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using LightSide.Benchmark;

/// <summary>
/// Shared glyph-rasterization harness: clears the engine's glyph atlas, times the re-fill of one
/// workload, and counts the newly rasterized glyphs — once per iteration with warmup, GC and
/// atlas-delta bookkeeping. Concretes plug in how their engine clears, rasterizes and counts.
/// </summary>
public abstract class GlyphRasterBenchmarkBase : MonoBehaviour
{
    [Header("Settings")]
    public int iterations = 5;
    public int warmupIterations = 1;
    [Min(0), Tooltip("Frames to keep the rasterized workload visible after each measured pass. Set to 0 for unattended runs.")]
    public int previewFrames = 8;

    [Header("Profiling")]
    [Tooltip("After the timed runs, do one extra rasterization recorded through Prof. Tree depth follows the engine's manual zones (UniText: UNITEXT_PROFILE).")]
    public bool captureProfile;

    [Tooltip("Also record per-zone allocation. Costs an allocation probe on every zone — leave off for a fast time-only tree.")]
    public bool captureAlloc;

    [Header("Status")]
    [SerializeField] protected bool isRunning;
    [SerializeField, TextArea(15, 30)] protected string lastResult = "";

    protected readonly Stopwatch sw = new();
    protected readonly StringBuilder report = new();
    protected float lastE2eMs;
    protected string runStatus;
    protected string runStatusReason;
    bool gpuCompletionProbeLogged;
    bool cleanupPending;

    public GlyphRasterData LastResults { get; private set; }

    /// <summary>Font under test for the current glyph phase, stamped by the runner's per-font loop. Folded into every screenshot name so each font keeps its own per-stage captures; the shared base otherwise names only engine + stage, collapsing both fonts onto one file per stage.</summary>
    public static string CurrentFontLabel;

    protected abstract string EngineName { get; }

    /// <summary>Gather the workload (children, fonts, target atlas) and apply the shared corpus. Return false — after logging — when there is nothing to run.</summary>
    protected abstract bool CollectTargets();

    protected abstract void ClearCaches();

    /// <summary>The timed work: re-rasterize the workload into the freshly cleared atlas.</summary>
    protected abstract void Rasterize();

    protected abstract int CountGlyphs();

    protected virtual void OnBeforeRun() { }
    protected virtual void OnAfterRun() { }
    protected virtual string RequestedPath => "engineNative";

    protected virtual IEnumerator PrepareRun()
    {
        yield break;
    }

    protected void SetRunStatus(string status, string reason)
    {
        runStatus = status;
        runStatusReason = reason;
    }

    /// <summary>Turn the workload off before clearing so nothing re-rasterizes during the clear frame. No-op for engines that rasterize a font asset directly.</summary>
    protected virtual void Deactivate() { }

    protected virtual void ShowPreview() { }

    protected virtual string Diagnostics(string label) => "";

    protected virtual void ResetExecutionDiagnostics() { }

    protected virtual GlyphExecutionSample CaptureExecutionDiagnostics() => null;

    protected virtual bool ShouldAbortRun() => false;

    /// <summary>True when component activation completes after the immediate timed call and <see cref="AwaitAsyncCompletion"/> records a distinct component-to-atlas-ready latency.</summary>
    protected virtual bool HasE2E => false;

    protected virtual IEnumerator AwaitAsyncCompletion(float cpuMs)
    {
        lastE2eMs = cpuMs;
        yield break;
    }

    /// <summary>The probe infrastructure is unavailable (readback/format limits of the platform), which
    /// invalidates only the e2e number — CPU timings stay measured. A missing atlas or a probe timeout
    /// stays a hard abort: those are engine anomalies, not environment limits.</summary>
    void DegradeE2e(string reason)
    {
        lastE2eMs = float.NaN;
        Debug.LogWarning($"[{EngineName} GlyphRaster] {reason}; e2e unavailable, CPU timings kept.");
    }

    /// <summary>Extends the component-trigger interval to one common GPU completion boundary by reading one texel from every atlas layer without stalling the CPU thread.</summary>
    protected IEnumerator AwaitGpuTextureCompletion(double dispatchStart,
        IReadOnlyList<Texture> textures, System.Action<string> abort)
    {
        if (textures == null || textures.Count == 0)
        {
            abort("No atlas texture was created");
            yield break;
        }
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            DegradeE2e("Async GPU readback is unsupported");
            yield break;
        }

        var requests = new List<AsyncGPUReadbackRequest>(textures.Count);
        bool queueFailed = false;
        for (int i = 0; i < textures.Count; i++)
        {
            var texture = textures[i];
            if (texture == null) continue;
            try
            {
#if UNITY_2023_2_OR_NEWER
                if (!SystemInfo.IsFormatSupported(texture.graphicsFormat, GraphicsFormatUsage.ReadPixels))
#else
                if (!SystemInfo.IsFormatSupported(texture.graphicsFormat, FormatUsage.ReadPixels))
#endif
                {
                    queueFailed = true;
                    Debug.LogWarning($"[{EngineName} GlyphRaster] Async readback completion probe does not support {texture.graphicsFormat}.");
                    continue;
                }
                int depth = texture is RenderTexture renderTexture ? renderTexture.volumeDepth
                    : texture is Texture2DArray array ? array.depth
                    : 1;
                requests.Add(AsyncGPUReadback.Request(texture, 0, 0, 1, 0, 1, 0, depth));
            }
            catch (System.Exception exception)
            {
                queueFailed = true;
                Debug.LogWarning($"[{EngineName} GlyphRaster] Async readback completion probe could not be queued: {exception.GetType().Name}.");
            }
        }

        if (requests.Count == 0)
        {
            DegradeE2e("No completion probe could be queued");
            yield break;
        }
        if (!gpuCompletionProbeLogged)
        {
            gpuCompletionProbeLogged = true;
            Debug.Log($"[{EngineName} GlyphRaster] Using a 1x1-per-layer async readback probe for GPU completion timing.");
        }

        const double timeoutSeconds = 15.0;
        double timeoutAt = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
        var completed = new bool[requests.Count];
        bool failed = queueFailed;
        while (true)
        {
            bool done = true;
            for (int i = 0; i < requests.Count; i++)
            {
                if (completed[i]) continue;
                var request = requests[i];
                if (!request.done)
                {
                    done = false;
                    continue;
                }
                failed |= request.hasError;
                completed[i] = true;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= timeoutAt)
            {
                abort($"Completion probe exceeded {timeoutSeconds:F0} seconds");
                yield break;
            }
            if (done)
            {
                if (failed)
                    DegradeE2e("Completion probe failed");
                else
                    lastE2eMs = (float)((now - dispatchStart) * 1000.0);
                yield break;
            }
            yield return null;
        }
    }

    protected IEnumerator RunPass(string mode)
    {
        isRunning = true;
        cleanupPending = true;
        report.Clear();
        LastResults = null;
        runStatus = "measured";
        runStatusReason = null;
        var frameTimes = new List<float>();
        var e2eTimes = HasE2E ? new List<float>() : null;
        var glyphCounts = new List<int>();
        var executionSamples = new List<GlyphExecutionSample>();
        long totalManagedAlloc = 0;
        try
        {
            OnBeforeRun();

            if (!CollectTargets())
            {
                if (runStatus == "measured")
                    SetRunStatus("skipped", "Target collection rejected the run");
                CompleteResults(frameTimes, e2eTimes, glyphCounts, executionSamples,
                    totalManagedAlloc, mode);
                yield break;
            }

            AppendHeader(mode);

            for (int iter = -warmupIterations; iter < iterations; iter++)
            {
                Deactivate();
                yield return null;

                string beforeClear = Diagnostics("BEFORE clear");
                ClearCaches();
                yield return null;
                yield return PrepareRun();
                if (runStatus is "unsupported" or "skipped" or "failed")
                {
                    CompleteResults(frameTimes, e2eTimes, glyphCounts, executionSamples,
                        totalManagedAlloc, mode);
                    yield break;
                }
                ResetExecutionDiagnostics();
                string afterClear = Diagnostics("AFTER clear");

                int glyphsBefore = CountGlyphs();
                var allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                Rasterize();
                sw.Stop();
                var allocated = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                float ms = (float)sw.Elapsed.TotalMilliseconds;
                if (ShouldAbortRun())
                {
                    if (runStatus == "measured")
                        SetRunStatus("failed", "Rasterization aborted before completion");
                    var failedExecution = CaptureExecutionDiagnostics();
                    if (failedExecution != null) executionSamples.Add(failedExecution);
                    CompleteResults(frameTimes, e2eTimes, glyphCounts, executionSamples,
                        totalManagedAlloc, mode);
                    yield break;
                }
                lastE2eMs = ms;
                if (HasE2E) yield return AwaitAsyncCompletion(ms);
                float e2eMs = lastE2eMs;

                int uniqueGlyphs = CountGlyphs() - glyphsBefore;
                var execution = CaptureExecutionDiagnostics();
                string afterRaster = Diagnostics("AFTER raster");
                ShowPreview();

                bool isWarmup = iter < 0;
                string tag = isWarmup ? "warmup" : $"iter {iter + 1}";
                Debug.Log($"[{EngineName} GlyphRaster{(mode != null ? $" {mode}" : "")}] {tag}: {ms:F2}ms" +
                          (HasE2E ? $" (e2e {e2eMs:F2}ms)" : "") + $", +{uniqueGlyphs} glyphs\n  {beforeClear}\n  {afterClear}\n  {afterRaster}");

                if (ShouldAbortRun())
                {
                    if (runStatus == "measured")
                        SetRunStatus("failed", "Run aborted before completion");
                    if (execution != null)
                        executionSamples.Add(execution);
                    if (!isWarmup && runStatus == "mismatch")
                    {
                        frameTimes.Add(ms);
                        if (!float.IsNaN(e2eMs) && !float.IsInfinity(e2eMs))
                            e2eTimes?.Add(e2eMs);
                        glyphCounts.Add(uniqueGlyphs);
                        totalManagedAlloc += allocated;
                    }
                    CompleteResults(frameTimes, e2eTimes, glyphCounts, executionSamples,
                        totalManagedAlloc, mode);
                    yield break;
                }

                int holdFrames = Mathf.Max(0, previewFrames);
                for (int frame = 0; frame < holdFrames; frame++)
                    yield return null;

                if (!isWarmup)
                {
                    frameTimes.Add(ms);
                    if (!float.IsNaN(e2eMs) && !float.IsInfinity(e2eMs))
                        e2eTimes?.Add(e2eMs);
                    glyphCounts.Add(uniqueGlyphs);
                    if (execution != null) executionSamples.Add(execution);
                    totalManagedAlloc += allocated;
                }

                yield return BenchmarkScreenshot.Capture(
                    $"glyph-{EngineName}{(CurrentFontLabel != null ? $"-{CurrentFontLabel}" : "")}{(mode != null ? $"-{mode}" : "")}-{tag}");
            }

            if (captureProfile) yield return CaptureProfilePass();

            CompleteResults(frameTimes, e2eTimes, glyphCounts, executionSamples,
                totalManagedAlloc, mode);
        }
        finally
        {
            CleanupRun();
        }
    }

    void CleanupRun()
    {
        if (!cleanupPending) return;
        cleanupPending = false;
        try
        {
            Deactivate();
        }
        finally
        {
            try
            {
                OnAfterRun();
            }
            finally
            {
                isRunning = false;
            }
        }
    }

    void OnDisable()
    {
        if (!cleanupPending) return;
        try
        {
            StopAllCoroutines();
        }
        finally
        {
            CleanupRun();
        }
    }

    void CompleteResults(List<float> frameTimes, List<float> e2eTimes,
        List<int> glyphCounts, List<GlyphExecutionSample> executionSamples,
        long totalManagedAlloc, string mode)
    {
        LastResults = new GlyphRasterData
        {
            frameTimes = frameTimes,
            e2eTimes = e2eTimes,
            uniqueGlyphs = glyphCounts.Count > 0 ? glyphCounts[0] : 0,
            managedAlloc = totalManagedAlloc,
            status = runStatus,
            statusReason = runStatusReason,
            benchmark = new GlyphBenchmarkConfig
            {
                mode = mode,
                requestedPath = RequestedPath,
                iterations = iterations,
                warmupIterations = warmupIterations,
                previewFrames = previewFrames,
                captureProfile = captureProfile,
                captureAlloc = captureAlloc
            },
            executionSamples = executionSamples
        };

        AppendResults(frameTimes, e2eTimes, glyphCounts, totalManagedAlloc, mode);
        if (runStatus != "measured")
            report.AppendLine($"  Status: {runStatus}{(string.IsNullOrEmpty(runStatusReason) ? "" : $" — {runStatusReason}")}");
        lastResult = report.ToString();
        Debug.Log(lastResult);
    }

    /// <summary>One extra rasterization, off the timing stats, recorded through the instrumentation sink (<see cref="Prof"/>). Tree depth follows the engine's manual zones. The capture is saved beside the results.</summary>
    private IEnumerator CaptureProfilePass()
    {
        Deactivate();
        yield return null;
        ClearCaches();
        yield return null;
        ResetExecutionDiagnostics();

        Prof.SampleAlloc = captureAlloc;
        if (captureAlloc) ProfExactAlloc.Begin();
        Prof.BeginCapture();

        try
        {
            Rasterize();
            if (HasE2E) yield return AwaitAsyncCompletion(0f);

            var capture = Prof.EndCapture();
            ProfExactAlloc.End();
            ProfTransport.Ship(capture.ToJson(), $"prof/{BenchmarkRun.Id}/profile_{EngineName}.json");
        }
        finally
        {
            if (Prof.Capturing) Prof.EndCapture();
            ProfExactAlloc.End();
        }
    }

    private void AppendHeader(string mode)
    {
        report.AppendLine("═══════════════════════════════════════════════");
        report.AppendLine($"    {EngineName.ToUpperInvariant()} GLYPH RASTERIZATION BENCHMARK{(mode != null ? $" ({mode})" : "")}");
        report.AppendLine("═══════════════════════════════════════════════");
        report.AppendLine($"  Iterations: {iterations}  Warmup: {warmupIterations}");
        report.AppendLine("═══════════════════════════════════════════════");
    }

    private void AppendResults(List<float> frameTimes, List<float> e2eTimes, List<int> glyphCounts, long managedAlloc, string mode)
    {
        if (frameTimes.Count == 0) return;

        var sorted = new List<float>(frameTimes);
        sorted.Sort();
        float median = BenchmarkStatistics.MedianSorted(sorted);
        float sum = 0;
        for (int i = 0; i < sorted.Count; i++) sum += sorted[i];
        int typicalGlyphs = glyphCounts[0];

        report.AppendLine();
        for (int i = 0; i < frameTimes.Count; i++)
            report.AppendLine($"  Run {i + 1}: {frameTimes[i]:F2} ms" +
                              (e2eTimes != null && i < e2eTimes.Count ? $"   e2e {e2eTimes[i]:F2} ms" : "") + $"   ({glyphCounts[i]} glyphs)");

        report.AppendLine();
        if (mode != null) report.AppendLine($"  Mode: {mode}");
        report.AppendLine($"  Median:  {median:F2} ms");
        report.AppendLine($"  Average: {sum / sorted.Count:F2} ms");
        report.AppendLine($"  Min:     {sorted[0]:F2} ms");
        report.AppendLine($"  Max:     {sorted[sorted.Count - 1]:F2} ms");
        if (e2eTimes is { Count: > 0 })
        {
            var sortedE2e = new List<float>(e2eTimes);
            sortedE2e.Sort();
            report.AppendLine($"  Median component-to-atlas-ready: {BenchmarkStatistics.MedianSorted(sortedE2e):F2} ms");
        }
        report.AppendLine($"  Unique glyphs: {typicalGlyphs}");
        report.AppendLine($"  Managed alloc: {TextBenchmarkBase.FormatBytes(managedAlloc)} (total across {iterations} runs)");
        if (typicalGlyphs > 0)
            report.AppendLine($"  Per-glyph (median): {(median * 1000.0) / typicalGlyphs:F1} us");
        report.AppendLine("═══════════════════════════════════════════════");
    }
}
