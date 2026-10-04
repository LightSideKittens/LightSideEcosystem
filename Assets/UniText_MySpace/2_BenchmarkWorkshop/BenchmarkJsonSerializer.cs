using System;
using System.Collections.Generic;
using System.Text;
using LightSide.Benchmark;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class BenchmarkJsonSerializer
{
    internal static JObject SerializeTextBenchmarks(Dictionary<string, TextBenchmarkBase.TestResults> benchmarks)
    {
        var obj = new JObject();
        foreach (var kvp in benchmarks)
            obj[kvp.Key] = SerializeTestResults(kvp.Value);
        return obj;
    }

    static JObject SerializeTestResults(TextBenchmarkBase.TestResults r)
    {
        var obj = new JObject
        {
            ["creation"] = SerializeMetrics(r.creation),
            ["destruction"] = SerializeMetrics(r.destruction),
            ["fullRebuild"] = SerializeMetrics(r.fullRebuild),
            ["fullRebuildUnique"] = SerializeMetrics(r.fullRebuildUnique),
            ["fullRebuildRichText"] = SerializeMetrics(r.fullRebuildRichText),
            ["layoutWrapNoAuto"] = SerializeMetrics(r.layoutWrapNoAuto),
            ["layoutWrapAuto"] = SerializeMetrics(r.layoutWrapAuto),
            ["layoutNoWrapNoAuto"] = SerializeMetrics(r.layoutNoWrapNoAuto),
            ["layoutNoWrapAuto"] = SerializeMetrics(r.layoutNoWrapAuto),
            ["meshRebuild"] = SerializeMetrics(r.meshRebuild)
        };
        if (r.memory.available)
            obj["memory"] = SerializeRunMemory(r.memory);
        return obj;
    }

    static JObject SerializeMetrics(TextBenchmarkBase.TestMetrics m)
    {
        var times = m.frameTimes ?? new List<float>();
        var sorted = new List<float>(times);
        sorted.Sort();

        float median = BenchmarkStatistics.MedianSorted(sorted);
        float min = sorted.Count > 0 ? sorted[0] : 0;
        float max = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0;

        var obj = new JObject
        {
            ["totalMs"] = m.TotalTime,
            ["frameTimes"] = new JArray(times.ToArray()),
            ["median"] = median,
            ["min"] = min,
            ["max"] = max,
            ["managedAlloc"] = Number(m.managedAlloc)
        };
        if (m.memory.available)
            obj["memory"] = SerializePhaseMemory(m.memory);
        return obj;
    }

    internal static string SerializeMemoryProfile(string engine, string corpus, string phase, string runId,
        string checkpoint, TextBenchmarkBase.PhaseMemoryMetrics memory) => new JObject
    {
        ["version"] = "1.1",
        ["kind"] = "phase-memory",
        ["runId"] = runId,
        ["checkpoint"] = checkpoint,
        ["engine"] = engine,
        ["corpus"] = corpus,
        ["phase"] = phase,
        ["memory"] = SerializePhaseMemory(memory)
    }.ToString(Formatting.Indented);

    internal static string SerializeMemoryProfileText(string engine, string corpus, string phase, string runId,
        string checkpoint, TextBenchmarkBase.PhaseMemoryMetrics memory)
    {
        var sb = new StringBuilder(1024);
        sb.Append("memory: ").Append(engine).Append('/').Append(corpus).Append('/').Append(phase).AppendLine();
        sb.Append("run: ").Append(runId).Append(" | checkpoint: ").Append(checkpoint).AppendLine();
        AppendSnapshot(sb, "before phase setup", memory.beforeWarmup);
        AppendSnapshot(sb, "state-normalized baseline + GC", memory.normalizedBaseline);
        AppendSnapshot(sb, "after warmup + GC", memory.afterWarmup);
        AppendSnapshot(sb, "measured peak", memory.measuredPeak);
        AppendSnapshot(sb, "measured end before state normalization", memory.measuredEnd);
        AppendSnapshot(sb, "state-normalized end before GC", memory.normalizedEnd);
        AppendSnapshot(sb, "after measured + GC", memory.afterMeasured);
        AppendDelta(sb, "phase setup net delta", memory.beforeWarmup, memory.normalizedBaseline);
        AppendDelta(sb, "warmup post-GC delta", memory.normalizedBaseline, memory.afterWarmup);
        AppendDelta(sb, "measured post-GC delta", memory.afterWarmup, memory.afterMeasured);
        AppendDelta(sb, "total state-normalized post-GC delta", memory.normalizedBaseline, memory.afterMeasured);
        sb.Append("retained candidate (Unity used): ")
          .Append(FormatDelta(memory.afterMeasured.used, memory.normalizedBaseline.used, true)).AppendLine();
        sb.Append("GC garbage reclaimed at state-normalized checkpoint: ")
          .Append(FormatDelta(memory.normalizedEnd.gcUsed, memory.afterMeasured.gcUsed, true)).AppendLine();

        var previous = memory.beforeProbes;
        var probeCount = memory.probes?.Count ?? 0;
        for (int i = 0; i < probeCount; i++)
        {
            var cycle = memory.probes[i];
            sb.AppendLine();
            sb.Append("probe ").Append(i + 1).Append(" managed allocation traffic: ")
              .Append(FormatValue(cycle.managedAlloc)).AppendLine();
            AppendSnapshot(sb, "peak", cycle.peak);
            AppendSnapshot(sb, "end", cycle.end);
            AppendSnapshot(sb, "after GC", cycle.afterCollect);
            var before = cycle.before.used >= 0 || cycle.before.resident >= 0 ? cycle.before : previous;
            AppendDelta(sb, "repeat post-GC delta", before, cycle.afterCollect);
            sb.Append("leak candidate (Unity used): ")
              .Append(FormatDelta(cycle.afterCollect.used, before.used, true)).AppendLine();
            sb.Append("GC garbage reclaimed: ").Append(FormatDelta(cycle.end.gcUsed, cycle.afterCollect.gcUsed, true)).AppendLine();
            previous = cycle.afterCollect;
        }
        return sb.ToString();
    }

    internal static string SerializeRunMemoryProfile(string engine, string corpus, string runId,
        TextBenchmarkBase.RunMemoryMetrics memory) => new JObject
    {
        ["version"] = "1.1",
        ["kind"] = "run-memory",
        ["runId"] = runId,
        ["checkpoint"] = "run",
        ["engine"] = engine,
        ["corpus"] = corpus,
        ["memory"] = SerializeRunMemory(memory)
    }.ToString(Formatting.Indented);

    internal static string SerializeRunMemoryProfileText(string engine, string corpus, string runId,
        TextBenchmarkBase.RunMemoryMetrics memory)
    {
        var sb = new StringBuilder(512);
        sb.Append("run memory: ").Append(engine).Append('/').Append(corpus).AppendLine();
        sb.Append("run: ").Append(runId).Append(" | checkpoint: run").AppendLine();
        AppendSnapshot(sb, "baseline", memory.baseline);
        AppendSnapshot(sb, "sampled workload peak", memory.peak);
        AppendSnapshot(sb, "before cleanup", memory.beforeCleanup);
        AppendSnapshot(sb, "after cleanup + GC", memory.afterCleanup);
        AppendDelta(sb, "post-run app delta (includes retained benchmark results and optional profile replay)", memory.baseline, memory.afterCleanup);
        return sb.ToString();
    }

    static JObject SerializePhaseMemory(TextBenchmarkBase.PhaseMemoryMetrics memory)
    {
        var probes = new JArray();
        var derivedProbes = new JArray();
        var previous = memory.beforeProbes;
        if (memory.probes != null)
            foreach (var probe in memory.probes)
            {
                probes.Add(new JObject
                {
                    ["before"] = SerializeSnapshot(probe.before),
                    ["peak"] = SerializeSnapshot(probe.peak),
                    ["end"] = SerializeSnapshot(probe.end),
                    ["afterCollect"] = SerializeSnapshot(probe.afterCollect),
                    ["managedAlloc"] = Number(probe.managedAlloc)
                });
                var before = probe.before.used >= 0 || probe.before.resident >= 0 ? probe.before : previous;
                derivedProbes.Add(new JObject
                {
                    ["peakDelta"] = SerializeDelta(before, probe.peak),
                    ["postGcGrowth"] = SerializeDelta(before, probe.afterCollect),
                    ["leakCandidateUnityUsed"] = PositiveDelta(before.used, probe.afterCollect.used),
                    ["gcReclaimed"] = Reclaimed(probe.end.gcUsed, probe.afterCollect.gcUsed)
                });
                previous = probe.afterCollect;
            }

        return new JObject
        {
            ["beforeWarmup"] = SerializeSnapshot(memory.beforeWarmup),
            ["normalizedBaseline"] = SerializeSnapshot(memory.normalizedBaseline),
            ["afterWarmup"] = SerializeSnapshot(memory.afterWarmup),
            ["measuredPeak"] = SerializeSnapshot(memory.measuredPeak),
            ["measuredEnd"] = SerializeSnapshot(memory.measuredEnd),
            ["normalizedEnd"] = SerializeSnapshot(memory.normalizedEnd),
            ["afterMeasured"] = SerializeSnapshot(memory.afterMeasured),
            ["beforeProbes"] = SerializeSnapshot(memory.beforeProbes),
            ["probes"] = probes,
            ["derived"] = new JObject
            {
                ["phaseSetupNetDelta"] = SerializeDelta(memory.beforeWarmup, memory.normalizedBaseline),
                ["warmupPostGcDelta"] = SerializeDelta(memory.normalizedBaseline, memory.afterWarmup),
                ["measuredPostGcDelta"] = SerializeDelta(memory.afterWarmup, memory.afterMeasured),
                ["retainedCandidate"] = SerializeDelta(memory.normalizedBaseline, memory.afterMeasured),
                ["retainedCandidateUnityUsed"] = PositiveDelta(memory.normalizedBaseline.used, memory.afterMeasured.used),
                ["measuredPeakDelta"] = SerializeDelta(memory.afterWarmup, memory.measuredPeak),
                ["checkpointGcReclaimed"] = Reclaimed(memory.normalizedEnd.gcUsed, memory.afterMeasured.gcUsed),
                ["probes"] = derivedProbes
            }
        };
    }

    static JObject SerializeRunMemory(TextBenchmarkBase.RunMemoryMetrics memory) => new()
    {
        ["baseline"] = SerializeSnapshot(memory.baseline),
        ["peak"] = SerializeSnapshot(memory.peak),
        ["beforeCleanup"] = SerializeSnapshot(memory.beforeCleanup),
        ["afterCleanup"] = SerializeSnapshot(memory.afterCleanup),
        ["derived"] = new JObject
        {
            ["sampledPeakDelta"] = SerializeDelta(memory.baseline, memory.peak),
            ["retainedFootprint"] = SerializeDelta(memory.baseline, memory.beforeCleanup),
            ["postRunAppDelta"] = SerializeDelta(memory.baseline, memory.afterCleanup)
        }
    };

    static JObject SerializeSnapshot(TextBenchmarkBase.MemorySnapshot snapshot) => new()
    {
        ["resident"] = Number(snapshot.resident),
        ["unityUsed"] = Number(snapshot.used),
        ["unityReserved"] = Number(snapshot.reserved),
        ["gcUsed"] = Number(snapshot.gcUsed),
        ["gcReserved"] = Number(snapshot.gcReserved),
        ["buffers"] = Number(snapshot.buffers)
    };

    static JObject SerializeDelta(TextBenchmarkBase.MemorySnapshot from, TextBenchmarkBase.MemorySnapshot to) => new()
    {
        ["resident"] = Delta(from.resident, to.resident),
        ["unityUsed"] = Delta(from.used, to.used),
        ["unityReserved"] = Delta(from.reserved, to.reserved),
        ["gcUsed"] = Delta(from.gcUsed, to.gcUsed),
        ["gcReserved"] = Delta(from.gcReserved, to.gcReserved),
        ["buffers"] = Delta(from.buffers, to.buffers)
    };

    static JToken Number(long value) => value >= 0 ? new JValue(value) : JValue.CreateNull();

    static JToken Delta(long from, long to) => from >= 0 && to >= 0
        ? new JValue(to - from)
        : JValue.CreateNull();

    static JToken Reclaimed(long before, long after) => before >= 0 && after >= 0
        ? new JValue(Math.Max(0, before - after))
        : JValue.CreateNull();

    static JToken PositiveDelta(long from, long to) => from >= 0 && to >= 0
        ? new JValue(Math.Max(0, to - from))
        : JValue.CreateNull();

    static void AppendSnapshot(StringBuilder sb, string name, TextBenchmarkBase.MemorySnapshot value)
    {
        sb.Append(name).Append(": resident ").Append(FormatValue(value.resident))
          .Append(" | Unity used ").Append(FormatValue(value.used))
          .Append(" | reserved ").Append(FormatValue(value.reserved))
          .Append(" | GC ").Append(FormatValue(value.gcUsed)).Append('/').Append(FormatValue(value.gcReserved))
          .Append(" | buffers ").Append(FormatValue(value.buffers))
          .AppendLine();
    }

    static void AppendDelta(StringBuilder sb, string name, TextBenchmarkBase.MemorySnapshot from,
        TextBenchmarkBase.MemorySnapshot to)
    {
        sb.Append(name).Append(": resident ").Append(FormatDelta(to.resident, from.resident))
          .Append(" | Unity used ").Append(FormatDelta(to.used, from.used))
          .Append(" | reserved ").Append(FormatDelta(to.reserved, from.reserved))
          .Append(" | GC ").Append(FormatDelta(to.gcUsed, from.gcUsed))
          .Append(" | buffers ").Append(FormatDelta(to.buffers, from.buffers))
          .AppendLine();
    }

    static string FormatValue(long value) => value >= 0 ? TextBenchmarkBase.FormatBytes(value) : "n/a";

    static string FormatDelta(long to, long from, bool positiveOnly = false)
    {
        if (to < 0 || from < 0) return "n/a";
        var delta = to - from;
        if (positiveOnly) delta = Math.Max(0, delta);
        return TextBenchmarkBase.FormatBytes(delta);
    }

    internal static JObject SerializeGlyphRasterization(Dictionary<string, Dictionary<string, GlyphRasterData>> data)
    {
        var obj = new JObject();
        foreach (var engine in data)
        {
            var byFont = new JObject();
            foreach (var font in engine.Value)
                if (font.Value?.frameTimes != null)
                    byFont[font.Key] = SerializeGlyphRaster(font.Value);
            if (byFont.Count > 0)
                obj[engine.Key] = byFont;
        }
        return obj;
    }

    static JObject SerializeGlyphRaster(GlyphRasterData d)
    {
        var sorted = new List<float>(d.frameTimes);
        sorted.Sort();

        float median = BenchmarkStatistics.MedianSorted(sorted);
        float min = sorted.Count > 0 ? sorted[0] : 0;
        float max = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0;
        float sum = 0;
        for (int i = 0; i < sorted.Count; i++) sum += sorted[i];
        float avg = sorted.Count > 0 ? sum / sorted.Count : 0;
        double perGlyphUs = d.uniqueGlyphs > 0 ? (median * 1000.0) / d.uniqueGlyphs : 0;

        var obj = new JObject
        {
            ["status"] = string.IsNullOrEmpty(d.status) ? "measured" : d.status,
            ["frameTimes"] = new JArray(d.frameTimes.ToArray()),
            ["median"] = median,
            ["min"] = min,
            ["max"] = max,
            ["average"] = avg,
            ["uniqueGlyphs"] = d.uniqueGlyphs,
            ["perGlyphMedianUs"] = perGlyphUs,
            ["managedAlloc"] = Number(d.managedAlloc)
        };
        if (!string.IsNullOrEmpty(d.statusReason))
            obj["statusReason"] = d.statusReason;

        if (d.e2eTimes is { Count: > 0 })
        {
            var sortedE2e = new List<float>(d.e2eTimes);
            sortedE2e.Sort();
            float e2eMedian = BenchmarkStatistics.MedianSorted(sortedE2e);
            obj["e2eTimes"] = new JArray(d.e2eTimes.ToArray());
            obj["e2eMedian"] = e2eMedian;
            obj["perGlyphE2eMedianUs"] = d.uniqueGlyphs > 0 ? (e2eMedian * 1000.0) / d.uniqueGlyphs : 0;
        }

        if (d.benchmark != null)
            obj["benchmark"] = new JObject
            {
                ["mode"] = d.benchmark.mode,
                ["requestedPath"] = d.benchmark.requestedPath,
                ["iterations"] = d.benchmark.iterations,
                ["warmupIterations"] = d.benchmark.warmupIterations,
                ["previewFrames"] = d.benchmark.previewFrames,
                ["captureProfile"] = d.benchmark.captureProfile,
                ["captureAlloc"] = d.benchmark.captureAlloc
            };
        if (d.executionSamples is { Count: > 0 })
        {
            var samples = new JArray();
            foreach (var sample in d.executionSamples)
                samples.Add(SerializeExecutionSample(sample));
            obj["executionSamples"] = samples;
        }

        return obj;
    }

    static JObject SerializeExecutionSample(GlyphExecutionSample sample)
    {
        var atlases = new JArray();
        if (sample.atlases != null)
            foreach (var atlas in sample.atlases)
                atlases.Add(SerializeAtlasExecution(atlas));
        var obj = new JObject
        {
            ["trigger"] = sample.trigger,
            ["rasterBackend"] = sample.rasterBackend,
            ["threading"] = sample.threading,
            ["completion"] = sample.completion,
            ["fallback"] = sample.fallback,
            ["atlases"] = atlases
        };
        return obj;
    }

    static JObject SerializeAtlasExecution(GlyphAtlasExecutionData atlas)
    {
        var obj = new JObject
        {
            ["mode"] = atlas.mode,
            ["requestedPath"] = atlas.requestedPath,
            ["backend"] = atlas.backend,
            ["storage"] = atlas.storage,
            ["preparation"] = atlas.preparation,
            ["writePaths"] = new JArray((atlas.writePaths ?? new List<string>()).ToArray())
        };
        Add(obj, "cpuMirror", atlas.cpuMirror);
        Add(obj, "gpuUploadTarget", atlas.gpuUploadTarget);
        Add(obj, "gpuUploadBatches", atlas.gpuUploadBatches);
        Add(obj, "uploadedRegions", atlas.uploadedRegions);
        Add(obj, "uploadedBytes", atlas.uploadedBytes);
        Add(obj, "flushYields", atlas.flushYields);
        if (!string.IsNullOrEmpty(atlas.lastGpuUploadError))
            obj["lastGpuUploadError"] = atlas.lastGpuUploadError;
        return obj;
    }

    static void Add(JObject obj, string name, bool? value)
    {
        if (value.HasValue) obj[name] = value.Value;
    }

    static void Add(JObject obj, string name, int? value)
    {
        if (value.HasValue) obj[name] = value.Value;
    }

    static void Add(JObject obj, string name, long? value)
    {
        if (value.HasValue) obj[name] = value.Value;
    }
}
public class GlyphRasterData
{
    public List<float> frameTimes;

    /// <summary>Component-trigger to GPU-atlas-ready times; schema 1.5 uses a common async readback boundary, while older synchronous results may leave this empty.</summary>
    public List<float> e2eTimes;

    public int uniqueGlyphs;
    public long managedAlloc;
    public string status;
    public string statusReason;
    public GlyphBenchmarkConfig benchmark;
    public List<GlyphExecutionSample> executionSamples;
}

public sealed class GlyphBenchmarkConfig
{
    public string mode;
    public string requestedPath;
    public int iterations;
    public int warmupIterations;
    public int previewFrames;
    public bool captureProfile;
    public bool captureAlloc;
}

public sealed class GlyphExecutionSample
{
    public string trigger;
    public string rasterBackend;
    public string threading;
    public string completion;
    public string fallback;
    public List<GlyphAtlasExecutionData> atlases = new();
}

public sealed class GlyphAtlasExecutionData
{
    public string mode;
    public string requestedPath;
    public string backend;
    public string storage;
    public string preparation;
    public bool? cpuMirror;
    public bool? gpuUploadTarget;
    public List<string> writePaths = new();
    public int? gpuUploadBatches;
    public int? uploadedRegions;
    public long? uploadedBytes;
    public int? flushYields;
    public string lastGpuUploadError;
}
