using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// UniText against itself in the situations a shipped project meets every frame: a screen full of styled
/// text, text that changes every frame, animated text, typing, scrolling lists, object churn and world-space
/// labels, plus the memory a create/destroy cycle leaves behind.
/// </summary>
/// <remarks>
/// Every frame scenario is measured the same way: thermal settle, setup, adaptive warmup until the frame cost
/// stops drifting, one garbage collection outside the window, then <see cref="measuredFrames"/> frames sampled
/// by <see cref="BenchmarkFrameSampler"/> and a validation that the workload really ran. Nothing in the harness
/// allocates inside a window, so allocation figures belong to UniText and the scenario's own edits.
/// </remarks>
public sealed class UniTextScenarioSuite : MonoBehaviour, IBenchmarkSuite
{
    const int SetupFrames = 3;
    const int SettleFrames = 6;
    const int MemoryCycles = 8;

    [Tooltip("Font stack every scenario text uses, so results never depend on the platform's system fonts.")]
    [SerializeField] UniTextFontStack fontStack;

    [Tooltip("The package's default Text (UniText) prefab, the one GameObject > LightSide > UniText creates.")]
    [SerializeField] GameObject textPrefab;

    [Tooltip("The package's default World Text (UniText) prefab.")]
    [SerializeField] GameObject worldTextPrefab;

    [Tooltip("The package's default Input Field prefab.")]
    [SerializeField] GameObject inputFieldPrefab;

    [Min(30), Tooltip("Frames sampled per scenario.")]
    [SerializeField] int measuredFrames = 120;

    [Min(24), Tooltip("Upper bound of the adaptive warmup; a scenario whose frame cost still drifts after it is reported as unsettled.")]
    [SerializeField] int maxWarmupFrames = 180;

    [Tooltip("Keep every frame's sample beside the summaries.")]
    [SerializeField] bool includeSamples = true;

    readonly Dictionary<string, JObject> results = new();

    public string SuiteId => "scenarios";

    public string Section => "unitextScenarios";

    public string StreamGlobal => "__unitextScenarioRuns";

    public int Scenario => 5;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("scenarios.windows",
            "Per frame: frameMs = main-thread work from the end of Initialization to the start of PresentAfterDraw (scripts, UniText, uGUI, render submission; no display wait); updateMs = end of Initialization to end of PreLateUpdate; canvasMs = PostLateUpdate.PlayerUpdateCanvases, where UniText processes canvas and world text; intervalMs = unscaledDeltaTime (on vsync-paced devices it is quantized to the refresh interval)."),
        new KeyValuePair<string, string>("scenarios.frameTiming",
            "cpuMainMs, cpuRenderMs and gpuMs come from FrameTimingManager (needs Frame Timing Stats in release players). gpuMs is the GPU time of the frame; it is absent on drivers without timer queries, and on software or paravirtual renderers (hosted CI runners, systemInfo.softwareRenderer) it measures the emulation, not a GPU."),
        new KeyValuePair<string, string>("scenarios.allocation",
            "allocatedBytes counts managed allocations of the main thread inside the frame window; worker-thread allocations are not included."),
        new KeyValuePair<string, string>("scenarios.method",
            "Each scenario: thermal settle, setup, adaptive warmup until the mean frameMs of consecutive 12-frame windows agrees within 5% (or 0.05 ms), one forced GC outside the window, then the measured frames and a validation that the workload ran. Variants differ in exactly one property; the order reverses on odd repeats."),
        new KeyValuePair<string, string>("scenarios.memory",
            "memory.cycles: identical create-process-destroy cycles of paragraphs and labels; growth is the post-GC difference between the end of the first and the last cycle divided by the cycles between them.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        if (fontStack == null || textPrefab == null || worldTextPrefab == null || inputFieldPrefab == null)
            throw new InvalidOperationException(
                $"{nameof(UniTextScenarioSuite)} needs its font stack and its text, world text and input field prefabs assigned.");
        BenchmarkFrameProbe.Install();
        results.Clear();

        var scenarios = Select(context, UniTextScenarios.Create());
        var measureMemory = Measures(context, UniTextScenarios.MemoryId);
        PublishConfig(context, scenarios, measureMemory);

        var rig = new ScenarioRig(fontStack, textPrefab, worldTextPrefab, inputFieldPrefab);
        try
        {
            for (var i = 0; i < scenarios.Count; i++)
            {
                var scenario = scenarios[(context.Repeat & 1) == 0 ? i : scenarios.Count - 1 - i];
                var record = results[scenario.Id] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {scenario.Id}",
                    () => Guard(Measure(context, rig, scenario, record), record), null);
                if (!context.Alive) yield break;
            }

            if (measureMemory)
            {
                var record = results[UniTextScenarios.MemoryId] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {UniTextScenarios.MemoryId}",
                    () => Guard(MeasureMemory(context, rig, record), record), null);
            }
        }
        finally
        {
            rig.Dispose();
            UniText.UseParallel = true;
        }
    }

    public JObject Serialize()
    {
        if (results.Count == 0) return null;
        var section = new JObject();
        foreach (var pair in results)
            section[pair.Key] = pair.Value;
        return section;
    }

    public bool Measured(out string reason)
    {
        reason = null;
        var measured = false;
        foreach (var pair in results)
        {
            var status = (string)pair.Value["status"];
            if (status == "measured")
            {
                measured = true;
                continue;
            }
            reason = $"Scenario '{pair.Key}' ended with status '{status}'.";
            return false;
        }
        return measured;
    }

    static bool Measures(BenchmarkContext context, string id)
    {
        var participant = context.Participant;
        return participant == null
               || string.Equals(id, participant, StringComparison.OrdinalIgnoreCase)
               || id.StartsWith(participant + ".", StringComparison.OrdinalIgnoreCase);
    }

    static List<UniTextScenario> Select(BenchmarkContext context, List<UniTextScenario> all)
    {
        if (context.Participant == null) return all;
        var selected = new List<UniTextScenario>();
        foreach (var scenario in all)
            if (Measures(context, scenario.Id))
                selected.Add(scenario);
        if (selected.Count == 0 && !Measures(context, UniTextScenarios.MemoryId))
            throw new InvalidOperationException($"No UniText scenario answers to participant '{context.Participant}'.");
        return selected;
    }

    void PublishConfig(BenchmarkContext context, List<UniTextScenario> scenarios, bool measureMemory)
    {
        var order = new JArray();
        foreach (var scenario in scenarios)
            order.Add(scenario.Id);
        if (measureMemory) order.Add(UniTextScenarios.MemoryId);
        context.Config["scenarios"] = new JObject
        {
            ["measuredFrames"] = measuredFrames,
            ["maxWarmupFrames"] = maxWarmupFrames,
            ["setupFrames"] = SetupFrames,
            ["settleFrames"] = SettleFrames,
            ["memoryCycles"] = MemoryCycles,
            ["referenceResolution"] = $"{ScenarioRig.ReferenceWidth}x{ScenarioRig.ReferenceHeight}",
            ["fontStack"] = fontStack.name,
            ["reversed"] = (context.Repeat & 1) != 0,
            ["order"] = order
        };
    }

    /// <summary>Runs <paramref name="routine"/> to its end, writing a failure into <paramref name="record"/> before rethrowing it to the runner.</summary>
    static IEnumerator Guard(IEnumerator routine, JObject record)
    {
        var owned = new OwnedEnumerator(routine);
        Exception failure = null;
        var cleanupFailure = false;
        try
        {
            while (owned.MoveNext(out var current, out failure, out cleanupFailure))
                yield return current;
        }
        finally
        {
            failure = owned.Dispose(failure, ref cleanupFailure);
        }
        if (failure == null) yield break;
        record["status"] = "failed";
        record["reason"] = failure.Message;
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    IEnumerator Measure(BenchmarkContext context, ScenarioRig rig, UniTextScenario scenario, JObject record)
    {
        yield return context.ThermalSettle();
        UniText.UseParallel = scenario.Parallel;
        try
        {
            var started = Stopwatch.GetTimestamp();
            scenario.Setup(rig);
            record["setupMs"] = Round(Milliseconds(started));

            var frame = 0;
            for (var i = 0; i < SetupFrames; i++)
            {
                scenario.Step(frame++);
                yield return null;
            }
            scenario.Validate();

            var steady = new SteadyState();
            while (!steady.Settled && steady.Frames < maxWarmupFrames)
            {
                scenario.Step(frame++);
                yield return null;
                steady.Observe((float)BenchmarkFrameProbe.LastFrameMilliseconds);
            }
            record["warmup"] = new JObject { ["frames"] = steady.Frames, ["settled"] = steady.Settled };

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (var sampler = new BenchmarkFrameSampler(measuredFrames))
            {
                for (var i = 0; i < SettleFrames; i++)
                {
                    scenario.Step(frame++);
                    yield return null;
                }

                scenario.ResetCommits();
                for (var i = 0; i < measuredFrames; i++)
                {
                    scenario.Step(frame++);
                    yield return null;
                    sampler.Sample();
                }
                record["metrics"] = sampler.Serialize(includeSamples);
            }

            scenario.Validate();
            scenario.RequireCommits(measuredFrames);
            var workload = new JObject { ["parallel"] = scenario.Parallel };
            scenario.Describe(workload);
            record["workload"] = workload;
            record["status"] = "measured";
            yield return BenchmarkScreenshot.Capture($"scenario-{scenario.Id}");
        }
        finally
        {
            var started = Stopwatch.GetTimestamp();
            scenario.Teardown();
            rig.Clear();
            record["teardownMs"] = Round(Milliseconds(started));
            UniText.UseParallel = true;
        }
    }

    IEnumerator MeasureMemory(BenchmarkContext context, ScenarioRig rig, JObject record)
    {
        yield return context.ThermalSettle();
        var paragraphs = UniTextScenarios.Paragraphs(UniTextScenarios.LatinLines, 4, 24);
        var labels = UniTextScenarios.Labels(48);
        using var memory = new MemoryCounters();
        try
        {
            yield return SettleMemory();
            var before = memory.Read();
            JObject afterFirst = null;
            for (var cycle = 0; cycle < MemoryCycles; cycle++)
            {
                var root = rig.CreateRoot("MemoryCycle");
                UniTextScenarios.Grid(rig, root, paragraphs, 4, 6, 20f, 0.5f);
                UniTextScenarios.Grid(rig, root, labels, 4, 12, 24f, 0.5f, 0.5f);
                for (var i = 0; i < SetupFrames; i++)
                    yield return null;
                if (cycle == 0) UniTextScenarios.RequireGlyphs(root);
                rig.Clear();
                for (var i = 0; i < SetupFrames; i++)
                    yield return null;
                if (cycle != 0) continue;
                yield return SettleMemory();
                afterFirst = memory.Read();
            }
            yield return SettleMemory();
            var after = memory.Read();

            var growth = new JObject();
            foreach (var pair in after)
                growth[pair.Key] = Math.Round(((long)pair.Value - (long)afterFirst[pair.Key]) / (double)(MemoryCycles - 1));
            record["workload"] = new JObject
            {
                ["cycles"] = MemoryCycles,
                ["paragraphsPerCycle"] = 24,
                ["labelsPerCycle"] = 48
            };
            record["before"] = before;
            record["afterFirstCycle"] = afterFirst;
            record["afterLastCycle"] = after;
            record["growthPerCycleBytes"] = growth;
            record["status"] = "measured";
        }
        finally
        {
            rig.Clear();
        }
    }

    static IEnumerator SettleMemory()
    {
        for (var i = 0; i < 2; i++)
            yield return null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (var i = 0; i < 2; i++)
            yield return null;
    }

    static double Milliseconds(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000d / Stopwatch.Frequency;

    static double Round(double value) => Math.Round(value, 3);

    /// <summary>
    /// Declares warmup over once the mean of consecutive 12-frame windows agrees within 5%, or within 0.05 ms for
    /// frames too cheap for a relative bound to mean anything.
    /// </summary>
    sealed class SteadyState
    {
        const int Window = 12;
        const float Tolerance = 0.05f;
        const float AbsoluteToleranceMs = 0.05f;

        readonly float[] recent = new float[Window];
        float previousMean;

        public int Frames { get; private set; }

        public bool Settled { get; private set; }

        public void Observe(float milliseconds)
        {
            recent[Frames % Window] = milliseconds;
            Frames++;
            if (Frames < Window * 2 || Frames % Window != 0) return;
            var mean = 0f;
            foreach (var value in recent)
                mean += value;
            mean /= Window;
            Settled = Mathf.Abs(mean - previousMean) <= Mathf.Max(previousMean * Tolerance, AbsoluteToleranceMs);
            previousMean = mean;
        }
    }

    sealed class MemoryCounters : IDisposable
    {
        static readonly string[] Names =
        {
            "GC Used Memory", "GC Reserved Memory", "Total Used Memory", "Total Reserved Memory", "System Used Memory"
        };

        static readonly string[] Keys = { "gcUsed", "gcReserved", "totalUsed", "totalReserved", "systemUsed" };

        readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Names.Length];

        public MemoryCounters()
        {
            for (var i = 0; i < Names.Length; i++)
                recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Memory, Names[i], 1);
        }

        public JObject Read()
        {
            var snapshot = new JObject();
            for (var i = 0; i < recorders.Length; i++)
            {
                if (!recorders[i].Valid)
                    throw new NotSupportedException($"The player exposes no '{Names[i]}' counter.");
                snapshot[Keys[i]] = recorders[i].CurrentValue;
            }
            return snapshot;
        }

        public void Dispose()
        {
            foreach (var recorder in recorders)
                recorder.Dispose();
        }
    }
}
