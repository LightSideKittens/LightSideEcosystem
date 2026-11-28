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

/// <summary>A workload measured in one timing launch: its arms are built together and run one at a time in randomized rounds.</summary>
sealed class ScenarioCase
{
    public ScenarioCase(string id, params UniTextScenario[] arms)
    {
        if (arms == null || arms.Length == 0) throw new ArgumentException($"Case '{id}' has no arms.", nameof(arms));
        Id = id;
        Arms = arms;
    }

    public string Id { get; }

    /// <summary>The case's scenarios; the first is UniText's default configuration, the others its twins and variants.</summary>
    public IReadOnlyList<UniTextScenario> Arms { get; }
}

/// <summary>
/// UniText in the situations a shipped project meets every frame: a screen full of styled text, text that changes
/// every frame, animated text, typing, scrolling lists, object churn, HUDs, paged screens under masks and world-space
/// labels, plus the memory a create/destroy cycle leaves behind and the first screen of text a process opens. The HUD,
/// page, screen and world workloads also run with TMP as the control.
/// </summary>
/// <remarks>
/// A timing launch measures one case: all its arms are set up in the one rig, then <see cref="BenchmarkRounds"/> runs
/// them in randomized rounds with an in-launch reference, and every arm is captured at <see cref="StillFrame"/> in an
/// untimed pass afterwards. A memory launch measures one arm alone with the process memory sampled. The first screen of
/// text is a one-shot case whose round 0 is each engine's first use in the process.
/// </remarks>
public sealed class UniTextScenarioSuite : MonoBehaviour, IBenchmarkSuite, IIsolatedLaunchSuite
{
    /// <summary>The frame passed to <see cref="UniTextScenario.Step"/> for the stills: a fixed content the same in every arm, never reached by measurement.</summary>
    public const int StillFrame = 1 << 30;

    const int SetupFrameBase = 1 << 28;
    const int ProfileFrameBase = 3 << 20;
    const int SetupFrames = 3;
    const int StillSettleFrames = 3;
    const int MemoryFrames = 120;
    const int MemoryCycles = 40;
    const int MemoryWarmupCycles = 24;
    const int StartupCommitFrames = 30;
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

    [Tooltip("The render pipeline's default particle material, the one a new ParticleSystem gets, drawn by the ParticleSystem participants of the particle scenarios.")]
    [SerializeField] Material particleMaterial;

    [Tooltip("UniGUI's default Button prefab, measured by the controls.button.today scenarios; empty skips them.")]
    [SerializeField] GameObject buttonPrefab;

    [Tooltip("The Button prototype of the next control model, measured by the controls.button.proto scenarios; empty skips them.")]
    [SerializeField] GameObject buttonPrototype;

    [Tooltip("The Button prototype with its styles in a shared InteractionStyleSet, measured by the controls.button.shared scenarios; empty skips them.")]
    [SerializeField] GameObject buttonShared;

    [Tooltip("UniGUI's default Calendar prefab, measured by the controls.calendar.today scenarios; empty skips them.")]
    [SerializeField] GameObject calendarPrefab;

    [Tooltip("The Calendar prototype of the next control model, measured by the controls.calendar.proto scenarios; empty skips them.")]
    [SerializeField] GameObject calendarPrototype;

    [Min(2), Tooltip("Rounds per case; every round runs every arm once in a random order. Round 0 is the warm-up round.")]
    [SerializeField] int rounds = 8;

    [Min(0), Tooltip("Stepped frames before each slot's measured frames.")]
    [SerializeField] int settleFrames = 30;

    [Min(10), Tooltip("Measured frames per slot.")]
    [SerializeField] int slotFrames = 60;

    [Min(24), Tooltip("Upper bound of each arm's adaptive warmup; an arm whose main-thread CPU still drifts after it is reported as unsettled.")]
    [SerializeField] int maxWarmupFrames = 180;

    [Tooltip("Keep every frame's sample beside the summaries.")]
    [SerializeField] bool includeSamples = true;

    [Tooltip("After each case's rounds, records as many untimed frames per arm through Prof and ships prof_scenario_{id}.json/.txt to Benchmarks/prof/{runId}/ (device: persistentDataPath). UniText's zones need UNITEXT_PROFILE.")]
    [SerializeField] bool captureProfile;

    readonly Dictionary<string, JObject> results = new();

    public string SuiteId => "scenarios";

    public string Section => "unitextScenarios";

    public string StreamGlobal => "__unitextScenarioRuns";

    public int Scenario => 5;

    /// <summary>The first screen of text, then every case of the catalog.</summary>
    public IReadOnlyList<string> TimingCases => timingCases ??= ListTimingCases();

    /// <summary>The empty scene (the memory floor), every arm of every case of the catalog, then the memory cycles and the hidden-label memory.</summary>
    public IReadOnlyList<string> MemoryParticipants => memoryParticipants ??= ListMemoryParticipants();

    /// <summary>The arms of <paramref name="caseId"/>, each a memory participant; none for the first screen.</summary>
    public IReadOnlyList<string> MemoryParticipantsOf(string caseId)
    {
        if (caseArms == null)
        {
            caseArms = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var scenarioCase in UniTextScenarios.Cases())
            {
                var ids = new List<string>(scenarioCase.Arms.Count);
                foreach (var arm in scenarioCase.Arms)
                    ids.Add(arm.Id);
                caseArms[scenarioCase.Id] = ids;
            }
        }
        if (caseArms.TryGetValue(caseId, out var arms)) return arms;
        return Array.Empty<string>();
    }

    List<string> timingCases;
    List<string> memoryParticipants;
    Dictionary<string, List<string>> caseArms;

    static List<string> ListTimingCases()
    {
        var ids = new List<string> { UniTextScenarios.StartupId };
        foreach (var scenarioCase in UniTextScenarios.Cases())
            ids.Add(scenarioCase.Id);
        return ids;
    }

    static List<string> ListMemoryParticipants()
    {
        var ids = new List<string> { BenchmarkRounds.ReferenceArmId };
        foreach (var scenarioCase in UniTextScenarios.Cases())
        foreach (var arm in scenarioCase.Arms)
            ids.Add(arm.Id);
        ids.Add(UniTextScenarios.MemoryId);
        ids.Add(UniTextScenarios.HiddenMemoryId);
        return ids;
    }

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("scenarios.method",
            "A timing launch measures one case: every arm (UniText's default, its twins and variants) is set up in one rig and validated, then each is warmed up until its main-thread CPU (FrameTimingManager cpuMainMs, the frame window where it is off) settles, then rounds run: every round visits the in-launch reference (no arm active) and every arm once, in an order drawn from the case's seed, each slot a forced GC, settle frames and measured frames, then the arm's commit and validation check. Round 0 is the warm-up round. Each slot's cpuMs is its median main-thread CPU and names its source. The clock is the launch's (config.clock): every paced frame's main thread starts with a busy-wait that brings its load to one target (metrics.fillMs, excluded from cpuMs and processCpuMs), so every slot loads it alike and starts its work on a running core; on Android 12+ a Performance Hint session over every thread is also told every frame is late. With -benchmarkGates the case also runs <first arm>.aa (the first arm again, an A/A) and <control base>.control, the first arm whose threads is not parallel plus a busy loop per frame whose in-slot time is controlBusyMs: the control's cpuMs minus its base's in the same round recovers it, and a phone's CPU numbers of a case count only where that recovery is 1 within 10 %. Engines are compared by their net cpuMs above the reference of the same round, never by raw milliseconds across launches. A memory launch sets up one arm alone and samples the process memory over its setup and frames; its timings are not timing results."),
        new KeyValuePair<string, string>("scenarios.windows",
            "Per frame: frameMs = end of Initialization to start of PresentAfterDraw (it holds the display wait on Android); updateMs = end of Initialization to end of PreLateUpdate; canvasMs = PostLateUpdate.PlayerUpdateCanvases, where UniText processes canvas and world text; intervalMs = unscaledDeltaTime. cpuMainMs, cpuRenderMs and gpuMs come from FrameTimingManager; cpuMainMs excludes the waits for the render thread, the present and the target frame rate. processCpuMs (Android and iOS players) is the CPU time of every thread of the process per measured frame: main, render, Unity job workers, UniText workers, GC, spin-waits included; above the reference it is the total work on phones whose clock is a hint session over all threads."),
        new KeyValuePair<string, string>("scenarios.allocation",
            "allocatedBytes counts managed bytes and allocatedObjects managed objects that every thread allocated inside the frame window; bytes exist in development players only, objects in IL2CPP players, release included. The reference slots are the player's and the harness's own floor."),
        new KeyValuePair<string, string>("scenarios.startup",
            "startup: a one-shot case after a uGUI screen of images, so neither engine pays for the canvas. Each round opens the first screen of 120 shop labels with each engine in a random order and clears it again; round 0 is each engine's first use in the process. A slot is fenced on the GPU first; syncMs is the synchronous creation, frames the completed frames until every label holds glyphs (frames to pixels), workMs the update and canvas windows of those frames, frameTimings FrameTimingManager's series of those frames and the next 8."),
        new KeyValuePair<string, string>("scenarios.memory",
            "memory.cycles: identical create-process-destroy cycles of paragraphs and labels, a post-GC snapshot before the first cycle and after every cycle. The first warmupCycles fill pools and the managed heap once; growthPerCycleBytes is the difference between the snapshots after the last warm-up cycle and after the last cycle, divided by the cycles between them, and emptyGrowthPerCycleBytes the same for cycles that create no text, run first. Only growth above the empty cycles belongs to the text. Each snapshot also carries the process's own memory where the player reads it: dirty (Android Private_Dirty + SwapPss, Windows PrivateUsage, iOS phys_footprint), clean and pss on Android; no GPU driver memory. " +
            (BenchmarkProcessMemory.Available ? "This player reads them." : "This player reads none: " + BenchmarkProcessMemory.UnavailableReason)),
        new KeyValuePair<string, string>("scenarios.peak",
            "A memory launch's arm carries setupPeak over its setup and metrics.processPeak over its measured frames, both {start, peak, growth} with memory held only inside a frame included (systemInfo.processPeak names the counter). Timing launches sample no memory.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        if (fontStack == null || tmpFont == null || tmpMaterial == null || textPrefab == null || worldTextPrefab == null ||
            inputFieldPrefab == null)
            throw new InvalidOperationException(
                $"{nameof(UniTextScenarioSuite)} needs its font stack, TMP font and material and its text, world text and input field prefabs assigned.");
        BenchmarkFrameProbe.Install();
        results.Clear();

        var cases = UniTextScenarios.Cases();
#if LIGHTSIDE_HAS_UNIGUI
        foreach (var control in UniGUIControlScenarios.Create(buttonPrefab, buttonPrototype, buttonShared, calendarPrefab, calendarPrototype))
            cases.Add(new ScenarioCase(control.Id, control));
#endif
        var rig = new ScenarioRig(fontStack, tmpFont, tmpMaterial, textPrefab, worldTextPrefab, inputFieldPrefab, particleMaterial);
        try
        {
            if (context.MeasuresMemory)
            {
                yield return RunMemoryLaunch(context, rig, cases);
                yield break;
            }

            var startup = context.Selects(UniTextScenarios.StartupId);
            var selected = new List<ScenarioCase>();
            foreach (var scenarioCase in cases)
                if (context.Selects(scenarioCase.Id))
                    selected.Add(scenarioCase);
            if (!startup && selected.Count == 0)
                throw new InvalidOperationException($"No scenario case answers to '{context.Participant}'.");
            PublishConfig(context, startup, selected);

            if (startup)
            {
                var record = results[UniTextScenarios.StartupId] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {UniTextScenarios.StartupId}",
                    () => BenchmarkRecord.Guard(MeasureStartup(context, rig, record), record), null);
                if (!context.Alive) yield break;
            }

            foreach (var scenarioCase in selected)
            {
                var record = results[scenarioCase.Id] = new JObject { ["status"] = "measuring" };
                yield return context.Run($"scenario {scenarioCase.Id}",
                    () => BenchmarkRecord.Guard(MeasureCase(context, rig, scenarioCase, record), record), null);
                if (!context.Alive) yield break;
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

    void PublishConfig(BenchmarkContext context, bool startup, List<ScenarioCase> cases)
    {
        var ids = new JArray();
        if (startup) ids.Add(UniTextScenarios.StartupId);
        foreach (var scenarioCase in cases)
            ids.Add(scenarioCase.Id);
        context.Config["scenarios"] = new JObject
        {
            ["rounds"] = rounds,
            ["settleFrames"] = settleFrames,
            ["slotFrames"] = slotFrames,
            ["maxWarmupFrames"] = maxWarmupFrames,
            ["setupFrames"] = SetupFrames,
            ["referenceResolution"] = $"{ScenarioRig.ReferenceWidth}x{ScenarioRig.ReferenceHeight}",
            ["fontStack"] = fontStack.name,
            ["tmpFont"] = tmpFont.name,
            ["tmpMaterial"] = tmpMaterial.name,
            ["cases"] = ids
        };
    }

    BenchmarkRoundSettings Settings(string caseId, int repeat) => new()
    {
        seed = BenchmarkRounds.Seed(caseId, repeat),
        rounds = rounds,
        settleFrames = settleFrames,
        measuredFrames = slotFrames,
        maxWarmupFrames = maxWarmupFrames,
        maxCommitFrames = StartupCommitFrames,
        includeSamples = includeSamples,
        coldResult = caseId == UniTextScenarios.StartupId
    };

    IEnumerator MeasureCase(BenchmarkContext context, ScenarioRig rig, ScenarioCase scenarioCase, JObject record)
    {
        yield return context.ThermalSettle();
        var built = new List<UniTextScenario>(scenarioCase.Arms.Count);
        var arms = new List<ScenarioArm>(scenarioCase.Arms.Count);
        try
        {
            var setup = new JObject();
            record["setup"] = setup;
            foreach (var scenario in scenarioCase.Arms)
            {
                UniText.UseParallel = scenario.Parallel;
                var started = Stopwatch.GetTimestamp();
                built.Add(scenario);
                scenario.Setup(rig);
                var setupMs = Round(Milliseconds(started));
                var frames = new JArray();
                for (var i = 0; i < SetupFrames; i++)
                {
                    scenario.Step(SetupFrameBase + i);
                    yield return null;
                    frames.Add(Round(BenchmarkFrameProbe.LastFrameMilliseconds));
                }
                scenario.Validate();
                scenario.SetActive(false);
                setup[scenario.Id] = new JObject { ["setupMs"] = setupMs, ["setupFrameMs"] = frames };
                arms.Add(new ScenarioArm(scenario));
            }

            var caseArms = new List<IBenchmarkArm>(arms);
            yield return BenchmarkRounds.RunFrames(scenarioCase.Id, caseArms, Settings(scenarioCase.Id, context.Repeat), record);

            var workload = new JObject();
            foreach (var arm in arms)
            {
                arm.Activate();
                yield return null;
                var description = new JObject { ["parallel"] = arm.Scenario.Parallel };
                arm.Scenario.Describe(description);
                workload[arm.Id] = description;
                arm.Deactivate();
            }
            record["workload"] = workload;
            record["status"] = "measured";

            foreach (var arm in arms)
            {
                arm.Activate();
                arm.Scenario.Step(StillFrame);
                for (var i = 0; i < StillSettleFrames; i++)
                    yield return null;
                yield return BenchmarkScreenshot.Capture($"scenario-{arm.Id}");
                if (captureProfile) yield return CaptureProfile(arm.Scenario);
                arm.Deactivate();
            }
        }
        finally
        {
            var started = Stopwatch.GetTimestamp();
            foreach (var scenario in built)
            {
                scenario.Teardown();
                scenario.StopCountingCommits();
            }
            rig.Clear();
            record["teardownMs"] = Round(Milliseconds(started));
            UniText.UseParallel = true;
        }
    }

    /// <summary>Measures one arm, the memory cycles or the hidden-label memory alone, with the process memory sampled.</summary>
    IEnumerator RunMemoryLaunch(BenchmarkContext context, ScenarioRig rig, List<ScenarioCase> cases)
    {
        var participant = context.Participant
                          ?? throw new InvalidOperationException("A memory launch measures one participant: name it with -benchmarkParticipant.");
        context.Config["scenarios"] = new JObject
        {
            ["memoryFrames"] = MemoryFrames,
            ["maxWarmupFrames"] = maxWarmupFrames,
            ["setupFrames"] = SetupFrames,
            ["memoryCycles"] = MemoryCycles,
            ["referenceResolution"] = $"{ScenarioRig.ReferenceWidth}x{ScenarioRig.ReferenceHeight}",
            ["fontStack"] = fontStack.name,
            ["participant"] = participant
        };
        var record = results[participant] = new JObject { ["status"] = "measuring" };
        if (string.Equals(participant, UniTextScenarios.MemoryId, StringComparison.OrdinalIgnoreCase))
        {
            yield return context.Run($"scenario {participant}",
                () => BenchmarkRecord.Guard(MeasureMemoryCycles(context, rig, record), record), null);
            yield break;
        }
        if (string.Equals(participant, BenchmarkRounds.ReferenceArmId, StringComparison.OrdinalIgnoreCase))
        {
            yield return context.Run($"scenario {participant}",
                () => BenchmarkRecord.Guard(MeasureArmMemory(context, rig, null, null, record), record), null);
            yield break;
        }
        if (string.Equals(participant, UniTextScenarios.HiddenMemoryId, StringComparison.OrdinalIgnoreCase))
        {
            yield return context.Run($"scenario {participant}",
                () => BenchmarkRecord.Guard(MeasureHiddenMemory(context, rig, record), record), null);
            yield break;
        }
        foreach (var scenarioCase in cases)
        foreach (var scenario in scenarioCase.Arms)
        {
            if (!string.Equals(scenario.Id, participant, StringComparison.OrdinalIgnoreCase)) continue;
            var caseId = scenarioCase.Id;
            yield return context.Run($"scenario {participant}",
                () => BenchmarkRecord.Guard(MeasureArmMemory(context, rig, caseId, scenario, record), record), null);
            yield break;
        }
        throw new InvalidOperationException($"No scenario arm answers to memory participant '{participant}'.");
    }

    /// <summary>
    /// Measures the memory of <paramref name="scenario"/>, an arm of <paramref name="caseId"/>, alone; with null the empty
    /// scene the arms are drawn into: the floor.
    /// </summary>
    IEnumerator MeasureArmMemory(BenchmarkContext context, ScenarioRig rig, string caseId, UniTextScenario scenario,
        JObject record)
    {
        var info = new JObject();
        if (scenario != null)
        {
            scenario.DescribeArm(info);
            info["control"] = JValue.CreateNull();
        }
        else info["control"] = BenchmarkRounds.ReferenceArmId;
        record["memoryCase"] = caseId;
        record["armInfo"] = info;
        yield return context.ThermalSettle();
        UniText.UseParallel = scenario?.Parallel ?? true;
        var memory = scenario is { MeasuresFootprint: true } ? new BenchmarkMemoryCounters() : null;
        try
        {
            JObject before = null;
            if (memory != null)
            {
                yield return BenchmarkMemoryCounters.Settle();
                before = memory.Read();
            }
            var setupPeak = BenchmarkProcessMemory.StartPeak();
            var started = Stopwatch.GetTimestamp();
            scenario?.Setup(rig);
            record["setupMs"] = Round(Milliseconds(started));
            var frame = 0;
            for (var i = 0; i < SetupFrames; i++)
            {
                scenario?.Step(frame++);
                yield return null;
            }
            var setupPeakRecord = BenchmarkProcessMemory.PeakRecord(setupPeak);
            if (setupPeakRecord != null) record["setupPeak"] = setupPeakRecord;
            if (memory != null)
            {
                yield return BenchmarkMemoryCounters.Settle();
                var after = memory.Read();
                var footprint = new JObject();
                foreach (var pair in after) footprint[pair.Key] = (long)pair.Value - (long)before[pair.Key];
                record["footprint"] = footprint;
            }
            scenario?.Validate();

            for (var i = 0; i < maxWarmupFrames; i++)
            {
                scenario?.Step(frame++);
                yield return null;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (var sampler = new BenchmarkFrameSampler(MemoryFrames, scenario?.Markers ?? Array.Empty<string>()))
            {
                scenario?.ResetCommits();
                scenario?.MeasuringStarts();
                for (var i = 0; i < MemoryFrames; i++)
                {
                    scenario?.Step(frame++);
                    yield return null;
                    sampler.Sample();
                }
                record["metrics"] = sampler.Serialize(false);
            }
            var workload = new JObject();
            if (scenario != null)
            {
                scenario.Validate();
                scenario.RequireCommits(MemoryFrames);
                workload["parallel"] = scenario.Parallel;
                scenario.Describe(workload);
            }
            else workload["texts"] = 0;
            record["workload"] = workload;
            record["status"] = "measured";
        }
        finally
        {
            scenario?.Teardown();
            scenario?.StopCountingCommits();
            rig.Clear();
            UniText.UseParallel = true;
            memory?.Dispose();
        }
    }

    /// <summary>
    /// Opens the first screen of text with each engine after a uGUI screen of images, in one-shot rounds: round 0 is
    /// each engine's first use in the process, later rounds open the same screen warm.
    /// </summary>
    IEnumerator MeasureStartup(BenchmarkContext context, ScenarioRig rig, JObject record)
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
        var arms = new List<StartupArm> { new(rig, ScenarioEngine.UniText), new(rig, ScenarioEngine.Tmp) };
        try
        {
            var oneShotArms = new List<IBenchmarkOneShotArm>(arms);
            yield return BenchmarkRounds.RunOneShots(UniTextScenarios.StartupId, oneShotArms,
                Settings(UniTextScenarios.StartupId, context.Repeat), record);
            var workload = new JObject();
            foreach (var arm in arms)
                workload[arm.Id] = arm.Workload;
            record["workload"] = workload;
            record["status"] = "measured";
        }
        finally
        {
            rig.Clear();
        }
    }

    /// <summary>Steps <paramref name="scenario"/> through as many untimed frames as a slot measures under a Prof capture and ships the capture.</summary>
    IEnumerator CaptureProfile(UniTextScenario scenario)
    {
        ProfCapture capture;
        try
        {
            ProfCounters.Arm();
            Prof.BeginCapture();
            for (var i = 0; i < slotFrames; i++)
            {
                scenario.Step(ProfileFrameBase + i);
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

    IEnumerator MeasureMemoryCycles(BenchmarkContext context, ScenarioRig rig, JObject record)
    {
        record["memoryCase"] = UniTextScenarios.MemoryId;
        record["armInfo"] = UniTextArmInfo(72, "cycles");
        yield return context.ThermalSettle();
        var paragraphs = UniTextScenarios.Paragraphs(UniTextScenarios.LatinLines, 4, 24);
        var labels = UniTextScenarios.Labels(48);
        using var memory = new BenchmarkMemoryCounters();
        try
        {
            var empty = new JObject[MemoryCycles + 1];
            yield return RunMemoryCycles(rig, memory, empty, null);
            var texts = new JObject[MemoryCycles + 1];
            yield return RunMemoryCycles(rig, memory, texts, root =>
            {
                UniTextScenarios.Grid(rig, root, paragraphs, 4, 6, 20f, 0.5f);
                UniTextScenarios.Grid(rig, root, labels, 4, 12, 24f, 0.5f, 0.5f);
            });

            record["workload"] = new JObject
            {
                ["cycles"] = MemoryCycles,
                ["warmupCycles"] = MemoryWarmupCycles,
                ["paragraphsPerCycle"] = 24,
                ["labelsPerCycle"] = 48
            };
            record["snapshots"] = new JArray(texts);
            record["growthPerCycleBytes"] = GrowthPerCycle(texts);
            record["emptyGrowthPerCycleBytes"] = GrowthPerCycle(empty);
            record["status"] = "measured";
        }
        finally
        {
            rig.Clear();
        }
    }

    /// <summary>Reads <paramref name="snapshots"/> before the first and after every cycle of a screen that <paramref name="fill"/> fills; null fills nothing.</summary>
    static IEnumerator RunMemoryCycles(ScenarioRig rig, BenchmarkMemoryCounters memory, JObject[] snapshots,
        Action<RectTransform> fill)
    {
        yield return BenchmarkMemoryCounters.Settle();
        snapshots[0] = memory.Read();
        for (var cycle = 1; cycle < snapshots.Length; cycle++)
        {
            var root = rig.CreateRoot("MemoryCycle");
            fill?.Invoke(root);
            for (var i = 0; i < SetupFrames; i++)
                yield return null;
            if (cycle == 1 && fill != null) UniTextScenarios.RequireGlyphs(root);
            rig.Clear();
            for (var i = 0; i < SetupFrames; i++)
                yield return null;
            yield return BenchmarkMemoryCounters.Settle();
            snapshots[cycle] = memory.Read();
        }
    }

    static JObject GrowthPerCycle(JObject[] snapshots)
    {
        var growth = new JObject();
        foreach (var pair in snapshots[MemoryCycles])
            growth[pair.Key] = Math.Round(((long)pair.Value - (long)snapshots[MemoryWarmupCycles][pair.Key]) /
                                          (double)(MemoryCycles - MemoryWarmupCycles));
        return growth;
    }

    const int HiddenLabels = 200;
    const float HiddenWaitSeconds = 22f;

    IEnumerator MeasureHiddenMemory(BenchmarkContext context, ScenarioRig rig, JObject record)
    {
        record["memoryCase"] = UniTextScenarios.HiddenMemoryId;
        record["armInfo"] = UniTextArmInfo(HiddenLabels, "hidden");
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
                for (var i = 0; i < SetupFrames; i++)
                    yield return null;
                UniTextScenarios.RequireGlyphs(root);
                foreach (UniTextBase text in texts) glyphs += text.GlyphCount;
                var renderers = root.GetComponentsInChildren<CanvasRenderer>(true).Length;
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

    /// <summary>The descriptor of a memory-only workload drawn by UniText's default configuration.</summary>
    static JObject UniTextArmInfo(int load, string variant) => new()
    {
        ["engine"] = "unitext",
        ["threads"] = "parallel",
        ["style"] = UniTextBenchmarkEnvironment.Plain ? "plain" : "styled",
        ["load"] = load,
        ["baseline"] = false,
        ["variant"] = variant,
        ["control"] = JValue.CreateNull()
    };

    static string StartupKey(ScenarioEngine engine) =>
        $"{UniTextScenarios.StartupId}.{(engine == ScenarioEngine.Tmp ? "tmp" : "unitext")}";

    static double Milliseconds(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000d / Stopwatch.Frequency;

    static double Round(double value) => Math.Round(value, 3);

    /// <summary>A scenario as an arm of its case: <see cref="UniTextScenario.SetActive"/> shows and hides it, its parallel mode is set on activation.</summary>
    sealed class ScenarioArm : IBenchmarkArm
    {
        public ScenarioArm(UniTextScenario scenario) => Scenario = scenario;

        public UniTextScenario Scenario { get; }

        public string Id => Scenario.Id;

        public string[] Markers => Scenario.Markers;

        public void Activate()
        {
            UniText.UseParallel = Scenario.Parallel;
            Scenario.SetActive(true);
        }

        public void Deactivate() => Scenario.SetActive(false);

        public void Step(int frame) => Scenario.Step(frame);

        public void SlotStarts()
        {
            Scenario.ResetCommits();
            Scenario.MeasuringStarts();
        }

        public void SlotEnds(int measuredFrames)
        {
            Scenario.RequireCommits(measuredFrames);
            Scenario.Validate();
        }

        public void Describe(JObject arm) => Scenario.DescribeArm(arm);
    }

    /// <summary>One engine's first screen of shop labels as a one-shot arm: created by the measured call, cleared by the reset.</summary>
    sealed class StartupArm : IBenchmarkOneShotArm
    {
        readonly ScenarioRig rig;
        readonly ScenarioEngine engine;
        RectTransform root;
        MaskableGraphic[] labels;
        bool captured;

        public StartupArm(ScenarioRig rig, ScenarioEngine engine)
        {
            this.rig = rig;
            this.engine = engine;
            Id = StartupKey(engine);
        }

        public string Id { get; }

        /// <summary>Texts and glyphs of the screen, read after the first round that committed it.</summary>
        public JObject Workload { get; } = new();

        public IEnumerator Prepare()
        {
            yield break;
        }

        public void Operate()
        {
            root = rig.CreateRoot("First screen");
            labels = UniTextScenarios.FillPage(rig, engine, root, 0);
        }

        public bool Committed
        {
            get
            {
                foreach (var label in labels)
                    if (Glyphs(label) == 0) return false;
                return true;
            }
        }

        public IEnumerator Reset()
        {
            UniTextScenarios.RequireGlyphs(root);
            Workload["texts"] = labels.Length;
            Workload["glyphs"] = UniTextScenarios.CountGlyphs(root);
            if (!captured)
            {
                captured = true;
                yield return BenchmarkScreenshot.Capture($"scenario-{Id}");
            }
            rig.Clear();
            root = null;
            labels = null;
            for (var i = 0; i < SetupFrames; i++)
                yield return null;
        }

        public void Describe(JObject arm)
        {
            arm["engine"] = engine == ScenarioEngine.Tmp ? "tmp" : "unitext";
            arm["threads"] = engine == ScenarioEngine.Tmp ? JValue.CreateNull() : new JValue("parallel");
            arm["style"] = UniTextBenchmarkEnvironment.Plain ? "plain" : "styled";
            arm["load"] = Workload["texts"];
            arm["baseline"] = false;
        }

        static int Glyphs(MaskableGraphic label) =>
            label is UniTextBase uniText ? uniText.GlyphCount : ((TMP_Text)label).textInfo.characterCount;
    }
}
