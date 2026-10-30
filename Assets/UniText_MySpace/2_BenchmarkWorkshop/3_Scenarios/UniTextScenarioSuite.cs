using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UniText in the situations a shipped project meets every frame: a screen full of styled text, text that changes
/// every frame, animated text, typing, scrolling lists, object churn, HUDs, paged screens under masks and world-space
/// labels, plus the memory a create/destroy cycle leaves behind and the first screen of text a process opens. Every
/// 2D text carries an outline and a shadow, the configuration shipped projects use most. The HUD, page and screen
/// workloads also run with TMP as the control.
/// </summary>
/// <remarks>
/// Every frame scenario is measured the same way: thermal settle, setup, adaptive warmup until the frame cost
/// stops drifting, one garbage collection outside the window, then <see cref="measuredFrames"/> frames sampled
/// by <see cref="BenchmarkFrameSampler"/> and a validation that the workload really ran. Nothing in the harness
/// allocates inside a window, so allocation figures belong to the text engine and the scenario's own edits.
/// The first-screen measurement runs before every scenario, because only the first use of an engine in a process
/// pays its one-time costs.
/// </remarks>
public sealed class UniTextScenarioSuite : MonoBehaviour, IBenchmarkSuite
{
    const int SetupFrames = 3;
    const int SettleFrames = 6;
    const int MemoryCycles = 8;
    const int FirstScreenFrames = 4;
    const int WarmUpImages = 16;

    [Tooltip("Font stack every scenario text uses, so results never depend on the platform's system fonts.")]
    [SerializeField] UniTextFontStack fontStack;

    [Tooltip("TMP font asset of the font stack's primary font, drawn by the TMP twin of each compared scenario.")]
    [SerializeField] TMP_FontAsset tmpFont;

    [Tooltip("Material preset of the TMP font with outline and underlay, TMP's counterpart of the UniText texts' stroke and shadow.")]
    [SerializeField] Material tmpMaterial;

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

    [Tooltip("After each scenario's timed frames, records as many untimed frames through Prof and ships prof_scenario_{id}.json/.txt to Benchmarks/prof/{runId}/ (device: persistentDataPath). UniText's zones need UNITEXT_PROFILE.")]
    [SerializeField] bool captureProfile;

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
            "allocatedBytes counts managed bytes and allocatedObjects managed objects that every thread allocated inside the frame window; bytes exist in development players only, objects in IL2CPP players, release included. Both are garbage-collected heap; native buffers are in neither. The idle scenario draws no text: its figures are the player's and the harness's own floor, and a scenario's own allocation is its figure minus idle's."),
        new KeyValuePair<string, string>("scenarios.method",
            "Each scenario: thermal settle, setup, adaptive warmup until the mean frameMs of consecutive 12-frame windows agrees within 5% (or 0.05 ms), one forced GC outside the window, then the measured frames and a validation that the workload ran. Variants differ in exactly one property; the order reverses on odd repeats. Every 2D text carries a whole-text StrokeModifier and ShadowModifier at their defaults (TMP: an outline and underlay material preset); text3d keeps its own layers. screen.outline and screen.shadow drop one of the two effects. A scenario X.tmp is X drawn by TMP; screen.default is screen drawn with the default canvas material."),
        new KeyValuePair<string, string>("scenarios.startup",
            "startup: the first screen of 120 shop labels each engine opens in the process, then the same screen opened again. createMs is the synchronous creation, framesMs the intervals of the first frames from the start of creation, so framesMs[0] is the frame the user sees stall. A uGUI screen of images is drawn first so neither engine pays for the canvas; UniText goes first on even repeats. Only a player measures a cold engine; -benchmarkParticipant startup.unitext or startup.tmp measures one engine alone."),
        new KeyValuePair<string, string>("scenarios.memory",
            "memory.cycles: identical create-process-destroy cycles of paragraphs and labels; growth is the post-GC difference between the end of the first and the last cycle divided by the cycles between them. Each snapshot also carries the process's own memory where the player reads it: dirty (Android Private_Dirty + SwapPss, Windows PrivateUsage, iOS phys_footprint), clean and pss on Android; no GPU driver memory. " +
            (BenchmarkProcessMemory.Available ? "This player reads them." : "This player reads none: " + BenchmarkProcessMemory.UnavailableReason))
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        if (fontStack == null || tmpFont == null || tmpMaterial == null || textPrefab == null || worldTextPrefab == null ||
            inputFieldPrefab == null)
            throw new InvalidOperationException(
                $"{nameof(UniTextScenarioSuite)} needs its font stack, TMP font and material and its text, world text and input field prefabs assigned.");
        BenchmarkFrameProbe.Install();
        results.Clear();

        var startup = StartupEngines(context);
        var measureMemory = Measures(context, UniTextScenarios.MemoryId);
        var measureHidden = Measures(context, UniTextScenarios.HiddenMemoryId);
        var scenarios = Select(context, UniTextScenarios.Create(), startup.Count > 0 || measureMemory || measureHidden);
        PublishConfig(context, startup, scenarios, measureMemory);

        var rig = new ScenarioRig(fontStack, tmpFont, tmpMaterial, textPrefab, worldTextPrefab, inputFieldPrefab);
        try
        {
            if (startup.Count > 0)
            {
                var record = results[UniTextScenarios.StartupId] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {UniTextScenarios.StartupId}",
                    () => BenchmarkRecord.Guard(MeasureStartup(context, rig, startup, record), record), null);
                if (!context.Alive) yield break;
            }

            for (var i = 0; i < scenarios.Count; i++)
            {
                var scenario = scenarios[(context.Repeat & 1) == 0 ? i : scenarios.Count - 1 - i];
                var record = results[scenario.Id] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {scenario.Id}",
                    () => BenchmarkRecord.Guard(Measure(context, rig, scenario, record), record), null);
                if (!context.Alive) yield break;
            }

            if (measureMemory)
            {
                var record = results[UniTextScenarios.MemoryId] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {UniTextScenarios.MemoryId}",
                    () => BenchmarkRecord.Guard(MeasureMemory(context, rig, record), record), null);
            }

            if (measureHidden)
            {
                var record = results[UniTextScenarios.HiddenMemoryId] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {UniTextScenarios.HiddenMemoryId}",
                    () => BenchmarkRecord.Guard(MeasureHiddenMemory(context, rig, record), record), null);
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

    static List<UniTextScenario> Select(BenchmarkContext context, List<UniTextScenario> all, bool measuresOther)
    {
        if (context.Participant == null) return all;
        var selected = new List<UniTextScenario>();
        foreach (var scenario in all)
            if (Measures(context, scenario.Id))
                selected.Add(scenario);
        if (selected.Count == 0 && !measuresOther)
            throw new InvalidOperationException($"No UniText scenario answers to participant '{context.Participant}'.");
        return selected;
    }

    /// <summary>The engines whose first screen this process measures, UniText first on even repeats.</summary>
    static List<ScenarioEngine> StartupEngines(BenchmarkContext context)
    {
        var engines = new List<ScenarioEngine>(2);
        var order = (context.Repeat & 1) == 0
            ? new[] { ScenarioEngine.UniText, ScenarioEngine.Tmp }
            : new[] { ScenarioEngine.Tmp, ScenarioEngine.UniText };
        foreach (var engine in order)
            if (Measures(context, StartupKey(engine)))
                engines.Add(engine);
        return engines;
    }

    static string StartupKey(ScenarioEngine engine) =>
        $"{UniTextScenarios.StartupId}.{(engine == ScenarioEngine.Tmp ? "tmp" : "unitext")}";

    void PublishConfig(BenchmarkContext context, List<ScenarioEngine> startup, List<UniTextScenario> scenarios,
        bool measureMemory)
    {
        var order = new JArray();
        foreach (var engine in startup)
            order.Add(StartupKey(engine));
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
            ["tmpFont"] = tmpFont.name,
            ["tmpMaterial"] = tmpMaterial.name,
            ["reversed"] = (context.Repeat & 1) != 0,
            ["order"] = order
        };
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

            var steady = new BenchmarkSteadyState();
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
            if (captureProfile) yield return CaptureProfile(scenario, frame);
            yield return BenchmarkScreenshot.Capture($"scenario-{scenario.Id}");
        }
        finally
        {
            var started = Stopwatch.GetTimestamp();
            scenario.Teardown();
            rig.Clear();
            record["teardownMs"] = Round(Milliseconds(started));
            scenario.StopCountingCommits();
            UniText.UseParallel = true;
        }
    }

    /// <summary>
    /// Opens the first screen of text of each engine in <paramref name="engines"/> after a uGUI screen of images, then
    /// opens it again, timing the creation and the frames that follow.
    /// </summary>
    IEnumerator MeasureStartup(BenchmarkContext context, ScenarioRig rig, List<ScenarioEngine> engines, JObject record)
    {
        yield return context.ThermalSettle();
        var warmUp = rig.CreateRoot("uGUI warm-up");
        for (var i = 0; i < WarmUpImages; i++)
        {
            var image = new GameObject("Image", typeof(RectTransform), typeof(Image)) { layer = 5 }.GetComponent<Image>();
            image.transform.SetParent(warmUp, false);
            UniTextScenarios.PlaceCell(image, i, 4, WarmUpImages / 4);
        }
        for (var i = 0; i < SetupFrames; i++)
            yield return null;
        rig.Clear();
        yield return null;

        record["sinceLaunchSeconds"] = Round(Time.realtimeSinceStartupAsDouble);
        record["player"] = !Application.isEditor;
        foreach (var engine in engines)
        {
            var entry = new JObject();
            record[StartupKey(engine)] = entry;
            foreach (var open in new[] { "first", "second" })
            {
                var screen = new JObject();
                entry[open] = screen;
                yield return OpenScreen(rig, engine, screen);
                if (open == "first") yield return BenchmarkScreenshot.Capture($"scenario-{StartupKey(engine)}");
                rig.Clear();
                for (var i = 0; i < SetupFrames; i++)
                    yield return null;
            }
        }
        record["status"] = "measured";
    }

    static IEnumerator OpenScreen(ScenarioRig rig, ScenarioEngine engine, JObject screen)
    {
        var started = Stopwatch.GetTimestamp();
        var root = rig.CreateRoot("First screen");
        var labels = UniTextScenarios.FillPage(rig, engine, root, 0);
        screen["createMs"] = Round(Milliseconds(started));
        var frames = new JArray();
        var last = started;
        for (var i = 0; i < FirstScreenFrames; i++)
        {
            yield return null;
            var now = Stopwatch.GetTimestamp();
            frames.Add(Round((now - last) * 1000d / Stopwatch.Frequency));
            last = now;
        }
        screen["framesMs"] = frames;
        screen["totalMs"] = Round(Milliseconds(started));
        UniTextScenarios.RequireGlyphs(root);
        screen["texts"] = labels.Length;
        screen["glyphs"] = UniTextScenarios.CountGlyphs(root);
    }

    /// <summary>Steps <paramref name="scenario"/> through <see cref="measuredFrames"/> more frames under a Prof capture and ships the capture.</summary>
    IEnumerator CaptureProfile(UniTextScenario scenario, int frame)
    {
        ProfCapture capture;
        try
        {
            ProfCounters.Arm();
            Prof.BeginCapture();
            for (var i = 0; i < measuredFrames; i++)
            {
                scenario.Step(frame + i);
                yield return null;
                Prof.SampleFrame();
                ProfCounters.Sample();
            }
        }
        finally
        {
            capture = Prof.Capturing ? Prof.EndCapture() : null;
            if (ProfCounters.Armed)
            {
                ProfCounters.Disarm();
                if (capture != null) ProfCounters.AttachTo(capture);
            }
        }

        try
        {
            if (capture == null) yield break;
            var run = BenchmarkRun.Id;
            var path = $"prof/{run}/{run}_prof_scenario_{scenario.Id}";
            ProfTransport.Ship(capture.ToText(), path + ".txt");
            ProfTransport.Ship(capture.ToJson(), path + ".json");
        }
        finally
        {
            ProfCounters.Release();
            Prof.ReleaseCaptureBuffers();
        }
    }

    IEnumerator MeasureMemory(BenchmarkContext context, ScenarioRig rig, JObject record)
    {
        yield return context.ThermalSettle();
        var paragraphs = UniTextScenarios.Paragraphs(UniTextScenarios.LatinLines, 4, 24);
        var labels = UniTextScenarios.Labels(48);
        using var memory = new BenchmarkMemoryCounters();
        try
        {
            yield return BenchmarkMemoryCounters.Settle();
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
                yield return BenchmarkMemoryCounters.Settle();
                afterFirst = memory.Read();
            }
            yield return BenchmarkMemoryCounters.Settle();
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

    const int HiddenLabels = 200;
    const float HiddenWaitSeconds = 22f;

    IEnumerator MeasureHiddenMemory(BenchmarkContext context, ScenarioRig rig, JObject record)
    {
        yield return context.ThermalSettle();
        var content = UniTextScenarios.Labels(HiddenLabels);
        using var memory = new BenchmarkMemoryCounters();
        var phases = new JArray();
        try
        {
            for (var repeat = 0; repeat < 2; repeat++)
            {
                var root = rig.CreateRoot("HiddenMemory");
                var texts = UniTextScenarios.LabelGrid(rig, ScenarioEngine.UniText, root, content, 8, HiddenLabels / 8, 28f);
                var glyphs = 0;
                var renderers = 0;
                for (var i = 0; i < SetupFrames; i++)
                    yield return null;
                UniTextScenarios.RequireGlyphs(root);
                foreach (UniTextBase text in texts) glyphs += text.GlyphCount;
                renderers = root.GetComponentsInChildren<CanvasRenderer>(true).Length;
                yield return BenchmarkMemoryCounters.Settle();
                var shown = memory.Read();
                foreach (var text in texts) text.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(HiddenWaitSeconds);
                yield return BenchmarkMemoryCounters.Settle();
                var hidden = memory.Read();
                rig.Clear();
                yield return new WaitForSecondsRealtime(HiddenWaitSeconds);
                yield return BenchmarkMemoryCounters.Settle();
                var cleared = memory.Read();
                phases.Add(new JObject
                {
                    ["repeat"] = repeat, ["glyphs"] = glyphs, ["renderers"] = renderers, ["shown"] = shown,
                    ["hidden"] = hidden, ["cleared"] = cleared
                });
            }
            record["workload"] = new JObject { ["labels"] = HiddenLabels, ["waitSeconds"] = HiddenWaitSeconds };
            record["phases"] = phases;
            record["status"] = "measured";
        }
        finally
        {
            rig.Clear();
        }
    }

    static double Milliseconds(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000d / Stopwatch.Frequency;

    static double Round(double value) => Math.Round(value, 3);
}
