using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LightSide;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using LightSide.Benchmark;

/// <summary>
/// Honest head-to-head for dynamic texture-atlas upload: the LightSide GPU path
/// (<see cref="GpuTileAtlas"/> over the GpuAtlas engine + <see cref="GpuUpload"/> — async regional
/// uploads into a Texture2DArray through a pooled staging ring) versus the two standard Unity paths:
///   • <b>Array + Apply</b>: write tiles into a single <see cref="Texture2DArray"/> and call Apply(),
///     which re-uploads the WHOLE array to the GPU every time (Unity has no dirty-slice upload);
///   • <b>Array + CopyTexture</b>: upload a batched source once, then Graphics.CopyTexture each tile
///     regionally into its slice (the fast, regional Unity path).
///
/// All three share ONE storage shape — a 2048²×N array — so the comparison is like-for-like, and the
/// heavy scenario (huge array, a few tiles changed per frame) is exactly where a regional path should
/// beat the whole-array re-upload.
///
/// Metrics per contender × scenario:
///   • CPU ms  — main-thread frame-blocking cost (Stopwatch around the submit);
///   • GPU ms  — FrameTimingManager.gpuFrameTime (whole-frame GPU work; the ONLY metric that sees the
///     deferred whole-array Apply re-upload — read it RELATIVE, it includes baseline rendering);
///   • e2e ms  — dispatch → GPU-visible, via an async-readback probe of a texel INSIDE a just-written
///     tile (true data dependency, so async/regional paths cannot report false-fast);
///   • GC/step — managed alloc on the calling thread (native staging / CPU mirrors excluded);
///   • Verify  — readback of sampled tile centres vs expected colour; FAIL cannot 'win'.
///
/// Scenarios: Bulk (all N at once), Incremental (k new tiles/step into a growing array), Sustained
/// (huge pre-filled array, k tiles changed EVERY frame for many frames — the real text/streaming case).
///
/// Play mode only (GPU + native upload plugin). Drop on a GameObject, enter Play, use the context menu.
/// </summary>
public sealed class TextureUploadBenchmark : MonoBehaviour
{
    [Header("Workload")]
    [Tooltip("Tile side in pixels. Power of two in [32,512] that divides the 2048 page.")]
    public int tileSize = 64;

    [Header("Bulk scenario")]
    [Tooltip("Tiles uploaded all at once.")]
    public int bulkTileCount = 2048;

    [Header("Incremental scenario")]
    public int incrementalPreFill = 1024;
    public int incrementalStepTiles = 8;
    public int incrementalSteps = 24;

    [Header("Sustained scenario (huge array + small frequent change)")]
    [Tooltip("Array depth (2048² slices). Total VRAM ≈ layers × 16 MB. 8 = 128 MB, 32 = 512 MB. This is the 'huge texture' Apply must re-upload whole every frame.")]
    public int sustainedLayers = 8;
    [Tooltip("Tiles changed each measured frame (the 'small frequent change').")]
    public int sustainedChangesPerFrame = 4;
    [Tooltip("Measured frames of sustained change.")]
    public int sustainedFrames = 120;

    [Header("Runs")]
    [Min(1)] public int iterations = 5;
    [Min(0)] public int warmupIterations = 1;
    [Tooltip("Measure end-to-end GPU-visible latency via async readback. Off = faster runs.")]
    public bool measureEndToEnd = true;
    [Tooltip("Measure GPU frame time (FrameTimingManager). The only metric that sees the deferred whole-array Apply upload.")]
    public bool measureGpuTime = true;
    [Tooltip("Collect internal GpuAtlas pipeline counters (backpressure stall, ExecuteCommandBuffer, native submit, staging copy) — reported once at the end, no per-frame logging.")]
    public bool collectDiagnostics = true;

    [Header("Contenders")]
    public bool runGpuAtlas = true;
    public bool runArrayApply = true;
    public bool runArrayCopyTexture = true;

    [Header("Result")]
    [SerializeField, TextArea(24, 60)] string lastResult = "";
    [SerializeField] bool isRunning;

    static readonly int PageSize = GpuAtlas<GpuTilePlacement>.DefaultPageSize;
    const double ReadyTimeoutSeconds = 12.0;
    const double ProbeTimeoutSeconds = 15.0;
    const int VerifySamples = 24;
    const long PrimeKey = long.MinValue;

    enum Scenario { Bulk, Incremental, Sustained }

    [ContextMenu("Run All")]
    public void RunAll() { if (Guard()) StartCoroutine(RunCoroutine(new[] { Scenario.Bulk, Scenario.Incremental, Scenario.Sustained })); }

    [ContextMenu("Run Bulk only")]
    public void RunBulk() { if (Guard()) StartCoroutine(RunCoroutine(new[] { Scenario.Bulk })); }

    [ContextMenu("Run Incremental only")]
    public void RunIncremental() { if (Guard()) StartCoroutine(RunCoroutine(new[] { Scenario.Incremental })); }

    [ContextMenu("Run Sustained only (huge array + frequent change)")]
    public void RunSustained() { if (Guard()) StartCoroutine(RunCoroutine(new[] { Scenario.Sustained })); }

    bool Guard()
    {
        if (isRunning) { Debug.LogWarning("[TextureUploadBenchmark] Already running."); return false; }
        if (!Application.isPlaying)
        {
            Debug.LogError("[TextureUploadBenchmark] Enter Play mode first (needs the GPU + native upload plugin).");
            return false;
        }
        if (tileSize < 32 || tileSize > 512 || (PageSize % tileSize) != 0 || (tileSize & (tileSize - 1)) != 0)
        {
            Debug.LogError($"[TextureUploadBenchmark] tileSize={tileSize} must be a power of two in [32,512] dividing {PageSize}.");
            return false;
        }
        if (bulkTileCount < 1 || incrementalPreFill < 0 || incrementalStepTiles < 1 || incrementalSteps < 1
            || sustainedLayers < 1 || sustainedChangesPerFrame < 1 || sustainedFrames < 1)
        {
            Debug.LogError("[TextureUploadBenchmark] Workload counts must be positive.");
            return false;
        }
        return true;
    }

    IEnumerator RunCoroutine(Scenario[] scenarios)
    {
        isRunning = true;
        bool mutedBefore = CatZoneRegistry.MuteAll;
        CatZoneRegistry.MuteAll = true;
        var report = new StringBuilder();
        try
        {
            foreach (var s in scenarios) yield return RunScenario(s, report);
            AppendFooter(report);
            lastResult = report.ToString();
            Debug.Log(lastResult);
        }
        finally
        {
            GpuUploadDiagnostics.Enabled = false;
            CatZoneRegistry.MuteAll = mutedBefore;
            isRunning = false;
        }
    }

    /// <summary>Arms/disarms the engine hot-path probes for the measured window only (never warmup/pre-fill).</summary>
    void SetDiag(bool on) => GpuUploadDiagnostics.Enabled = collectDiagnostics && on;

    int TilesPerPage() { int r = PageSize / tileSize; return r * r; }

    int ScenarioTiles(Scenario scenario) => scenario switch
    {
        Scenario.Bulk => bulkTileCount,
        Scenario.Incremental => incrementalPreFill + incrementalSteps * incrementalStepTiles,
        _ => sustainedLayers * TilesPerPage(),
    };

    IEnumerator RunScenario(Scenario scenario, StringBuilder report)
    {
        int scenarioTiles = ScenarioTiles(scenario);
        var results = new List<Result>();
        foreach (var factory in EnabledContenders())
        {
            var contender = factory();
            var result = new Result { name = contender.Name };
            try
            {
                string prepError = null;
                yield return contender.Prepare(tileSize, scenarioTiles, e => prepError = e);
                if (prepError != null)
                {
                    result.status = "unsupported";
                    result.note = prepError;
                    results.Add(result);
                    continue;
                }
                result.note = contender.StorageNote;

                if (scenario == Scenario.Bulk) yield return MeasureBulk(contender, result);
                else if (scenario == Scenario.Incremental) yield return MeasureIncremental(contender, result);
                else yield return MeasureSustained(contender, result);

                results.Add(result);
            }
            finally { contender.Cleanup(); }
            yield return null;
        }
        AppendScenario(report, scenario, results);
    }

    // ── scenario measurement ─────────────────────────────────────────────────────────────────

    IEnumerator MeasureBulk(IUploadContender contender, Result result)
    {
        var sw = new Stopwatch();
        GpuUploadDiagnostics.Reset();
        contender.ResetDiagnostics();
        for (int iter = -warmupIterations; iter < iterations; iter++)
        {
            SetDiag(false);
            yield return contender.ResetForIteration();
            if (contender.Failed) { result.status = "failed"; result.note = contender.FailReason; yield break; }
            yield return null;

            double dispatch = Time.realtimeSinceStartupAsDouble;
            SetDiag(iter >= 0);
            sw.Restart();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            bool ok = contender.Submit(0, bulkTileCount, out string submitError);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            sw.Stop();
            SetDiag(false);
            if (!ok) { result.status = "failed"; result.note = submitError; yield break; }

            float e2e = float.NaN;
            if (measureEndToEnd)
            {
                bool probeFailed = false;
                yield return AwaitProbe(contender, dispatch, ms => e2e = ms, () => probeFailed = true);
                if (probeFailed) { result.status = "failed"; result.note = "e2e probe failed"; yield break; }
            }
            yield return SampleGpu(result, iter);

            if (iter >= 0)
                result.Record((float)sw.Elapsed.TotalMilliseconds, e2e, alloc);
        }
        result.diagnostics = contender.DiagnosticsReport(0);
        yield return VerifyContender(contender, bulkTileCount, result);
    }

    IEnumerator MeasureIncremental(IUploadContender contender, Result result)
    {
        var sw = new Stopwatch();
        GpuUploadDiagnostics.Reset();
        contender.ResetDiagnostics();
        for (int iter = -warmupIterations; iter < iterations; iter++)
        {
            SetDiag(false);
            yield return contender.ResetForIteration();
            if (contender.Failed) { result.status = "failed"; result.note = contender.FailReason; yield break; }
            yield return null;

            if (incrementalPreFill > 0)
            {
                if (!contender.Submit(0, incrementalPreFill, out string preError))
                { result.status = "failed"; result.note = preError; yield break; }
                yield return DrainFrames(2);
            }

            SetDiag(iter >= 0);
            for (int step = 0; step < incrementalSteps; step++)
            {
                int start = incrementalPreFill + step * incrementalStepTiles;
                double dispatch = Time.realtimeSinceStartupAsDouble;
                sw.Restart();
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                bool ok = contender.Submit(start, incrementalStepTiles, out string submitError);
                long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                sw.Stop();
                if (!ok) { result.status = "failed"; result.note = submitError; yield break; }

                float e2e = float.NaN;
                if (measureEndToEnd)
                {
                    bool probeFailed = false;
                    yield return AwaitProbe(contender, dispatch, ms => e2e = ms, () => probeFailed = true);
                    if (probeFailed) { result.status = "failed"; result.note = "e2e probe failed"; yield break; }
                }
                else yield return null;
                yield return SampleGpu(result, iter);

                if (iter >= 0)
                {
                    float cpuMs = (float)sw.Elapsed.TotalMilliseconds;
                    result.Record(cpuMs, e2e, alloc);
                    if (step == 0) result.firstStepCpu.Add(cpuMs);
                    if (step == incrementalSteps - 1) result.lastStepCpu.Add(cpuMs);
                }
            }
            SetDiag(false);
        }
        result.diagnostics = contender.DiagnosticsReport(0);
        yield return VerifyContender(contender, ScenarioTiles(Scenario.Incremental), result);
    }

    IEnumerator MeasureSustained(IUploadContender contender, Result result)
    {
        int total = ScenarioTiles(Scenario.Sustained);
        int k = Mathf.Min(sustainedChangesPerFrame, total);
        var sw = new Stopwatch();
        GpuUploadDiagnostics.Reset();
        contender.ResetDiagnostics();
        for (int iter = -warmupIterations; iter < iterations; iter++)
        {
            SetDiag(false);
            yield return contender.ResetForIteration();
            if (contender.Failed) { result.status = "failed"; result.note = contender.FailReason; yield break; }
            yield return null;

            if (!contender.Submit(0, total, out string fillError))
            { result.status = "failed"; result.note = fillError; yield break; }
            yield return DrainFrames(3);

            SetDiag(iter >= 0);
            int cursor = 0;
            for (int frame = 0; frame < sustainedFrames; frame++)
            {
                int start = cursor;
                if (start + k > total) start = 0;
                cursor = start + k;

                double dispatch = Time.realtimeSinceStartupAsDouble;
                sw.Restart();
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                bool ok = contender.Submit(start, k, out string submitError);
                long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
                sw.Stop();
                if (!ok) { result.status = "failed"; result.note = submitError; yield break; }

                float e2e = float.NaN;
                if (measureEndToEnd)
                {
                    bool probeFailed = false;
                    yield return AwaitProbe(contender, dispatch, ms => e2e = ms, () => probeFailed = true);
                    if (probeFailed) { result.status = "failed"; result.note = "e2e probe failed"; yield break; }
                }
                else yield return null;
                yield return SampleGpu(result, iter);

                if (iter >= 0)
                    result.Record((float)sw.Elapsed.TotalMilliseconds, e2e, alloc);
            }
            SetDiag(false);
        }
        result.diagnostics = contender.DiagnosticsReport(0);
        yield return VerifyContender(contender, total, result);
    }

    static IEnumerator DrainFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    // ── GPU frame time (FrameTimingManager) ──────────────────────────────────────────────────

    readonly FrameTiming[] gpuTimingBuf = new FrameTiming[1];

    IEnumerator SampleGpu(Result result, int iter)
    {
        if (!measureGpuTime) yield break;
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, gpuTimingBuf) >= 1)
        {
            double g = gpuTimingBuf[0].gpuFrameTime;
            if (iter >= 0 && g > 0.0) result.gpu.Add((float)g);
        }
    }

    // ── async readback: e2e probe + verification ─────────────────────────────────────────────

    IEnumerator AwaitProbe(IUploadContender contender, double dispatchStart,
        Action<float> onMeasured, Action onFailed)
    {
        if (!SystemInfo.supportsAsyncGPUReadback) { onMeasured(float.NaN); yield break; }

        AsyncGPUReadbackRequest request;
        if (contender.LocateTile(contender.LastWrittenTile, out var tex, out int layer, out int px, out int py) && tex != null)
            request = AsyncGPUReadback.Request(tex, 0, px, 1, py, 1, layer, 1);
        else
        {
            var probes = contender.ProbeTextures;
            if (probes == null || probes.Count == 0 || probes[0] == null) { onFailed(); yield break; }
            request = AsyncGPUReadback.Request(probes[0], 0, 0, 1, 0, 1, 0, 1);
        }

        double timeoutAt = Time.realtimeSinceStartupAsDouble + ProbeTimeoutSeconds;
        while (!request.done)
        {
            if (Time.realtimeSinceStartupAsDouble >= timeoutAt) { onFailed(); yield break; }
            yield return null;
        }
        if (request.hasError) onFailed();
        else onMeasured((float)((Time.realtimeSinceStartupAsDouble - dispatchStart) * 1000.0));
    }

    IEnumerator VerifyContender(IUploadContender contender, int uploadedTiles, Result result)
    {
        if (!SystemInfo.supportsAsyncGPUReadback) { result.verify = "n/a (no readback)"; yield break; }
        int samples = Mathf.Min(VerifySamples, uploadedTiles);
        if (samples < 1) { result.verify = "n/a"; yield break; }

        var requests = new List<AsyncGPUReadbackRequest>(samples);
        var expected = new List<Color32>(samples);
        var located = new List<int>(samples);
        for (int i = 0; i < samples; i++)
        {
            int tileIndex = samples == 1 ? 0 : (int)((long)i * (uploadedTiles - 1) / (samples - 1));
            if (!contender.LocateTile(tileIndex, out var tex, out int layer, out int px, out int py) || tex == null) continue;
            requests.Add(AsyncGPUReadback.Request(tex, 0, px, 1, py, 1, layer, 1));
            expected.Add(ExpectedColor(tileIndex));
            located.Add(tileIndex);
        }
        if (requests.Count == 0) { result.verify = "n/a (no locator)"; yield break; }

        double timeoutAt = Time.realtimeSinceStartupAsDouble + ProbeTimeoutSeconds;
        while (true)
        {
            bool allDone = true;
            for (int i = 0; i < requests.Count; i++) if (!requests[i].done) { allDone = false; break; }
            if (allDone) break;
            if (Time.realtimeSinceStartupAsDouble >= timeoutAt) { result.verify = "TIMEOUT"; yield break; }
            yield return null;
        }

        int mismatches = 0;
        string firstBad = null;
        for (int i = 0; i < requests.Count; i++)
        {
            if (requests[i].hasError) { mismatches++; firstBad ??= $"tile {located[i]}: readback error"; continue; }
            var got = requests[i].GetData<Color32>()[0];
            var want = expected[i];
            if (got.r != want.r || got.g != want.g || got.b != want.b || got.a != want.a)
            {
                mismatches++;
                firstBad ??= $"tile {located[i]}: got ({got.r},{got.g},{got.b},{got.a}) want ({want.r},{want.g},{want.b},{want.a})";
            }
        }
        result.verify = mismatches == 0
            ? $"OK ({requests.Count}/{requests.Count})"
            : $"FAIL {mismatches}/{requests.Count} — {firstBad}";
        if (mismatches != 0 && result.status == "measured") result.status = "verify-failed";
    }

    IEnumerable<Func<IUploadContender>> EnabledContenders()
    {
        if (runGpuAtlas) yield return () => new GpuAtlasContender();
        if (runArrayApply) yield return () => new ArrayApplyContender();
        if (runArrayCopyTexture) yield return () => new ArrayCopyTextureContender();
    }

    static Color32 ExpectedColor(int tileIndex) => new Color32(
        (byte)(17 + tileIndex * 37),
        (byte)(29 + tileIndex * 53),
        (byte)(43 + tileIndex * 97),
        255);

    static void FillBytes(byte[] buffer, int lengthBytes, Color32 c)
    {
        for (int p = 0; p < lengthBytes; p += 4)
        { buffer[p] = c.r; buffer[p + 1] = c.g; buffer[p + 2] = c.b; buffer[p + 3] = c.a; }
    }

    // ── report ───────────────────────────────────────────────────────────────────────────────

    sealed class Result
    {
        public string name;
        public string status = "measured";
        public string note = "";
        public string verify = "—";
        public string diagnostics;
        public readonly List<float> cpu = new();
        public readonly List<float> gpu = new();
        public readonly List<float> e2e = new();
        public readonly List<long> alloc = new();
        public readonly List<float> firstStepCpu = new();
        public readonly List<float> lastStepCpu = new();

        public void Record(float cpuMs, float endToEndMs, long allocatedBytes)
        {
            cpu.Add(cpuMs);
            if (!float.IsNaN(endToEndMs)) e2e.Add(endToEndMs);
            alloc.Add(allocatedBytes);
        }
    }

    static (float median, float min, float max) Stat(List<float> values)
    {
        if (values.Count == 0) return (float.NaN, float.NaN, float.NaN);
        var sorted = new List<float>(values);
        sorted.Sort();
        return (BenchmarkStatistics.MedianSorted(sorted), sorted[0], sorted[sorted.Count - 1]);
    }

    static long MedianAlloc(List<long> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<long>(values);
        sorted.Sort();
        int mid = sorted.Count / 2;
        return (sorted.Count & 1) != 0 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    void AppendScenario(StringBuilder sb, Scenario scenario, List<Result> results)
    {
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine(scenario switch
        {
            Scenario.Bulk => $"  BULK — {bulkTileCount} tiles × {tileSize}px, one shot   (iters={iterations} warmup={warmupIterations})",
            Scenario.Incremental => $"  INCREMENTAL — preFill {incrementalPreFill}, +{incrementalStepTiles}/step × {incrementalSteps} steps, {tileSize}px",
            _ => $"  SUSTAINED — {sustainedLayers}-layer array ({sustainedLayers * 16}MB), {sustainedChangesPerFrame} tiles changed/frame × {sustainedFrames} frames, {tileSize}px",
        });
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

        float baseCpu = float.NaN;
        foreach (var r in results)
            if (r.name == GpuAtlasContender.Label && r.cpu.Count > 0) baseCpu = Stat(r.cpu).median;

        sb.AppendLine($"  {"Contender",-26}{"CPU ms",10}{"vs GPU",8}{"GPU ms",9}{"e2e ms",9}{"GC/step",11}  Verify");
        sb.AppendLine("  ───────────────────────────────────────────────────────────────────────────");
        foreach (var r in results)
        {
            if (r.cpu.Count == 0)
            {
                sb.AppendLine($"  {r.name,-26}  {r.status.ToUpperInvariant()} — {r.note}");
                continue;
            }
            var cpu = Stat(r.cpu);
            var gpu = Stat(r.gpu);
            var e2e = Stat(r.e2e);
            string ratio = float.IsNaN(baseCpu) || baseCpu <= 0 ? "—" : $"{cpu.median / baseCpu:0.00}×";
            string gpuStr = float.IsNaN(gpu.median) ? "n/a" : $"{gpu.median:0.00}";
            string e2eStr = float.IsNaN(e2e.median) ? "—" : $"{e2e.median:0.00}";
            sb.AppendLine($"  {r.name,-26}{cpu.median,9:0.000}{ratio,8}{gpuStr,9}{e2eStr,9}{TextBenchmarkBase.FormatBytes(MedianAlloc(r.alloc)),11}  {r.verify}");
        }

        sb.AppendLine();
        int perSample = scenario switch
        {
            Scenario.Bulk => bulkTileCount,
            Scenario.Incremental => incrementalStepTiles,
            _ => Mathf.Min(sustainedChangesPerFrame, ScenarioTiles(Scenario.Sustained)),
        };
        foreach (var r in results)
        {
            if (r.cpu.Count == 0) continue;
            var cpu = Stat(r.cpu);
            sb.Append($"  {r.name}: cpu min/med/max = {cpu.min:0.000}/{cpu.median:0.000}/{cpu.max:0.000} ms" +
                      $"  ({cpu.median * 1000.0 / Mathf.Max(1, perSample):0.0} µs/tile)");
            if (scenario == Scenario.Incremental && r.firstStepCpu.Count > 0 && r.lastStepCpu.Count > 0)
            {
                float first = Stat(r.firstStepCpu).median;
                float last = Stat(r.lastStepCpu).median;
                sb.Append($"   | step1={first:0.000} → step{incrementalSteps}={last:0.000} ms (growth {(first > 0 ? $"{last / first:0.00}×" : "—")})");
            }
            if (!string.IsNullOrEmpty(r.note)) sb.Append($"   [{r.note}]");
            sb.AppendLine();
        }
        foreach (var r in results)
            if (!string.IsNullOrEmpty(r.diagnostics))
            {
                sb.AppendLine();
                sb.Append(r.diagnostics);
            }
        sb.AppendLine();
    }

    void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("  Methodology (honest comparison):");
        sb.AppendLine("   • All contenders share ONE storage shape: a 2048²×N RGBA32 array. Same per-tile fill.");
        sb.AppendLine("   • CPU ms  = main-thread cost (Stopwatch around submit). Apply defers the actual GPU");
        sb.AppendLine("     upload to the render thread, so its whole-array cost lands in GPU ms, not here.");
        sb.AppendLine("   • GPU ms  = FrameTimingManager.gpuFrameTime (WHOLE frame incl. baseline render, ~3-4");
        sb.AppendLine("     frame delay). Read RELATIVE between contenders; n/a if the platform reports no data.");
        sb.AppendLine("   • e2e ms  = probes a texel INSIDE a just-written tile (true dependency, no false-fast).");
        sb.AppendLine("   • GC/step = managed alloc on the calling thread (native staging / CPU mirror excluded).");
        sb.AppendLine("   • Array+Apply re-uploads the ENTIRE array on every change (Unity has no dirty-slice");
        sb.AppendLine("     upload); Array+CopyTexture is regional but needs a batched source upload first.");
        sb.AppendLine("   • Unmeasured for GpuAtlas: one-time reservation/native-pool install + first-page");
        sb.AppendLine("     materialization happen in the prime loop; timings are steady-state, not first-use.");
        sb.AppendLine($"   • copyTextureSupport={SystemInfo.copyTextureSupport}, asyncReadback={SystemInfo.supportsAsyncGPUReadback}, 2DArray={SystemInfo.supports2DArrayTextures}, gpuUpload={GpuUpload.IsSupported}");
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
    }

    // ── contenders ───────────────────────────────────────────────────────────────────────────

    interface IUploadContender
    {
        string Name { get; }
        string StorageNote { get; }
        bool Failed { get; }
        string FailReason { get; }
        int LastWrittenTile { get; }
        IReadOnlyList<Texture> ProbeTextures { get; }
        IEnumerator Prepare(int tileSize, int scenarioTiles, Action<string> onUnsupported);
        IEnumerator ResetForIteration();
        bool Submit(int startTile, int count, out string error);
        bool LocateTile(int tileIndex, out Texture tex, out int layer, out int px, out int py);
        void ResetDiagnostics();
        string DiagnosticsReport(int frames);
        void Cleanup();
    }

    /// <summary>Shared array-grid math for the Unity contenders: one 2048²×N Texture2DArray, tiles packed
    /// row-major inside each slice, slice index = tileIndex / tilesPerPage — the same shape our GpuAtlas
    /// grows natively, so the only variable is the upload API.</summary>
    abstract class ArrayContender : IUploadContender
    {
        protected int tileSize, tilesPerRow, tilesPerPage, layers;
        protected Texture2DArray storage;
        protected int lastWritten;
        protected readonly List<Texture> probes = new();

        public abstract string Name { get; }
        public abstract string StorageNote { get; }
        public virtual bool Failed => false;
        public virtual string FailReason => null;
        public int LastWrittenTile => lastWritten;
        public IReadOnlyList<Texture> ProbeTextures => probes;

        public virtual IEnumerator Prepare(int ts, int scenarioTiles, Action<string> onUnsupported)
        {
            tileSize = ts;
            tilesPerRow = PageSize / tileSize;
            tilesPerPage = tilesPerRow * tilesPerRow;
            layers = Mathf.Max(1, (scenarioTiles + tilesPerPage - 1) / tilesPerPage);
            if (!SystemInfo.supports2DArrayTextures) onUnsupported("Texture2DArray is unsupported on this device.");
            if (layers > SystemInfo.maxTextureArraySlices)
                onUnsupported($"needs {layers} array slices, device max is {SystemInfo.maxTextureArraySlices}.");
            yield break;
        }

        protected void Locate(int tileIndex, out int slice, out int px, out int py)
        {
            slice = tileIndex / tilesPerPage;
            int local = tileIndex % tilesPerPage;
            px = (local % tilesPerRow) * tileSize;
            py = (local / tilesPerRow) * tileSize;
        }

        public bool LocateTile(int tileIndex, out Texture tex, out int layer, out int px, out int py)
        {
            Locate(tileIndex, out layer, out px, out py);
            px += tileSize / 2;
            py += tileSize / 2;
            tex = storage;
            return tex != null;
        }

        public void ResetDiagnostics() { }
        public string DiagnosticsReport(int frames) => null;

        public abstract IEnumerator ResetForIteration();
        public abstract bool Submit(int startTile, int count, out string error);
        public virtual void Cleanup()
        {
            probes.Clear();
            if (storage != null) { Object.Destroy(storage); storage = null; }
        }
    }

    /// <summary>Standard Unity path #1: write changed tiles into a per-slice CPU mirror, then
    /// Texture2DArray.Apply() — which re-uploads the WHOLE array to the GPU every call, changed or not.
    /// This is the cost a regional atlas is built to avoid; its bite shows in GPU ms, not CPU ms.</summary>
    sealed class ArrayApplyContender : ArrayContender
    {
        public const string Label = "Unity Array+Apply";
        NativeArray<Color32>[] mirror;
        bool[] dirty;

        public override string Name => Label;
        public override string StorageNote => $"Texture2DArray {PageSize}²×{layers} ({layers * 16}MB, whole-array Apply)";

        public override IEnumerator ResetForIteration()
        {
            Cleanup();
            storage = new Texture2DArray(PageSize, PageSize, layers, TextureFormat.RGBA32, false, false)
            { name = "ArrayApply", filterMode = FilterMode.Point };
            probes.Add(storage);
            mirror = new NativeArray<Color32>[layers];
            for (int i = 0; i < layers; i++)
                mirror[i] = new NativeArray<Color32>(PageSize * PageSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            dirty = new bool[layers];
            yield break;
        }

        public override bool Submit(int startTile, int count, out string error)
        {
            Array.Clear(dirty, 0, dirty.Length);
            for (int i = 0; i < count; i++)
            {
                int tileIndex = startTile + i;
                var c = ExpectedColor(tileIndex);
                Locate(tileIndex, out int slice, out int px, out int py);
                var m = mirror[slice];
                for (int y = 0; y < tileSize; y++)
                {
                    int row = (py + y) * PageSize + px;
                    for (int x = 0; x < tileSize; x++) m[row + x] = c;
                }
                dirty[slice] = true;
            }
            for (int s = 0; s < layers; s++)
                if (dirty[s]) storage.SetPixelData(mirror[s], 0, s);
            storage.Apply(false, false);
            lastWritten = startTile + count - 1;
            error = null;
            return true;
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (mirror == null) return;
            foreach (var m in mirror) if (m.IsCreated) m.Dispose();
            mirror = null;
        }
    }

    /// <summary>Standard Unity path #2: batch the changed tiles into one source strip, upload it ONCE
    /// (SetPixelData + Apply), then Graphics.CopyTexture each tile regionally into its array slice — the
    /// fast regional Unity path, and the real competitor to our regional upload.</summary>
    sealed class ArrayCopyTextureContender : ArrayContender
    {
        public const string Label = "Unity Array+CopyTexture";
        Texture2D source;
        byte[] sourceBytes;
        int stage;

        public override string Name => Label;
        public override string StorageNote => $"Texture2DArray {PageSize}²×{layers} (regional GPU copy, staged×{stage})";

        public override IEnumerator Prepare(int ts, int scenarioTiles, Action<string> onUnsupported)
        {
            yield return base.Prepare(ts, scenarioTiles, onUnsupported);
            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
                onUnsupported("Graphics.CopyTexture is unsupported on this device.");
        }

        public override IEnumerator ResetForIteration()
        {
            Cleanup();
            stage = Mathf.Max(1, Mathf.Min(64, 8192 / tileSize));
            source = new Texture2D(tileSize, stage * tileSize, TextureFormat.RGBA32, false, false) { name = "CopyTexture_src" };
            sourceBytes = new byte[tileSize * stage * tileSize * 4];
            storage = new Texture2DArray(PageSize, PageSize, layers, TextureFormat.RGBA32, false, false)
            { name = "ArrayCopyTexture", filterMode = FilterMode.Point };
            storage.Apply(false, true);
            probes.Add(storage);
            yield break;
        }

        public override bool Submit(int startTile, int count, out string error)
        {
            int tileBytes = tileSize * tileSize * 4;
            for (int chunkStart = 0; chunkStart < count; chunkStart += stage)
            {
                int chunk = Mathf.Min(stage, count - chunkStart);
                for (int j = 0; j < chunk; j++)
                {
                    int tileIndex = startTile + chunkStart + j;
                    var c = ExpectedColor(tileIndex);
                    int off = j * tileBytes;
                    for (int p = 0; p < tileBytes; p += 4)
                    { sourceBytes[off + p] = c.r; sourceBytes[off + p + 1] = c.g; sourceBytes[off + p + 2] = c.b; sourceBytes[off + p + 3] = c.a; }
                }
                source.SetPixelData(sourceBytes, 0);
                source.Apply(false, false);
                for (int j = 0; j < chunk; j++)
                {
                    int tileIndex = startTile + chunkStart + j;
                    Locate(tileIndex, out int slice, out int px, out int py);
                    Graphics.CopyTexture(source, 0, 0, 0, j * tileSize, tileSize, tileSize, storage, slice, 0, px, py);
                }
            }
            lastWritten = startTile + count - 1;
            error = null;
            return true;
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (source != null) { Object.Destroy(source); source = null; }
        }
    }

    /// <summary>The LightSide path: <see cref="GpuTileAtlas"/> over the GpuAtlas engine + GpuUpload —
    /// async regional uploads into a Texture2DArray through a pooled staging ring, no whole-array
    /// re-upload. Rent takes ownership of a pooled buffer; FlushPending stages+submits within the frame;
    /// completion is pumped by the player loop and observed by the shared readback probe.</summary>
    sealed class GpuAtlasContender : IUploadContender
    {
        public const string Label = "LightSide GpuAtlas";
        GpuAtlasConfig config;
        GpuTileAtlas atlas;
        int tileSize;
        int lastWritten;
        bool failed;
        string failReason;
        readonly Dictionary<int, GpuTilePlacement> placements = new();
        readonly List<Texture> probes = new();
        long beginTicks, rentTicks, flushTicks, commitTicks;
        int phaseFrames;

        public string Name => Label;
        public string StorageNote => atlas != null ? $"Texture2DArray {PageSize}²×{atlas.PageCount} (async regional)" : "";
        public bool Failed => failed;
        public string FailReason => failReason;
        public int LastWrittenTile => lastWritten;

        public IReadOnlyList<Texture> ProbeTextures
        {
            get
            {
                probes.Clear();
                if (atlas != null && atlas.AtlasTexture != null) probes.Add(atlas.AtlasTexture);
                return probes;
            }
        }

        public IEnumerator Prepare(int ts, int scenarioTiles, Action<string> onUnsupported)
        {
            tileSize = ts;
            config = new GpuAtlasConfig
            {
                Format = TextureFormat.RGBA32,
                Linear = false,
                Filter = FilterMode.Point,
                Mips = GpuAtlasMips.None,
                PixelTiles = true,
                TileSizes = new[] { tileSize },
                TileGutter = 0,
                Label = "TexUploadBench"
            };
            yield return ResetForIteration();
            if (failed) onUnsupported(failReason ?? "GpuUpload delivery unavailable (native plugin required).");
        }

        public IEnumerator ResetForIteration()
        {
            failed = false;
            failReason = null;
            atlas?.Dispose();
            atlas = new GpuTileAtlas(config);
            placements.Clear();

            int lengthBytes = tileSize * tileSize * 4;
            var buf = ArrayPool<byte>.Rent(lengthBytes);
            FillBytes(buf, lengthBytes, ExpectedColor(-1));

            double timeoutAt = Time.realtimeSinceStartupAsDouble + ReadyTimeoutSeconds;
            bool primed = false;
            while (true)
            {
                atlas.BeginFrame();
                if (!primed) { atlas.Rent(PrimeKey, buf, tileSize, tileSize); primed = true; }
                bool ok = atlas.FlushPending();
                atlas.CommitPresentationAfterPublication();
                if (ok && atlas.AtlasTexture != null) break;
                if (Time.realtimeSinceStartupAsDouble >= timeoutAt)
                {
                    failed = true;
                    failReason = $"GpuUpload reservation not ready after {ReadyTimeoutSeconds:F0}s (lastError={atlas.GetStats().LastUploadError?.ToString() ?? "none"})";
                    atlas.Dispose();
                    atlas = null;
                    yield break;
                }
                yield return null;
            }
            for (int i = 0; i < 2; i++) { atlas.BeginFrame(); yield return null; }
        }

        public bool Submit(int startTile, int count, out string error)
        {
            bool diag = GpuUploadDiagnostics.Enabled;
            long t0 = diag ? Stopwatch.GetTimestamp() : 0;
            atlas.BeginFrame();
            long t1 = diag ? Stopwatch.GetTimestamp() : 0;

            int lengthBytes = tileSize * tileSize * 4;
            for (int i = 0; i < count; i++)
            {
                int tileIndex = startTile + i;
                var buf = ArrayPool<byte>.Rent(lengthBytes);
                FillBytes(buf, lengthBytes, ExpectedColor(tileIndex));
                placements[tileIndex] = atlas.Rent(tileIndex, buf, tileSize, tileSize);
            }
            long t2 = diag ? Stopwatch.GetTimestamp() : 0;
            bool ok = atlas.FlushPending();
            long t3 = diag ? Stopwatch.GetTimestamp() : 0;
            atlas.CommitPresentationAfterPublication();

            if (diag)
            {
                long t4 = Stopwatch.GetTimestamp();
                beginTicks += t1 - t0;
                rentTicks += t2 - t1;
                flushTicks += t3 - t2;
                commitTicks += t4 - t3;
                phaseFrames++;
            }

            lastWritten = startTile + count - 1;
            if (!ok)
            {
                error = $"FlushPending returned false (lastError={atlas.GetStats().LastUploadError?.ToString() ?? "none"})";
                return false;
            }
            error = null;
            return true;
        }

        public void ResetDiagnostics()
        {
            beginTicks = rentTicks = flushTicks = commitTicks = 0;
            phaseFrames = 0;
        }

        public string DiagnosticsReport(int frames)
        {
            if (phaseFrames == 0) return null;
            double f = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder();
            sb.AppendLine($"  ── GpuAtlas internals ({phaseFrames} measured flushes) ─────────────────────");
            sb.AppendLine("  Submit phases (main-thread, total / µs-per-flush):");
            sb.AppendLine($"    BeginFrame   {beginTicks * f,8:0.000} ms   {beginTicks * f * 1000.0 / phaseFrames,7:0.0} µs");
            sb.AppendLine($"    Rent loop    {rentTicks * f,8:0.000} ms   {rentTicks * f * 1000.0 / phaseFrames,7:0.0} µs");
            sb.AppendLine($"    FlushPending {flushTicks * f,8:0.000} ms   {flushTicks * f * 1000.0 / phaseFrames,7:0.0} µs   ← transport");
            sb.AppendLine($"    Commit       {commitTicks * f,8:0.000} ms   {commitTicks * f * 1000.0 / phaseFrames,7:0.0} µs");
            sb.AppendLine("  Inside the pipeline (engine probes):");
            sb.Append(GpuUploadDiagnostics.Report(phaseFrames));
            long drains = GpuUploadDiagnostics.CountOf(GpuUploadDiagnostics.Probe.BackpressureDrain);
            long submits = GpuUploadDiagnostics.CountOf(GpuUploadDiagnostics.Probe.CreateSubmission);
            sb.AppendLine($"  Batches/flush = {(double)submits / phaseFrames:0.00} (1.00 = coalesced)   " +
                          $"BackpressureStalls = {drains}" + (drains > 0 ? "  ⚠ WaitForCompletion firing" : "  ✓ none"));
            return sb.ToString();
        }

        public bool LocateTile(int tileIndex, out Texture tex, out int layer, out int px, out int py)
        {
            tex = atlas != null ? atlas.AtlasTexture : null;
            layer = px = py = 0;
            if (tex == null || !placements.TryGetValue(tileIndex, out var placement)) return false;
            layer = placement.PageIndex;
            var uv = atlas.GetTileUv(placement);
            px = Mathf.Clamp((int)((uv.x + uv.width * 0.5f) * PageSize), 0, PageSize - 1);
            py = Mathf.Clamp((int)((uv.y + uv.height * 0.5f) * PageSize), 0, PageSize - 1);
            return true;
        }

        public void Cleanup()
        {
            atlas?.Dispose();
            atlas = null;
            placements.Clear();
            probes.Clear();
        }
    }
}
