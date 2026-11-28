using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class BenchmarkJsonSerializer
{
    /// <summary>
    /// Writes a memory run of the text suite into <paramref name="record"/> in the scenario suite's shape: <c>setupPeak</c>
    /// is the creation window, <c>metrics</c> holds the idle window as <c>processPeak</c> and the system's read before
    /// cleanup as <c>systemFootprint</c>; <c>phases</c> has one object per phase in run order (creation first) with its
    /// stepped frames, the texts reprocessed over them and their expected count (-1 where the engine reports none or the
    /// phase sets no requirement), its memory checkpoints and peak window; <c>memory</c> the run's checkpoints. Sets
    /// <c>status</c> to measured.
    /// </summary>
    internal static void WriteTextMemory(JObject record, TextBenchmarkBase.TestResults results)
    {
        var phases = new JObject();
        var metrics = new JObject();
        foreach (var pair in results.phases)
        {
            var phase = pair.Value;
            var entry = new JObject
            {
                ["frames"] = phase.frames,
                ["commits"] = phase.commits,
                ["expectedCommits"] = phase.expectedCommits
            };
            if (phase.memory.available)
                entry["memory"] = SerializePhaseMemory(phase.memory);
            if (phase.memory.processPeak != null)
            {
                entry["processPeak"] = phase.memory.processPeak;
                if (pair.Key == "creation") record["setupPeak"] = phase.memory.processPeak;
                if (pair.Key == TextPhases.Key(TextPhase.Idle)) metrics["processPeak"] = phase.memory.processPeak;
            }
            phases[pair.Key] = entry;
        }
        if (results.memory.systemFootprint != null) metrics["systemFootprint"] = results.memory.systemFootprint;
        record["metrics"] = metrics;
        record["phases"] = phases;
        if (results.memory.available)
            record["memory"] = SerializeRunMemoryCheckpoints(results.memory);
        record["status"] = "measured";
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
        AppendSnapshot(sb, "after settle frames", memory.afterWarmup);
        AppendSnapshot(sb, "measured peak", memory.measuredPeak);
        AppendSnapshot(sb, "measured end", memory.measuredEnd);
        AppendSnapshot(sb, "end before GC", memory.normalizedEnd);
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
        AppendSnapshot(sb, "after pool retention + GC", memory.afterRetention);
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

    static JObject SerializeRunMemory(TextBenchmarkBase.RunMemoryMetrics memory)
    {
        var record = SerializeRunMemoryCheckpoints(memory);
        if (memory.systemFootprint != null) record["systemFootprint"] = memory.systemFootprint;
        return record;
    }

    static JObject SerializeRunMemoryCheckpoints(TextBenchmarkBase.RunMemoryMetrics memory) => new()
    {
        ["baseline"] = SerializeSnapshot(memory.baseline),
        ["peak"] = SerializeSnapshot(memory.peak),
        ["beforeCleanup"] = SerializeSnapshot(memory.beforeCleanup),
        ["afterCleanup"] = SerializeSnapshot(memory.afterCleanup),
        ["afterRetention"] = SerializeSnapshot(memory.afterRetention),
        ["derived"] = new JObject
        {
            ["sampledPeakDelta"] = SerializeDelta(memory.baseline, memory.peak),
            ["retainedFootprint"] = SerializeDelta(memory.baseline, memory.beforeCleanup),
            ["postRunAppDelta"] = SerializeDelta(memory.baseline, memory.afterCleanup),
            ["keptAfterRetention"] = SerializeDelta(memory.baseline, memory.afterRetention)
        }
    };

    static JObject SerializeSnapshot(TextBenchmarkBase.MemorySnapshot snapshot) => new()
    {
        ["resident"] = Number(snapshot.resident),
        ["unityUsed"] = Number(snapshot.used),
        ["unityReserved"] = Number(snapshot.reserved),
        ["gcUsed"] = Number(snapshot.gcUsed),
        ["gcReserved"] = Number(snapshot.gcReserved),
        ["buffers"] = Number(snapshot.buffers),
        ["dirty"] = Number(snapshot.dirty),
        ["clean"] = Number(snapshot.clean),
        ["pss"] = Number(snapshot.pss)
    };

    static JObject SerializeDelta(TextBenchmarkBase.MemorySnapshot from, TextBenchmarkBase.MemorySnapshot to) => new()
    {
        ["resident"] = Delta(from.resident, to.resident),
        ["unityUsed"] = Delta(from.used, to.used),
        ["unityReserved"] = Delta(from.reserved, to.reserved),
        ["gcUsed"] = Delta(from.gcUsed, to.gcUsed),
        ["gcReserved"] = Delta(from.gcReserved, to.gcReserved),
        ["buffers"] = Delta(from.buffers, to.buffers),
        ["dirty"] = Delta(from.dirty, to.dirty),
        ["clean"] = Delta(from.clean, to.clean),
        ["pss"] = Delta(from.pss, to.pss)
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
          .Append(" | dirty ").Append(FormatValue(value.dirty))
          .Append(" | clean ").Append(FormatValue(value.clean))
          .Append(" | pss ").Append(FormatValue(value.pss))
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
          .Append(" | dirty ").Append(FormatDelta(to.dirty, from.dirty))
          .Append(" | clean ").Append(FormatDelta(to.clean, from.clean))
          .Append(" | pss ").Append(FormatDelta(to.pss, from.pss))
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
}
