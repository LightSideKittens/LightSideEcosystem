using System;
using System.Collections;
using System.Collections.Generic;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;

/// <summary>Publishes the motion-engine comparison to the shared benchmark runner.</summary>
public sealed partial class MotionBenchmark : IBenchmarkSuite
{
    public string SuiteId => "motion";

    public string Section => "motionBenchmarks";

    public string StreamGlobal => "__moveitMotionRuns";

    public int Scenario => 4;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("motion",
            "Headline frame samples are the complete Internal/Main Thread marker over long finite, non-looping workloads after warmup, post-warmup GC normalization, and fixed sacrificial settling frames. The sequence topology is proven in a separate short context that crosses at least two ordered child boundaries before the measured context is created. Optional exact engine markers run in a separate detail pass on the same warmed measured context, so their recorders cannot affect headline samples; unavailable markers remain explicitly unavailable. A non-empty workload must produce an observable output change before either recorder pass; semantic validation runs between and after passes outside recorder windows. Workloads run adapter-alternating in workload-major order. Creation separates the first adapter-session batch, which is not process-cold, from warm recycled batches; preparation, validation, and cleanup are outside direct create-call time and current-thread allocation measurements.")
    };

    /// <summary>The run this suite belongs to; carries which participant this process owns.</summary>
    BenchmarkContext session;

    public IEnumerator Run(BenchmarkContext context)
    {
        session = context;
        var previous = Results;
        yield return context.Run(SuiteId, RunBenchmarkCoroutine, null,
            () =>
            {
                BenchmarkFrameProbe.Uninstall();
                if (ReferenceEquals(Results, previous)) Results = null;
            });
    }

    public JObject Serialize() => Results == null ? null : SerializeMotionBenchmarks(Results);

    public bool Measured(out string reason)
    {
        reason = null;
        if (Results == null) return false;

        var any = false;
        foreach (var engine in Results.engines.Values)
        {
            any |= engine.status == "measured";
            if (engine.status is "failed" or "partial" or "measuring")
            {
                reason = $"Requested motion suite ended with engine status '{engine.status}'.";
                return false;
            }
        }
        return any;
    }

    static JObject SerializeMotionBenchmarks(MotionBenchmarkData data)
    {
        if (data == null) return new JObject();

        var config = data.config;
        var engines = new JObject();
        foreach (var pair in data.engines)
            engines[pair.Key] = SerializeMotionEngine(pair.Value);

        return new JObject
        {
            ["config"] = new JObject
            {
                ["sharedMotionCount"] = config.sharedMotionCount,
                ["keyedFloatCount"] = config.keyedFloatCount,
                ["distinctTransformCount"] = config.distinctTransformCount,
                ["sequenceCount"] = config.sequenceCount,
                ["sequenceLength"] = config.sequenceLength,
                ["steadyMotionDurationSeconds"] = config.steadyMotionDurationSeconds,
                ["warmupFrames"] = config.warmupFrames,
                ["measuredFrames"] = config.measuredFrames,
                ["recorderSettlingFrames"] = config.recorderSettlingFrames,
                ["settleGarbage"] = config.settleGarbage,
                ["measurePhaseDetail"] = config.measurePhaseDetail,
                ["creationBatchSize"] = config.creationBatchSize,
                ["creationWarmupBatches"] = config.creationWarmupBatches,
                ["creationSamples"] = config.creationSamples,
                ["executionIsolation"] = config.executionIsolation,
                ["executionOrder"] = config.executionOrder,
                ["adapterOrder"] = new JArray(config.adapterOrder ?? Array.Empty<string>()),
                ["workloadOrder"] = new JArray(config.workloadOrder ?? Array.Empty<string>()),
                ["measurementPasses"] = config.measurementPasses,
                ["validationBoundary"] = config.validationBoundary,
                ["timeScale"] = config.timeScale,
                ["targetFrameRate"] = config.targetFrameRate,
                ["vSyncCount"] = config.vSyncCount
            },
            ["engines"] = engines
        };
    }

    static JObject SerializeMotionEngine(MotionBenchmarkEngineData engine)
    {
        var workloads = new JObject();
        foreach (var pair in engine.workloads)
            workloads[pair.Key] = SerializeMotionWorkload(pair.Value);

        var obj = new JObject
        {
            ["status"] = engine.status,
            ["adapterType"] = engine.adapterType,
            ["metadata"] = SerializeMotionMetadata(engine.metadata),
            ["workloads"] = workloads,
            ["creation"] = SerializeMotionCreation(engine.creation)
        };
        if (!string.IsNullOrEmpty(engine.statusReason))
            obj["statusReason"] = engine.statusReason;
        return obj;
    }

    static JObject SerializeMotionMetadata(in MotionBenchmarkAdapterMetadata metadata) => new()
    {
        ["engineVersion"] = metadata.EngineVersion,
        ["sourceRevision"] = metadata.SourceRevision,
        ["integration"] = metadata.Integration,
        ["capacityPolicy"] = metadata.CapacityPolicy,
        ["handleRetention"] = metadata.HandleRetention
    };

    static JObject SerializeMotionSpec(in MotionBenchmarkSpec spec) => new()
    {
        ["workload"] = spec.Workload.ToString(),
        ["motionCount"] = spec.MotionCount,
        ["childMotionCount"] = spec.ChildMotionCount,
        ["sequenceRootCount"] = spec.SequenceRootCount,
        ["totalLiveMotionCoreCount"] = spec.TotalLiveCoreCount,
        ["transformCount"] = spec.TransformCount,
        ["sequenceCount"] = spec.SequenceCount,
        ["sequenceLength"] = spec.SequenceLength,
        ["durationSeconds"] = spec.DurationSeconds,
        ["from"] = spec.From,
        ["to"] = spec.To,
        ["ease"] = spec.Ease.ToString(),
        ["clock"] = spec.Clock.ToString(),
        ["updatePhase"] = spec.UpdatePhase.ToString(),
        ["cycles"] = spec.Cycles,
        ["cycleMode"] = spec.CycleMode.ToString(),
        ["topology"] = spec.Topology.ToString(),
        ["essential"] = spec.Essential
    };

    static JObject SerializeMotionWorkload(MotionBenchmarkWorkloadData workload)
    {
        var markers = new JObject();
        foreach (var pair in workload.markers)
            markers[pair.Key] = SerializeMotionSeries(pair.Value);

        var obj = new JObject
        {
            ["status"] = workload.status,
            ["spec"] = SerializeMotionSpec(workload.spec),
            ["warmupFrames"] = workload.warmupFrames,
            ["warmupSettled"] = workload.warmupSettled,
            ["setupMilliseconds"] = workload.setupMilliseconds,
            ["validateMilliseconds"] = workload.validateMilliseconds,
            ["teardownMilliseconds"] = workload.teardownMilliseconds,
            ["teardownCollections"] = workload.teardownCollections,
            ["mainThread"] = SerializeMotionSeries(workload.mainThread),
            ["mainThreadCpu"] = SerializeMotionSeries(workload.mainThreadCpu),
            ["gcBytesPerFrame"] = SerializeMotionSeries(workload.gcBytesPerFrame),
            ["markers"] = markers
        };
        if (!string.IsNullOrEmpty(workload.statusReason))
            obj["statusReason"] = workload.statusReason;
        return obj;
    }

    static JObject SerializeMotionCreation(MotionBenchmarkCreationData creation)
    {
        if (creation == null) return new JObject();
        var obj = new JObject
        {
            ["status"] = creation.status,
            ["spec"] = SerializeMotionSpec(creation.spec),
            ["firstBatchScope"] = creation.firstBatchScope,
            ["warmRecycledScope"] = creation.warmRecycledScope,
            ["teardownMilliseconds"] = creation.teardownMilliseconds,
            ["teardownMotions"] = creation.teardownMotions,
            ["teardownMarkers"] = JObject.FromObject(creation.teardownMarkers),
            ["firstBatch"] = SerializeMotionCreationPass(creation.firstBatch),
            ["warmRecycled"] = SerializeMotionCreationPass(creation.warmRecycled)
        };
        if (!string.IsNullOrEmpty(creation.statusReason))
            obj["statusReason"] = creation.statusReason;
        return obj;
    }

    static JObject SerializeMotionCreationPass(MotionBenchmarkCreationPassData pass)
    {
        var obj = new JObject
        {
            ["status"] = pass.status,
            ["timePerCreation"] = SerializeMotionSeries(pass.timePerCreation),
            ["gcBytesPerCreation"] = SerializeMotionSeries(pass.gcBytesPerCreation)
        };
        if (pass.markers.Count > 0)
        {
            var markers = new JObject();
            foreach (var pair in pass.markers)
                markers[pair.Key] = SerializeMotionSeries(pair.Value);
            obj["markers"] = markers;
        }
        if (!string.IsNullOrEmpty(pass.statusReason))
            obj["statusReason"] = pass.statusReason;
        return obj;
    }

    static JObject SerializeMotionSeries(MotionBenchmarkSeriesData series)
    {
        var samples = series.samples ?? new List<float>();
        var sorted = new List<float>(samples);
        sorted.Sort();

        var obj = new JObject
        {
            ["marker"] = series.marker,
            ["unit"] = series.unit,
            ["measurementPass"] = series.measurementPass,
            ["status"] = series.status,
            ["samples"] = new JArray(samples.ToArray()),
            ["sampleCount"] = samples.Count
        };
        if (!string.IsNullOrEmpty(series.statusReason))
            obj["statusReason"] = series.statusReason;

        if (sorted.Count == 0)
        {
            obj["median"] = JValue.CreateNull();
            obj["p95"] = JValue.CreateNull();
            obj["min"] = JValue.CreateNull();
            obj["max"] = JValue.CreateNull();
            obj["average"] = JValue.CreateNull();
            return obj;
        }

        double sum = 0;
        for (int i = 0; i < sorted.Count; i++)
            sum += sorted[i];
        obj["median"] = BenchmarkStatistics.MedianSorted(sorted);
        obj["p95"] = BenchmarkStatistics.PercentileSorted(sorted, 0.95f);
        obj["min"] = sorted[0];
        obj["max"] = sorted[sorted.Count - 1];
        obj["average"] = sum / sorted.Count;
        return obj;
    }
}
