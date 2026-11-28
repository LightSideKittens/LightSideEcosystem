using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Engine-agnostic half of the text harness: the session an engine's texts live in, the phase steps the suite's arms
/// drive, the memory run of one engine, result structs and inspection. The instance-typed loops live in
/// <see cref="TextBenchmarkBase{TInstance}"/>.
/// </summary>
/// <remarks>
/// A session holds one engine's <see cref="BenchmarkConfig.objectCount"/> texts at a time, text <c>i</c> on page
/// <c>i</c> (<see cref="TextPhases.PageStride"/>), so the CPU lays out every text and the GPU draws one page. Arms build
/// their texts when they activate and destroy them when they deactivate, so engines sharing a process never hold their
/// workloads at once.
/// </remarks>
public abstract class TextBenchmarkBase : MonoBehaviour
{
    [Header("Profiling")]
    [Tooltip("Runs a separate untimed copy of each phase of a memory run under a Prof capture and ships prof_{engine}_{phase}.txt/.json to Benchmarks/prof/{runId}/ (device: persistentDataPath). UniText needs UNITEXT_PROFILE for a deep tree; other engines report totals only.")]
    public bool captureProfile;

    [Header("Inspection")]
    [SerializeField] protected TextPhase inspectionPhase = TextPhase.FullRebuildUnique;
    [SerializeField] protected int inspectionFrame;
    [SerializeField] protected bool inspectionLatinCorpus;
    [SerializeField, Min(0)] protected int inspectionSampleCount = 5;
    [SerializeField, TextArea(8, 24)] protected string inspectionReport = "";

    [Header("Status")]
    [SerializeField] protected bool isRunning;
    [SerializeField] protected string currentTest = "";

    protected readonly WaitForEndOfFrame waitForEndOfFrame = new();
    protected bool inspectionActive;

    MemorySampler memorySampler;
    bool sessionOpen;

    /// <summary>The corpus of the open session.</summary>
    public TextCorpus Corpus { get; private set; }

    public bool IsRunning => isRunning;
    public string InspectionReport => inspectionReport;

    /// <summary>The engine configurations this component measures, as result and arm ids.</summary>
    public abstract IReadOnlyList<string> EngineIds { get; }

    /// <summary>Engine name for logs.</summary>
    public abstract string SystemName { get; }

    protected static BenchmarkConfig Config =>
        BenchmarkConfig.Instance != null
            ? BenchmarkConfig.Instance
            : throw new InvalidOperationException("The text benchmark needs a BenchmarkConfig in the scene.");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        Application.targetFrameRate = 1000;
    }

    #region Data Structures

    [Serializable]
    public struct MemorySnapshot
    {
        public long resident;
        public long used;
        public long reserved;
        public long gcUsed;
        public long gcReserved;
        public long buffers;
        public long dirty;
        public long clean;
        public long pss;

        internal static MemorySnapshot Max(MemorySnapshot a, MemorySnapshot b) => new()
        {
            resident = MaxValue(a.resident, b.resident),
            used = MaxValue(a.used, b.used),
            reserved = MaxValue(a.reserved, b.reserved),
            gcUsed = MaxValue(a.gcUsed, b.gcUsed),
            gcReserved = MaxValue(a.gcReserved, b.gcReserved),
            buffers = MaxValue(a.buffers, b.buffers),
            dirty = MaxValue(a.dirty, b.dirty),
            clean = MaxValue(a.clean, b.clean),
            pss = MaxValue(a.pss, b.pss)
        };

        internal static MemorySnapshot Invalid() => new()
        {
            resident = -1,
            used = -1,
            reserved = -1,
            gcUsed = -1,
            gcReserved = -1,
            buffers = -1,
            dirty = -1,
            clean = -1,
            pss = -1
        };

        static long MaxValue(long a, long b)
        {
            if (a < 0) return b;
            if (b < 0) return a;
            return Math.Max(a, b);
        }
    }

    [Serializable]
    public struct MemoryCycle
    {
        public MemorySnapshot before;
        public MemorySnapshot peak;
        public MemorySnapshot end;
        public MemorySnapshot afterCollect;
        public long managedAlloc;
    }

    [Serializable]
    public struct PhaseMemoryMetrics
    {
        public bool available;
        public MemorySnapshot beforeWarmup;
        public MemorySnapshot normalizedBaseline;
        public MemorySnapshot afterWarmup;
        public MemorySnapshot measuredPeak;
        public MemorySnapshot measuredEnd;
        public MemorySnapshot normalizedEnd;
        public MemorySnapshot afterMeasured;
        public MemorySnapshot beforeProbes;
        public List<MemoryCycle> probes;

        /// <summary>The memory the phase's peak window opened at (<see cref="BenchmarkProcessMemory.StartPeak"/>), -1 outside memory launches.</summary>
        public long processPeakStart;

        /// <summary>The phase's peak window over its stepped frames (<see cref="BenchmarkProcessMemory.PeakRecord"/>), null outside memory launches.</summary>
        public JObject processPeak;

        internal static PhaseMemoryMetrics Create() => new()
        {
            processPeakStart = -1,
            beforeWarmup = MemorySnapshot.Invalid(),
            normalizedBaseline = MemorySnapshot.Invalid(),
            afterWarmup = MemorySnapshot.Invalid(),
            measuredPeak = MemorySnapshot.Invalid(),
            measuredEnd = MemorySnapshot.Invalid(),
            normalizedEnd = MemorySnapshot.Invalid(),
            afterMeasured = MemorySnapshot.Invalid(),
            beforeProbes = MemorySnapshot.Invalid(),
            probes = new List<MemoryCycle>()
        };
    }

    [Serializable]
    public struct RunMemoryMetrics
    {
        public bool available;
        public MemorySnapshot baseline;
        public MemorySnapshot peak;
        public MemorySnapshot beforeCleanup;
        /// <summary>The system's read of the process memory with its GPU part as the cleanup starts, the engine's texts alive; null unless the process reads its footprint (<see cref="BenchmarkProcessMemory.SystemFootprint"/>).</summary>
        public JObject systemFootprint;
        public MemorySnapshot afterCleanup;
        public MemorySnapshot afterRetention;
    }

    /// <summary>One phase of a memory run: its memory, and how often the texts reprocessed over its stepped frames.</summary>
    public struct PhaseResult
    {
        public PhaseMemoryMetrics memory;
        public int frames;
        public long commits;
        public long expectedCommits;
    }

    /// <summary>A memory run of one engine over one corpus: every phase in order, creation first, and the run's memory.</summary>
    public sealed class TestResults
    {
        public readonly List<KeyValuePair<string, PhaseResult>> phases = new();
        public RunMemoryMetrics memory;
    }

    sealed class MemorySampler : IDisposable
    {
        ProfilerRecorder resident;
        ProfilerRecorder used;
        ProfilerRecorder reserved;
        ProfilerRecorder gcUsed;
        ProfilerRecorder gcReserved;
        ProfilerRecorder buffers;
        readonly BenchmarkFrameAllocation frameAllocation = new();

        public MemorySnapshot Peak { get; private set; }
        public long LastFrameAllocation => frameAllocation.LastFrameBytes;
        public bool AllocationAvailable => frameAllocation.Available;

        public MemorySampler()
        {
            resident = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
            used = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            reserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory", 1);
            gcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory", 1);
            gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory", 1);
            buffers = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Used Buffers Bytes", 1);
            Peak = MemorySnapshot.Invalid();
        }

        public MemorySnapshot Read()
        {
            var snapshot = new MemorySnapshot
            {
                resident = Value(resident),
                used = Value(used),
                reserved = Value(reserved),
                gcUsed = Value(gcUsed),
                gcReserved = Value(gcReserved),
                buffers = Value(buffers),
                dirty = -1,
                clean = -1,
                pss = -1
            };
            Peak = MemorySnapshot.Max(Peak, snapshot);
            return snapshot;
        }

        public void Dispose()
        {
            resident.Dispose();
            used.Dispose();
            reserved.Dispose();
            gcUsed.Dispose();
            gcReserved.Dispose();
            buffers.Dispose();
            frameAllocation.Dispose();
        }

        static long Value(ProfilerRecorder recorder) => recorder.Valid ? recorder.CurrentValue : -1;
    }

    #endregion

    #region Engine hooks

    /// <summary>Sets the process-wide modes of <paramref name="engineId"/> (threading); the session stays open.</summary>
    public abstract void SelectEngine(string engineId);

    protected abstract void OnBeginSession();
    protected abstract void OnEndSession();
    protected abstract void SetupContainer();
    protected abstract void TeardownContainer();

    /// <summary>Whether the session's texts are drawn; hidden texts that do not change cost nothing per frame.</summary>
    public abstract bool Visible { set; }

    /// <summary>Creates the session's texts on their pages with <paramref name="text"/>, rich text off, the start state of every phase.</summary>
    public abstract void CreateAll(string text);

    /// <summary>Destroys the session's texts.</summary>
    public abstract void DestroyAll();

    /// <summary>Whether the session holds texts.</summary>
    public abstract bool HasTexts { get; }

    /// <summary>Puts the session's texts into <paramref name="phase"/>'s start state: wrap, auto-size, rich text, rect, color and start text.</summary>
    public abstract void ApplyPhase(TextPhase phase);

    /// <summary>Applies frame <paramref name="frame"/> of <paramref name="phase"/>; <paramref name="inputs"/> are the arm's texts for text phases.</summary>
    public abstract void Step(TextPhase phase, string[] inputs, int frame);

    /// <summary>Texts reprocessed since the session opened; -1 where the engine reports none.</summary>
    public abstract long Commits { get; }

    /// <summary>Whether every text created after <paramref name="commitsBefore"/> was counted has been processed by the engine.</summary>
    public abstract bool AllProcessed(long commitsBefore);

    /// <summary>Lets the engine prepare per-text derived data of <paramref name="inputs"/> before they are stepped.</summary>
    public virtual void PrepareInputs(string[] inputs) { }

    /// <summary>Chooses how the next <see cref="CreateAll"/> builds a text for <paramref name="phase"/>, for engines whose idiomatic structure depends on it.</summary>
    public virtual void SetStructure(TextPhase phase) { }

    protected virtual bool ValidateSetup() => true;
    protected abstract IEnumerator RunInspection();
    protected abstract void ClearInspectionObjects();

    #endregion

    #region Session

    /// <summary>Opens a session over <paramref name="corpus"/>: the engine's fonts and container exist, no text yet.</summary>
    public void BeginSession(TextCorpus corpus)
    {
        if (sessionOpen) throw new InvalidOperationException($"{SystemName} already has an open session.");
        if (!ValidateSetup()) throw new InvalidOperationException($"{SystemName} is not set up for the text benchmark.");
        ClearInspectionState();
        Corpus = corpus;
        OnBeginSession();
        SetupContainer();
        sessionOpen = true;
    }

    /// <summary>Destroys the session's texts and container and restores the engine's process-wide modes.</summary>
    public void EndSession()
    {
        if (!sessionOpen) return;
        sessionOpen = false;
        try
        {
            if (HasTexts) DestroyAll();
            TeardownContainer();
        }
        finally
        {
            OnEndSession();
        }
    }

    #endregion

    #region Memory run

    /// <summary>
    /// The memory launch of <paramref name="engineId"/> over <paramref name="corpus"/>: settled checkpoints and peak
    /// windows around repeated creation and every phase's stepped frames, then cleanup and retention. Timing is not
    /// recorded: the memory sampling threads of the launch compete with the measured work.
    /// </summary>
    public IEnumerator RunMemoryCoroutine(string engineId, TextCorpus corpus, TestResults results)
    {
        BenchmarkRun.Begin();
        isRunning = true;
        memorySampler?.Dispose();
        ProfCounters.Release();
        Prof.ReleaseCaptureBuffers();
        memorySampler = new MemorySampler();
        var config = Config;
        var completed = false;
        try
        {
            yield return CollectAndSettle();
            results.memory.baseline = ReadSettledMemory();
            results.memory.available = MemoryAvailable(results.memory.baseline);

            BeginSession(corpus);
            SelectEngine(engineId);
            Visible = true;
            LogMemory(engineId, "Start");

            currentTest = $"{engineId}: creation";
            SetStructure(TextPhase.Idle);
            var creation = new PhaseResult { memory = PhaseMemoryMetrics.Create(), commits = -1, expectedCommits = -1 };
            yield return CollectAndSettle();
            creation.memory.beforeWarmup = ReadSettledMemory();
            creation.memory.normalizedBaseline = creation.memory.beforeWarmup;
            creation.memory.available = MemoryAvailable(creation.memory.beforeWarmup);
            creation.memory.processPeakStart = BenchmarkProcessMemory.StartPeak();
            creation.memory.measuredPeak = creation.memory.beforeWarmup;
            for (var cycle = 0; cycle < config.textRounds; cycle++)
            {
                var before = Commits;
                CreateAll(TextPhases.PlainText(corpus));
                var frames = 0;
                do
                {
                    yield return null;
                    creation.memory.measuredPeak = MemorySnapshot.Max(creation.memory.measuredPeak, ReadMemory());
                    if (++frames > config.maxCommitFrames)
                        throw new InvalidOperationException($"{engineId} texts did not process within {config.maxCommitFrames} frames.");
                } while (!AllProcessed(before));
                creation.frames = Math.Max(creation.frames, frames);
                DestroyAll();
                yield return null;
                creation.memory.measuredPeak = MemorySnapshot.Max(creation.memory.measuredPeak, ReadMemory());
            }
            creation.memory.measuredEnd = ReadMemory();
            creation.memory.normalizedEnd = creation.memory.measuredEnd;
            creation.memory.processPeak = BenchmarkProcessMemory.PeakRecord(creation.memory.processPeakStart);
            yield return CollectAndSettle();
            creation.memory.afterMeasured = ReadSettledMemory();
            creation.memory.beforeProbes = creation.memory.afterMeasured;
            results.phases.Add(new KeyValuePair<string, PhaseResult>("creation", creation));
            ShipPhaseMemoryProfile(engineId, "creation", "measured", creation.memory);

            var armCode = ArmCode(engineId);
            foreach (var phase in TextPhases.All)
            {
                if (!TextPhases.Applies(phase, corpus)) continue;
                currentTest = $"{engineId}: {TextPhases.Key(phase)}";
                var inputs = TextPhases.Inputs(phase, corpus, armCode, config.TextFramesPerArm);
                PrepareInputs(inputs);
                if (HasTexts) DestroyAll();
                SetStructure(phase);
                var commitsBeforeBuild = Commits;
                CreateAll(TextPhases.PlainText(corpus));
                yield return WaitProcessed(engineId, commitsBeforeBuild);
                var result = new PhaseResult();
                yield return RunMemoryPhase(engineId, phase, inputs, config, probe => result = probe);
                results.phases.Add(new KeyValuePair<string, PhaseResult>(TextPhases.Key(phase), result));
            }

            yield return CollectAndSettle();
            results.memory.beforeCleanup = ReadSettledMemory();
            results.memory.systemFootprint = BenchmarkProcessMemory.SystemFootprint();
            EndSession();
            yield return CollectAndSettle();
            results.memory.afterCleanup = ReadSettledMemory();
            yield return WaitOutPoolRetention();
            results.memory.afterRetention = ReadSettledMemory();
            results.memory.peak = memorySampler.Peak;
            LogMemory(engineId, "Cleanup");
            ShipRunMemoryProfile(engineId, results.memory);
            completed = true;
            currentTest = "Complete";
        }
        finally
        {
            if (!completed)
            {
                try { EndSession(); }
                catch (Exception e) { Debug.LogException(e); }
                currentTest = "Failed";
            }
            if (captureProfile)
            {
                if (Prof.Capturing) Prof.EndCapture();
                ProfCounters.Release();
                Prof.ReleaseCaptureBuffers();
            }
            memorySampler?.Dispose();
            memorySampler = null;
            isRunning = false;
        }
    }

    IEnumerator RunMemoryPhase(string engineId, TextPhase phase, string[] inputs, BenchmarkConfig config,
        Action<PhaseResult> commit)
    {
        var key = TextPhases.Key(phase);
        var result = new PhaseResult { memory = PhaseMemoryMetrics.Create() };
        var probeCycles = ProbeCycles(config.memoryProbeRepeats);
        ApplyPhase(phase);
        yield return null;
        yield return CollectAndSettle();
        result.memory.beforeWarmup = ReadSettledMemory();
        result.memory.normalizedBaseline = result.memory.beforeWarmup;
        result.memory.available = MemoryAvailable(result.memory.beforeWarmup);
        result.memory.processPeakStart = BenchmarkProcessMemory.StartPeak();
        var frame = 0;
        for (var i = 0; i < config.textSettleFrames; i++)
        {
            Step(phase, inputs, frame++);
            yield return null;
            ReadMemory();
        }
        result.memory.afterWarmup = ReadMemory();
        result.memory.measuredPeak = result.memory.afterWarmup;
        var commitsBefore = Commits;
        for (var i = 0; i < config.textMeasuredFrames; i++)
        {
            Step(phase, inputs, frame++);
            yield return null;
            result.memory.measuredPeak = MemorySnapshot.Max(result.memory.measuredPeak, ReadMemory());
        }
        result.frames = config.textMeasuredFrames;
        result.commits = Commits >= 0 ? Commits - commitsBefore : -1;
        result.expectedCommits = ExpectedCommits(phase, config.textMeasuredFrames);
        result.memory.measuredEnd = ReadMemory();
        result.memory.normalizedEnd = result.memory.measuredEnd;
        result.memory.processPeak = BenchmarkProcessMemory.PeakRecord(result.memory.processPeakStart);
        yield return CollectAndSettle();
        result.memory.afterMeasured = ReadSettledMemory();
        result.memory.beforeProbes = result.memory.afterMeasured;
        RequireCommits(engineId, phase, result.commits, config.textMeasuredFrames);
        commit(result);
        ShipPhaseMemoryProfile(engineId, key, "measured", result.memory);

        var settled = result.memory.beforeProbes;
        for (var repeat = 0; repeat < probeCycles; repeat++)
        {
            var cycle = new MemoryCycle
            {
                before = settled,
                peak = settled,
                managedAlloc = memorySampler.AllocationAvailable ? 0 : -1
            };
            for (var i = 0; i < config.textMeasuredFrames; i++)
            {
                Step(phase, inputs, frame++);
                yield return null;
                if (cycle.managedAlloc >= 0) cycle.managedAlloc += memorySampler.LastFrameAllocation;
                cycle.peak = MemorySnapshot.Max(cycle.peak, ReadMemory());
            }
            cycle.end = ReadMemory();
            yield return CollectAndSettle();
            cycle.afterCollect = ReadSettledMemory();
            settled = cycle.afterCollect;
            result.memory.probes.Add(cycle);
            commit(result);
            ShipPhaseMemoryProfile(engineId, key, $"probes{result.memory.probes.Count}", result.memory);
        }

        if (captureProfile) yield return CapturePhaseProfile(engineId, phase, inputs, frame, config.textMeasuredFrames);
    }

    IEnumerator CapturePhaseProfile(string engineId, TextPhase phase, string[] inputs, int frame, int frames)
    {
        ProfCapture capture;
        try
        {
            ProfCounters.Arm();
            Prof.BeginCapture();
            for (var i = 0; i < frames; i++)
            {
                Step(phase, inputs, frame + i);
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
            var tag = ProfileTag(engineId, TextPhases.Key(phase));
            ProfTransport.Ship(capture.ToText(), ProfilePath(tag, ".txt"));
            ProfTransport.Ship(capture.ToJson(), ProfilePath(tag, ".json"));
        }
        finally
        {
            ProfCounters.Release();
            Prof.ReleaseCaptureBuffers();
        }
        yield return CollectAndSettle();
    }

    IEnumerator WaitProcessed(string engineId, long commitsBefore)
    {
        var limit = Config.maxCommitFrames;
        for (var frames = 0; ; frames++)
        {
            yield return null;
            if (AllProcessed(commitsBefore)) yield break;
            if (frames >= limit)
                throw new InvalidOperationException($"{engineId} texts did not process within {limit} frames.");
        }
    }

    /// <summary>The token digit that keeps <paramref name="engineId"/>'s input texts apart from every other engine's of this component.</summary>
    public int ArmCode(string engineId)
    {
        for (var i = 0; i < EngineIds.Count; i++)
            if (EngineIds[i] == engineId)
                return i + 1;
        throw new ArgumentException($"{SystemName} measures no engine '{engineId}'.", nameof(engineId));
    }

    /// <summary>Writes the arm descriptor of <paramref name="engineId"/> over the open session's texts; TMP on the multilingual corpus, which it does not shape, is not comparable.</summary>
    public void Describe(string engineId, JObject arm) =>
        BenchmarkEngines.Describe(arm, engineId, !UniTextBenchmarkEnvironment.Plain, Config.objectCount,
            engineId != BenchmarkEngines.Tmp || Corpus != TextCorpus.Multilingual);

    /// <summary>The unique-variant workload BenchLab mirrors: every paragraph of <paramref name="corpus"/> gets a fresh token per variant.</summary>
    internal static string[] GenerateUniqueVariants(string corpus, int count) =>
        TextPhases.TokenVariants(corpus, count, 0, true);

    /// <summary>Reprocessings a phase must see over <paramref name="frames"/> frames: every text every frame, none while idle, -1 where the phase sets no requirement.</summary>
    public long ExpectedCommits(TextPhase phase, int frames) =>
        phase == TextPhase.Idle ? 0 : TextPhases.RequiresCommits(phase) ? (long)Config.objectCount * frames : -1;

    /// <summary>
    /// Throws when the engine reported reprocessing (<paramref name="commits"/> ≥ 0) that contradicts the phase: a changing
    /// phase in which a text was not reprocessed every frame, or an idle phase in which any text was.
    /// </summary>
    public void RequireCommits(string engineId, TextPhase phase, long commits, int frames)
    {
        if (commits < 0) return;
        var expected = ExpectedCommits(phase, frames);
        if (phase == TextPhase.Idle && commits != 0)
            throw new InvalidOperationException($"{engineId} reprocessed {commits} texts while idle.");
        if (expected > 0 && commits < expected)
            throw new InvalidOperationException($"{engineId} reprocessed {commits} of {expected} texts in {TextPhases.Key(phase)}.");
    }

    #endregion

    #region Memory

    /// <summary>
    /// Seconds a run waits after its cleanup before <see cref="RunMemoryMetrics.afterRetention"/>: longer than the
    /// engines keep idle pooled buffers, so the checkpoint shows what a run leaves behind rather than what it parks
    /// for reuse.
    /// </summary>
    const float PoolRetentionSeconds = 25f;

    IEnumerator WaitOutPoolRetention()
    {
        var until = Time.realtimeSinceStartupAsDouble + PoolRetentionSeconds;
        while (Time.realtimeSinceStartupAsDouble < until)
            yield return null;
        yield return CollectAndSettle();
        yield return Resources.UnloadUnusedAssets();
        yield return CollectAndSettle();
    }

    /// <summary>
    /// Finishes the frame in flight, collects garbage including what finalizers release, and lets one frame pass so
    /// native memory the collection freed is returned before a settled checkpoint reads it.
    /// </summary>
    protected IEnumerator CollectAndSettle()
    {
        yield return waitForEndOfFrame;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        yield return waitForEndOfFrame;
    }

    void LogMemory(string engineId, string phase)
    {
        var memory = ReadMemory();
        Debug.Log($"[BenchmarkMemory] {engineId}/{TextPhases.Key(Corpus)}/{phase}: " +
                  $"Resident={FormatCounter(memory.resident)}, Used={FormatCounter(memory.used)}, " +
                  $"Reserved={FormatCounter(memory.reserved)}, " +
                  $"GC={FormatCounter(memory.gcUsed)}/{FormatCounter(memory.gcReserved)}, " +
                  $"Buffers={FormatCounter(memory.buffers)}");
    }

    MemorySnapshot ReadMemory() => memorySampler != null ? memorySampler.Read() : MemorySnapshot.Invalid();

    /// <summary>
    /// <see cref="ReadMemory"/> plus the process's dirty, clean and proportional memory where the player reads them
    /// (<see cref="BenchmarkProcessMemory"/>). Reading them walks every mapping on Android, so it belongs right after
    /// <see cref="CollectAndSettle"/>, never inside a stepped frame.
    /// </summary>
    MemorySnapshot ReadSettledMemory()
    {
        var snapshot = ReadMemory();
        if (!BenchmarkProcessMemory.Available) return snapshot;
        var process = BenchmarkProcessMemory.Read();
        snapshot.dirty = process.dirty;
        snapshot.clean = process.clean;
        snapshot.pss = process.pss;
        return snapshot;
    }

    static string FormatCounter(long value) => value >= 0 ? FormatBytes(value) : "n/a";

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        return $"{bytes / (1024f * 1024f):F2} MB";
    }

    protected static bool MemoryAvailable(MemorySnapshot memory) =>
        memory.resident >= 0 || memory.used >= 0 || memory.gcUsed >= 0 || memory.buffers >= 0;

    /// <summary>Repeat probe cycles each memory phase runs for a configured repeat count: the full count in development builds, none in release players.</summary>
    public static int ProbeCycles(int configured) => Debug.isDebugBuild ? configured : 0;

    void ShipPhaseMemoryProfile(string engineId, string phase, string checkpoint, PhaseMemoryMetrics memory)
    {
        var tag = ProfileTag(engineId, phase);
        var run = BenchmarkRun.Id;
        var corpus = TextPhases.Key(Corpus);
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeMemoryProfile(engineId, corpus, phase, run, checkpoint, memory),
            ProfilePath(tag, $"_memory-{checkpoint}.json"));
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeMemoryProfileText(engineId, corpus, phase, run, checkpoint, memory),
            ProfilePath(tag, $"_memory-{checkpoint}.txt"));
    }

    void ShipRunMemoryProfile(string engineId, RunMemoryMetrics memory)
    {
        var tag = ProfileTag(engineId, "Run");
        var run = BenchmarkRun.Id;
        var corpus = TextPhases.Key(Corpus);
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeRunMemoryProfile(engineId, corpus, run, memory),
            ProfilePath(tag, "_memory.json"));
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeRunMemoryProfileText(engineId, corpus, run, memory),
            ProfilePath(tag, "_memory.txt"));
    }

    string ProfileTag(string engineId, string phase) =>
        $"{ProfilePart(engineId)}_{ProfilePart(phase)}_{ProfilePart(TextPhases.Key(Corpus))}";

    static string ProfilePath(string tag, string suffix)
    {
        var run = BenchmarkRun.Id;
        return $"prof/{run}/{run}_prof_{tag}{suffix}";
    }

    static string ProfilePart(string value) => string.IsNullOrEmpty(value)
        ? "unknown"
        : value.Replace(" ", "").Replace("/", "-").Replace("\\", "-").Replace(":", "-");

    #endregion

    #region Manual runs and inspection

    [ContextMenu("Run Memory Benchmark (multilingual)")]
    public void RunBenchmark()
    {
        if (isRunning)
        {
            Debug.LogWarning("Benchmark already running!");
            return;
        }
        StartCoroutine(RunManually());
    }

    IEnumerator RunManually()
    {
        foreach (var engineId in EngineIds)
        {
            var results = new TestResults();
            yield return RunMemoryCoroutine(engineId, TextCorpus.Multilingual, results);
            var record = new JObject();
            BenchmarkJsonSerializer.WriteTextMemory(record, results);
            Debug.Log(record.ToString());
        }
    }

    [ContextMenu("Inspect Selected Phase")]
    public void InspectSelectedPhase()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("Enter Play Mode to inspect benchmark runtime objects.");
            return;
        }
        if (isRunning)
        {
            Debug.LogWarning("Benchmark already running!");
            return;
        }
        if (!ValidateSetup()) return;
        StartCoroutine(RunInspection());
    }

    [ContextMenu("Clear Inspection")]
    public void ClearInspection()
    {
        if (isRunning)
        {
            Debug.LogWarning("Stop the benchmark before clearing inspection objects.");
            return;
        }
        ClearInspectionState();
    }

    protected void ClearInspectionState()
    {
        if (!inspectionActive) return;
        inspectionActive = false;
        ClearInspectionObjects();
        inspectionReport = "";
        currentTest = "";
    }

    private void OnDisable()
    {
        if (isRunning)
        {
            StopAllCoroutines();
            EndSession();
            memorySampler?.Dispose();
            memorySampler = null;
            isRunning = false;
        }
        else ClearInspectionState();
    }

    #endregion
}

/// <summary>
/// The text loops, generic over the per-object handle each engine exposes (a <c>Component</c> for uGUI text, a
/// <c>VisualElement</c> for UI Toolkit). Subclasses supply object lifecycle, placement on a page and property setters.
/// </summary>
public abstract class TextBenchmarkBase<TInstance> : TextBenchmarkBase where TInstance : class
{
    protected TInstance[] instances;

    /// <summary>Creates text <paramref name="index"/> on its page (<see cref="TextPhases.PageStride"/>), sized to the page.</summary>
    protected abstract TInstance CreateInstance(int index);
    protected abstract void DestroyInstance(TInstance instance);
    protected abstract void SetText(TInstance instance, string text);
    protected abstract void SetFontSize(TInstance instance, float size);
    protected abstract void SetColor(TInstance instance, Color color);
    protected abstract void SetWordWrap(TInstance instance, bool enabled);
    protected abstract void SetAutoSize(TInstance instance, bool enabled);
    protected abstract void SetRectSize(TInstance instance, float width, float height);

    /// <summary>Enables the engine's markup path so the tags in the markup corpus are parsed and styled.</summary>
    protected abstract void SetRichText(TInstance instance, bool enabled);

    /// <summary>Starts counting the reprocessing of <paramref name="instance"/> into <see cref="commitCount"/>; engines without a commit signal leave it uncounted.</summary>
    protected virtual void WatchCommits(TInstance instance) { }
    protected virtual void UnwatchCommits(TInstance instance) { }
    protected virtual bool CountsCommits => false;

    /// <summary>Whether <paramref name="instance"/> has been laid out, for engines without a commit signal.</summary>
    protected virtual bool LaidOut(TInstance instance) => true;

    protected virtual void SetInspectionName(TInstance instance, int index) { }
    protected virtual string DescribeInspectionInstance(TInstance instance, int index) => instance != null ? instance.ToString() : "null";

    protected long commitCount;

    public override long Commits => CountsCommits ? commitCount : -1;

    public override bool HasTexts => instances != null;

    public override void CreateAll(string text)
    {
        if (instances != null) throw new InvalidOperationException($"{SystemName} already holds its texts.");
        var count = Config.objectCount;
        instances = new TInstance[count];
        for (var i = 0; i < count; i++)
        {
            var instance = CreateInstance(i);
            instances[i] = instance;
            WatchCommits(instance);
            SetRichText(instance, false);
            SetText(instance, text);
        }
    }

    public override void DestroyAll()
    {
        if (instances == null) return;
        foreach (var instance in instances)
        {
            UnwatchCommits(instance);
            DestroyInstance(instance);
        }
        instances = null;
    }

    public override bool AllProcessed(long commitsBefore)
    {
        if (instances == null) return true;
        if (CountsCommits) return commitCount - Math.Max(0, commitsBefore) >= instances.Length;
        foreach (var instance in instances)
            if (!LaidOut(instance))
                return false;
        return true;
    }

    public override void ApplyPhase(TextPhase phase)
    {
        var text = TextPhases.StartText(phase, Corpus);
        var wrap = TextPhases.WordWrap(phase);
        var autoSize = TextPhases.AutoSize(phase);
        var rich = TextPhases.RichText(phase);
        foreach (var instance in instances)
        {
            SetWordWrap(instance, wrap);
            SetAutoSize(instance, autoSize);
            if (!autoSize) SetFontSize(instance, TextPhases.FontSize);
            SetRichText(instance, rich);
            SetRectSize(instance, TextPhases.PageWidth, TextPhases.PageHeight);
            SetColor(instance, TextPhases.ColorA);
            SetText(instance, text);
        }
    }

    public override void Step(TextPhase phase, string[] inputs, int frame)
    {
        if (inputs != null)
        {
            var text = inputs[frame % inputs.Length];
            foreach (var instance in instances)
                SetText(instance, text);
            return;
        }
        if (TextPhases.ChangesLayout(phase))
        {
            var size = (frame & 1) == 0 ? 0f : TextPhases.LayoutStep;
            foreach (var instance in instances)
                SetRectSize(instance, TextPhases.PageWidth - size, TextPhases.PageHeight - size);
            return;
        }
        if (phase == TextPhase.MeshRebuild)
        {
            var color = (frame & 1) == 0 ? TextPhases.ColorB : TextPhases.ColorA;
            foreach (var instance in instances)
                SetColor(instance, color);
        }
    }

    protected override IEnumerator RunInspection()
    {
        ClearInspectionState();
        isRunning = true;
        try
        {
            var corpus = inspectionLatinCorpus ? TextCorpus.Latin : TextCorpus.Multilingual;
            BeginSession(corpus);
            SelectEngine(EngineIds[0]);
            Visible = true;
            currentTest = $"{SystemName}: Inspect {TextPhases.Key(inspectionPhase)}";
            CreateAll(TextPhases.PlainText(corpus));
            for (var i = 0; i < instances.Length; i++)
                SetInspectionName(instances[i], i);
            ApplyPhase(inspectionPhase);
            var inputs = TextPhases.Inputs(inspectionPhase, corpus, 1, Mathf.Max(1, inspectionFrame + 1));
            PrepareInputs(inputs);
            Step(inspectionPhase, inputs, Mathf.Max(0, inspectionFrame));
            inspectionActive = true;
        }
        finally
        {
            isRunning = false;
        }
        yield return null;
        yield return waitForEndOfFrame;
        inspectionReport = BuildInspectionReport();
        Debug.Log(inspectionReport);
    }

    protected override void ClearInspectionObjects() => EndSession();

    private string BuildInspectionReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{SystemName}] Inspection: {TextPhases.Key(inspectionPhase)}, frame {inspectionFrame}");
        sb.AppendLine($"Corpus: {TextPhases.Key(Corpus)}");
        var count = instances == null ? 0 : instances.Length;
        sb.AppendLine($"Live objects: {count}");
        var samples = Mathf.Min(Mathf.Max(0, inspectionSampleCount), count);
        for (var i = 0; i < samples; i++)
            sb.AppendLine($"Sample {i}: {DescribeInspectionInstance(instances[i], i)}");
        if (count > samples)
            sb.AppendLine($"... {count - samples} more object(s)");
        return sb.ToString();
    }
}
