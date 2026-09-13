using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LightSide;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;
using LightSide.Benchmark;

/// <summary>Stable states from the shared text benchmark flow that can be left alive for editor inspection.</summary>
public enum BenchmarkInspectionPhase
{
    CreatedOnly,
    InitialCorpus,
    FullRebuildIncremental,
    FullRebuildUnique,
    FullRebuildRichText,
    LayoutWrapNoAuto,
    LayoutWrapAuto,
    LayoutNoWrapNoAuto,
    LayoutNoWrapAuto,
    MeshRebuildColor
}

/// <summary>
/// Engine-agnostic half of the text harness: shared config, result structs, corpus/string generation,
/// reporting and run control. The instance-typed measurement loops live in <see cref="TextBenchmarkBase{TInstance}"/>.
/// Split so <see cref="TestResults"/> stays ONE type across uGUI (Component) and UI Toolkit (Label).
/// </summary>
public abstract class TextBenchmarkBase : MonoBehaviour
{
    [Header("Settings")]
    public int objectCount = 100;
    public int iterations = 10;
    public int warmupIterations = 3;

    [Min(0), Tooltip("Identical untimed cycles run after each measured phase to distinguish a retained plateau from continued growth.")]
    public int memoryProbeRepeats = 1;

    [Header("Test Control")]
    public bool runCreationDestructionTest = true;
    public bool runFullRebuildTest = true;
    public bool runLayoutRebuildTest = true;
    public bool runMeshRebuildTest = true;

    /// <summary>Runs a separate untimed copy of each phase after its memory probes and ships prof_{engine}_{phase}.txt/.json to Benchmarks/prof/{runId}/ (device: persistentDataPath). Keeping capture buffers outside the timed and memory windows prevents profiler capacity from being attributed to the text engine. UniText needs UNITEXT_PROFILE for a deep tree; other engines report totals only.</summary>
    [Header("Profiling")]
    public bool captureProfile;

    [Header("Inspection")]
    [SerializeField] protected BenchmarkInspectionPhase inspectionPhase = BenchmarkInspectionPhase.FullRebuildUnique;
    [SerializeField] protected int inspectionVariantIndex;
    [SerializeField] protected bool inspectionLatinCorpus;
    [SerializeField, Min(0)] protected int inspectionSampleCount = 5;
    [SerializeField, TextArea(8, 24)] protected string inspectionReport = "";

    /// <summary>Corpus label recorded with the results ("multilingual" = the showcase text, "latin" = the apples-to-apples pass).</summary>
    [NonSerialized] public string corpusName = "multilingual";

    /// <summary>Swaps the run's workload for another code corpus (the Latin pass); null uses <see cref="BenchmarkConfig.Multilingual"/>. The workload never comes from a prefab or scene field.</summary>
    [NonSerialized] public string corpusOverrideText;

    [Header("Status")]
    [SerializeField] protected bool isRunning;
    [SerializeField] protected string currentTest = "";

    protected string[] testStrings;
    protected string[] uniqueTestStrings;
    protected string[] richTestStrings;
    protected string corpus;
    protected string richCorpus;
    protected readonly StringBuilder results = new();
    protected TestResults testResults;

    protected readonly Stopwatch stopwatch = new();
    protected readonly WaitForEndOfFrame waitForEndOfFrame = new();

    MemorySampler memorySampler;

    public TestResults Results => testResults;
    public bool IsRunning => isRunning;
    public string InspectionReport => inspectionReport;
    protected bool inspectionActive;

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

        internal static MemorySnapshot Max(MemorySnapshot a, MemorySnapshot b) => new()
        {
            resident = MaxValue(a.resident, b.resident),
            used = MaxValue(a.used, b.used),
            reserved = MaxValue(a.reserved, b.reserved),
            gcUsed = MaxValue(a.gcUsed, b.gcUsed),
            gcReserved = MaxValue(a.gcReserved, b.gcReserved),
            buffers = MaxValue(a.buffers, b.buffers)
        };

        internal static MemorySnapshot Invalid() => new()
        {
            resident = -1,
            used = -1,
            reserved = -1,
            gcUsed = -1,
            gcReserved = -1,
            buffers = -1
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

        internal static PhaseMemoryMetrics Create() => new()
        {
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
        public MemorySnapshot afterCleanup;
    }

    public struct TestMetrics
    {
        public List<float> frameTimes;
        public long managedAlloc;
        public PhaseMemoryMetrics memory;

        public static TestMetrics Create() => new()
        {
            frameTimes = new List<float>(),
            memory = PhaseMemoryMetrics.Create()
        };

        public float MedianFrameTime
        {
            get
            {
                if (frameTimes == null || frameTimes.Count == 0) return 0;
                var sorted = new List<float>(frameTimes);
                sorted.Sort();
                return BenchmarkStatistics.MedianSorted(sorted);
            }
        }

        public float TotalTime
        {
            get
            {
                if (frameTimes == null || frameTimes.Count == 0) return 0;
                float sum = 0;
                for (int i = 0; i < frameTimes.Count; i++)
                    sum += frameTimes[i];
                return sum;
            }
        }
    }

    public struct TestResults
    {
        public TestMetrics creation;
        public TestMetrics destruction;
        public TestMetrics fullRebuild;
        public TestMetrics fullRebuildUnique;
        public TestMetrics fullRebuildRichText;
        public TestMetrics layoutWrapNoAuto;
        public TestMetrics layoutWrapAuto;
        public TestMetrics layoutNoWrapNoAuto;
        public TestMetrics layoutNoWrapAuto;
        public TestMetrics meshRebuild;
        public RunMemoryMetrics memory;

        public static TestResults Create() => new()
        {
            creation = TestMetrics.Create(),
            destruction = TestMetrics.Create(),
            fullRebuild = TestMetrics.Create(),
            fullRebuildUnique = TestMetrics.Create(),
            fullRebuildRichText = TestMetrics.Create(),
            layoutWrapNoAuto = TestMetrics.Create(),
            layoutWrapAuto = TestMetrics.Create(),
            layoutNoWrapNoAuto = TestMetrics.Create(),
            layoutNoWrapAuto = TestMetrics.Create(),
            meshRebuild = TestMetrics.Create(),
            memory = new RunMemoryMetrics()
        };
    }

    sealed class MemorySampler : IDisposable
    {
        ProfilerRecorder resident;
        ProfilerRecorder used;
        ProfilerRecorder reserved;
        ProfilerRecorder gcUsed;
        ProfilerRecorder gcReserved;
        ProfilerRecorder gcAllocated;
        ProfilerRecorder buffers;

        public MemorySnapshot Peak { get; private set; }
        public bool ManagedAllocationAvailable => gcAllocated.Valid;

        public MemorySampler()
        {
            resident = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
            used = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory", 1);
            reserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory", 1);
            gcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory", 1);
            gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory", 1);
            gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
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
                buffers = Value(buffers)
            };
            Peak = MemorySnapshot.Max(Peak, snapshot);
            return snapshot;
        }

        public long ManagedAllocatedCurrentFrame => gcAllocated.Valid ? Math.Max(0, gcAllocated.CurrentValue) : -1;

        public void Dispose()
        {
            resident.Dispose();
            used.Dispose();
            reserved.Dispose();
            gcUsed.Dispose();
            gcReserved.Dispose();
            gcAllocated.Dispose();
            buffers.Dispose();
        }

        static long Value(ProfilerRecorder recorder) => recorder.Valid ? recorder.CurrentValue : -1;

    }

    #endregion

    #region Engine hooks

    public abstract string SystemName { get; }
    protected abstract void OnBeforeAllTests();
    protected abstract void OnAfterAllTests();
    protected abstract void SetupContainer();
    protected abstract void TeardownContainer();
    protected abstract IEnumerator RunAllTests();
    protected abstract IEnumerator RunInspection();
    protected abstract void ClearInspectionObjects();

    protected virtual bool ValidateSetup() => true;
    protected virtual void OnPhaseComplete(string phaseName) { }
    protected virtual void OnBeforePhaseIterations(string phaseName) { }
    protected virtual void OnAfterPhaseIterations(string phaseName) { }

    #endregion

    #region Test Strings

    /// <summary>Derives edit/unique/rich variants from the shared <see cref="corpus"/> — the incremental, cold and markup workloads.</summary>
    protected void GenerateTestStrings()
    {
        testStrings = new string[10];
        var len = corpus.Length;
        var mid = len / 2;
        var chunk10 = Math.Max(50, len / 10);
        var chunk20 = Math.Max(100, len / 5);
        var chunk30 = Math.Max(150, len * 3 / 10);

        testStrings[0] = corpus.Substring(chunk10);
        testStrings[1] = corpus.Substring(chunk20);
        testStrings[2] = corpus.Substring(chunk30);

        testStrings[3] = corpus.Substring(0, len - chunk10);
        testStrings[4] = corpus.Substring(0, len - chunk20);

        testStrings[5] = corpus.Substring(0, mid - chunk10 / 2) + corpus.Substring(mid + chunk10 / 2);
        testStrings[6] = corpus.Substring(0, mid - chunk20 / 2) + corpus.Substring(mid + chunk20 / 2);

        var pad10 = new string('X', chunk10 / 2);
        var pad20 = new string('Y', chunk20 / 2);
        testStrings[7] = pad10 + corpus + pad10;
        testStrings[8] = pad20 + corpus + pad20;

        var replacement = new string('Z', chunk20);
        testStrings[9] = corpus.Substring(0, mid - chunk20 / 2) + replacement + corpus.Substring(mid + chunk20 / 2);

        uniqueTestStrings = GenerateUniqueVariants(corpus, testStrings.Length);
        richTestStrings = GenerateUniqueVariants(richCorpus, testStrings.Length);
    }

    /// <summary>
    /// Variants where EVERY paragraph's content differs between iterations (a fixed-width token is
    /// injected at the start of each line). Engines with content-addressed caches (UniText's
    /// per-paragraph shape cache) get zero reuse here — the true cold "full rebuild" case, while the
    /// incremental variants measure the warm editing case. Same strings go to every engine, so the
    /// split is fair by construction. Shared by all benchmark harnesses.
    /// </summary>
    internal static string[] GenerateUniqueVariants(string corpus, int count)
    {
        var variants = new string[count];
        var sb = new StringBuilder(corpus.Length + 512);
        var lines = corpus.Split('\n');

        for (int v = 0; v < count; v++)
        {
            sb.Clear();
            for (int l = 0; l < lines.Length; l++)
            {
                if (l > 0) sb.Append('\n');
                sb.Append('v').Append(v).Append('.').Append(l % 10).Append(' ').Append(lines[l]);
            }
            variants[v] = sb.ToString();
        }

        return variants;
    }

    #endregion

    #region Run Benchmark

    [ContextMenu("Run Benchmark")]
    public void RunBenchmark()
    {
        if (isRunning)
        {
            Debug.LogWarning("Benchmark already running!");
            return;
        }

        if (!ValidateSetup()) return;

        StartCoroutine(RunBenchmarkCoroutine(silent: false));
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

    [ContextMenu("Stop Benchmark")]
    public void StopBenchmark()
    {
        if (!isRunning) return;
        StopAllCoroutines();
        TeardownContainer();
        ReleaseWorkload();
        memorySampler?.Dispose();
        memorySampler = null;
        isRunning = false;
        Debug.Log("Benchmark stopped.");
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
        ClearInspectionObjects();
        OnAfterAllTests();
        inspectionActive = false;
        inspectionReport = "";
        currentTest = "";
    }

    public IEnumerator RunBenchmarkCoroutine(bool silent)
    {
        BenchmarkRun.Begin();
        ClearInspectionState();
        isRunning = true;
        results.Clear();
        bool beforeAll = false;
        bool containerReady = false;
        bool completed = false;

        var cfg = BenchmarkConfig.Instance;
        if (cfg != null)
        {
            objectCount = cfg.objectCount;
            iterations = cfg.iterations;
            warmupIterations = cfg.warmupIterations;
            memoryProbeRepeats = cfg.memoryProbeRepeats;
        }

        memorySampler?.Dispose();
        ProfCounters.Release();
        Prof.ReleaseCaptureBuffers();
        memorySampler = new MemorySampler();
        testResults = TestResults.Create();
        try
        {
            yield return CollectAndSettle();
            testResults.memory.baseline = ReadMemory();
            testResults.memory.available = MemoryAvailable(testResults.memory.baseline);

            beforeAll = true;
            OnBeforeAllTests();
            containerReady = true;
            SetupContainer();
            LogMemory("Start");

            if (!silent)
                AppendHeader();

            yield return RunAllTests();

            yield return BenchmarkScreenshot.Capture($"text-{SystemName}-{corpusName}");

            if (!silent)
            {
                AppendResults();
                Debug.Log(results.ToString());
            }

            yield return CollectAndSettle();
            testResults.memory.beforeCleanup = ReadMemory();
            TeardownContainer();
            containerReady = false;
            OnAfterAllTests();
            beforeAll = false;
            ReleaseWorkload();
            yield return CollectAndSettle();
            testResults.memory.afterCleanup = ReadMemory();
            testResults.memory.peak = memorySampler.Peak;
            LogMemory("Cleanup");
            ShipRunMemoryProfile(testResults.memory);
            completed = true;
            currentTest = "Complete";
        }
        finally
        {
            if (!completed)
            {
                if (containerReady)
                {
                    try { TeardownContainer(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                if (beforeAll)
                {
                    try { OnAfterAllTests(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                ReleaseWorkload();
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

    protected IEnumerator CollectAndSettle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 5; i++)
            yield return waitForEndOfFrame;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 2; i++)
            yield return waitForEndOfFrame;
    }

    void ReleaseWorkload()
    {
        testStrings = null;
        uniqueTestStrings = null;
        richTestStrings = null;
        corpus = null;
        richCorpus = null;
    }

    #endregion

    #region Results Output

    private void AppendHeader()
    {
        results.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        results.AppendLine($"                         {SystemName} BENCHMARK");
        results.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        results.AppendLine($"  Objects: {objectCount}");
        results.AppendLine($"  Iterations: {iterations}");
        results.AppendLine($"  Warmup: {warmupIterations}");
        results.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
    }

    protected void AppendCreationDestructionResult()
    {
        results.AppendLine($"\n[{SystemName}] Creation/Destruction");
        results.AppendLine($"  Create: {testResults.creation.TotalTime:F2}ms | Destroy: {testResults.destruction.TotalTime:F2}ms");
        results.AppendLine($"  Managed traffic: create {FormatCounter(testResults.creation.managedAlloc)}, destroy {FormatCounter(testResults.destruction.managedAlloc)}");
    }

    protected void AppendSingleTestResult(string testName, TestMetrics metrics)
    {
        results.AppendLine($"\n[{SystemName}] {testName}");
        results.AppendLine($"  Time: {metrics.TotalTime:F2}ms");
        results.AppendLine($"  Managed traffic: {FormatCounter(metrics.managedAlloc)}");
    }

    private void AppendResults()
    {
        results.AppendLine("\n═══════════════════════════════════════════════════════════════════════════════");
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        return $"{bytes / (1024f * 1024f):F2} MB";
    }

    protected void LogMemory(string phase)
    {
        var memory = ReadMemory();

        Debug.Log($"[BenchmarkMemory] {SystemName}/{corpusName}/{phase}: " +
                  $"Resident={FormatCounter(memory.resident)}, Used={FormatCounter(memory.used)}, " +
                  $"Reserved={FormatCounter(memory.reserved)}, " +
                  $"GC={FormatCounter(memory.gcUsed)}/{FormatCounter(memory.gcReserved)}, " +
                  $"Buffers={FormatCounter(memory.buffers)}");
    }

    protected MemorySnapshot ReadMemory()
    {
        if (memorySampler != null) return memorySampler.Read();
        return new MemorySnapshot
        {
            resident = ReadCounter("System Used Memory"),
            used = ReadCounter("Total Used Memory"),
            reserved = ReadCounter("Total Reserved Memory"),
            gcUsed = ReadCounter("GC Used Memory"),
            gcReserved = ReadCounter("GC Reserved Memory"),
            buffers = ReadCounter(ProfilerCategory.Render, "Used Buffers Bytes")
        };
    }

    static long ReadCounter(string name) => ReadCounter(ProfilerCategory.Memory, name);

    static long ReadCounter(ProfilerCategory category, string name) =>
        ProfCounters.TryGetCurrentValue(category, name, out var value) ? value : -1;

    static string FormatCounter(long value) => value >= 0 ? FormatBytes(value) : "n/a";

    protected void AddManagedAllocation(ref long total)
    {
        var value = memorySampler?.ManagedAllocatedCurrentFrame ?? -1;
        if (value < 0)
        {
            total = -1;
            return;
        }
        if (total >= 0) total += value;
    }

    protected long ManagedAllocationInitialValue =>
        memorySampler?.ManagedAllocationAvailable == true ? 0 : -1;

    protected static bool MemoryAvailable(MemorySnapshot memory) =>
        memory.resident >= 0 || memory.used >= 0 || memory.gcUsed >= 0 || memory.buffers >= 0;

    protected void ShipPhaseMemoryProfile(string phase, string profilePhase, string checkpoint,
        PhaseMemoryMetrics memory)
    {
        var tag = ProfileTag(profilePhase);
        var run = BenchmarkRun.Id;
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeMemoryProfile(SystemName, corpusName, phase, run, checkpoint, memory),
            ProfilePath(tag, $"_memory-{checkpoint}.json"));
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeMemoryProfileText(SystemName, corpusName, phase, run, checkpoint, memory),
            ProfilePath(tag, $"_memory-{checkpoint}.txt"));
    }

    void ShipRunMemoryProfile(RunMemoryMetrics memory)
    {
        var tag = ProfileTag("Run");
        var run = BenchmarkRun.Id;
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeRunMemoryProfile(SystemName, corpusName, run, memory),
            ProfilePath(tag, "_memory.json"));
        ProfTransport.Ship(BenchmarkJsonSerializer.SerializeRunMemoryProfileText(SystemName, corpusName, run, memory),
            ProfilePath(tag, "_memory.txt"));
    }

    protected string ProfileTag(string phase) =>
        $"{ProfilePart(SystemName)}_{ProfilePart(phase)}_{ProfilePart(corpusName)}";

    protected static string ProfilePath(string tag, string suffix)
    {
        var run = BenchmarkRun.Id;
        return $"prof/{run}/{run}_prof_{tag}{suffix}";
    }

    static string ProfilePart(string value) => string.IsNullOrEmpty(value)
        ? "unknown"
        : value.Replace(" ", "").Replace("/", "-").Replace("\\", "-").Replace(":", "-");

    #endregion

    private void OnDisable()
    {
        if (isRunning) StopBenchmark();
        else ClearInspectionState();
    }
}

/// <summary>
/// The measurement loops, generic over the per-object handle each engine exposes (a <c>Component</c>
/// for uGUI text, a <c>Label</c> for UI Toolkit). Subclasses supply object lifecycle + property setters;
/// the timing windows, warmup, GC/alloc bookkeeping and phase order live here once for every engine.
/// </summary>
public abstract class TextBenchmarkBase<TInstance> : TextBenchmarkBase where TInstance : class
{
    protected TInstance[] instances;

    protected abstract TInstance CreateInstance(int index);
    protected abstract void DestroyInstance(TInstance instance);
    protected abstract void SetText(TInstance instance, string text);
    protected abstract void SetFontSize(TInstance instance, float size);
    protected abstract void SetColor(TInstance instance, Color color);
    protected abstract void SetWordWrap(TInstance instance, bool enabled);
    protected abstract void SetAutoSize(TInstance instance, bool enabled);
    protected abstract void SetRectSize(TInstance instance, float width, float height);

    /// <summary>Enables the engine's markup path so the bold tags in the rich variants are parsed and styled.</summary>
    protected abstract void SetRichText(TInstance instance, bool enabled);

    protected virtual void SetInspectionName(TInstance instance, int index, BenchmarkInspectionPhase phase) { }
    protected virtual int CountInspectionObjects() => instances == null ? 0 : instances.Length;
    protected virtual string DescribeInspectionInstance(TInstance instance, int index) => instance != null ? instance.ToString() : "null";

    struct InspectionState
    {
        public string textSource;
        public string text;
        public bool richText;
        public bool wordWrap;
        public bool autoSize;
        public bool hasWordWrap;
        public bool hasAutoSize;
        public bool hasRect;
        public bool hasColor;
        public float width;
        public float height;
        public Color color;
        public int measuredIndex;
    }

    void PrepareCorpus(bool latin)
    {
        if (latin)
        {
            corpus = richCorpus = BenchmarkConfig.Latin;
        }
        else if (string.IsNullOrEmpty(corpusOverrideText))
        {
            corpus = BenchmarkConfig.MultilingualPlain;
            richCorpus = BenchmarkConfig.Multilingual;
        }
        else
        {
            corpus = richCorpus = corpusOverrideText;
        }

        GenerateTestStrings();
    }

    protected override IEnumerator RunAllTests()
    {
        PrepareCorpus(false);

        if (runCreationDestructionTest)
        {
            currentTest = $"{SystemName}: Creation/Destruction";
            Debug.Log($"  [{SystemName}] Creation/Destruction test...");
            yield return RunCreationDestruction();
            AppendCreationDestructionResult();
            OnPhaseComplete("Creation/Destruction");
        }

        if (!(runFullRebuildTest || runLayoutRebuildTest || runMeshRebuildTest))
            yield break;

        Debug.Log($"  [{SystemName}] Creating instances for rebuild tests...");
        instances = new TInstance[objectCount];
        for (int i = 0; i < objectCount; i++)
            instances[i] = CreateInstance(i);

        for (int i = 0; i < objectCount; i++)
        {
            SetText(instances[i], corpus);
            SetRichText(instances[i], false);
        }

        yield return null;

        if (runFullRebuildTest)
        {
            currentTest = $"{SystemName}: Full Rebuild";
            Debug.Log($"  [{SystemName}] Full Rebuild test...");
            yield return RunMeasuredPhase("Full Rebuild (Incremental Edit)",
                w => ApplyText(testStrings, w), iter => ApplyText(testStrings, iter),
                m => testResults.fullRebuild = m);
            OnPhaseComplete("Full Rebuild");

            yield return RunMeasuredPhase("Full Rebuild (Unique Text)",
                w => ApplyText(uniqueTestStrings, w), iter => ApplyText(uniqueTestStrings, iter),
                m => testResults.fullRebuildUnique = m);
            OnPhaseComplete("Full Rebuild Unique");

            for (int i = 0; i < objectCount; i++) SetRichText(instances[i], true);
            yield return RunMeasuredPhase("Full Rebuild (Rich Text)",
                w => ApplyText(richTestStrings, w), iter => ApplyText(richTestStrings, iter),
                m => testResults.fullRebuildRichText = m);
            for (int i = 0; i < objectCount; i++) SetRichText(instances[i], false);
            OnPhaseComplete("Full Rebuild Rich Text");
        }

        if (runLayoutRebuildTest)
        {
            currentTest = $"{SystemName}: Layout Rebuild";
            Debug.Log($"  [{SystemName}] Layout Rebuild test...");
            yield return RunLayoutVariation(true, false, "Wrap=ON, Auto=OFF", m => testResults.layoutWrapNoAuto = m);
            yield return RunLayoutVariation(true, true, "Wrap=ON, Auto=ON", m => testResults.layoutWrapAuto = m);
            yield return RunLayoutVariation(false, false, "Wrap=OFF, Auto=OFF", m => testResults.layoutNoWrapNoAuto = m);
            yield return RunLayoutVariation(false, true, "Wrap=OFF, Auto=ON", m => testResults.layoutNoWrapAuto = m);
            OnPhaseComplete("Layout Rebuild");
        }

        if (runMeshRebuildTest)
        {
            currentTest = $"{SystemName}: Mesh Rebuild";
            Debug.Log($"  [{SystemName}] Mesh Rebuild test...");
            for (int i = 0; i < objectCount; i++)
            {
                SetAutoSize(instances[i], false);
                SetRectSize(instances[i], 300, 300);
                SetText(instances[i], corpus);
            }
            yield return null;

            yield return RunMeasuredPhase("Mesh Rebuild (Color)",
                w => ApplyColor(w * 0.1f),
                iter => ApplyColor((iter * 0.1f) % 1f),
                m => testResults.meshRebuild = m,
                phaseHookName: "Mesh Rebuild");
            OnPhaseComplete("Mesh Rebuild");
        }

        for (int i = 0; i < objectCount; i++)
            DestroyInstance(instances[i]);
        instances = null;
        yield return null;
    }

    protected override IEnumerator RunInspection()
    {
        ClearInspectionState();

        var cfg = BenchmarkConfig.Instance;
        if (cfg != null)
        {
            objectCount = cfg.objectCount;
            iterations = cfg.iterations;
            warmupIterations = cfg.warmupIterations;
        }

        isRunning = true;
        currentTest = $"{SystemName}: Inspect {inspectionPhase}";
        OnBeforeAllTests();
        SetupContainer();
        PrepareCorpus(inspectionLatinCorpus);

        instances = new TInstance[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            instances[i] = CreateInstance(i);
            SetInspectionName(instances[i], i, inspectionPhase);
        }

        var state = ApplyInspectionPhase();
        yield return null;
        yield return waitForEndOfFrame;

        inspectionReport = BuildInspectionReport(state);
        Debug.Log(inspectionReport);
        inspectionActive = true;
        isRunning = false;
    }

    protected override void ClearInspectionObjects()
    {
        TeardownContainer();
        instances = null;
    }

    private void ApplyText(string[] source, int index)
    {
        var text = source[index % source.Length];
        for (int i = 0; i < objectCount; i++)
            SetText(instances[i], text);
    }

    private void ApplyColor(float r)
    {
        var c = new Color(r, 0.5f, 0.5f, 1f);
        for (int i = 0; i < objectCount; i++)
            SetColor(instances[i], c);
    }

    private InspectionState ApplyInspectionPhase()
    {
        var measuredIndex = Mathf.Max(0, inspectionVariantIndex) + warmupIterations;
        var state = new InspectionState { measuredIndex = measuredIndex };

        switch (inspectionPhase)
        {
            case BenchmarkInspectionPhase.CreatedOnly:
                state.textSource = "prefab/template defaults";
                break;

            case BenchmarkInspectionPhase.InitialCorpus:
                state.textSource = "corpus";
                state.text = corpus;
                state.richText = false;
                ApplyRichText(false);
                ApplySingleText(corpus);
                break;

            case BenchmarkInspectionPhase.FullRebuildIncremental:
                state.textSource = $"testStrings[{measuredIndex % testStrings.Length}]";
                state.text = testStrings[measuredIndex % testStrings.Length];
                state.richText = false;
                ApplyRichText(false);
                ApplySingleText(state.text);
                break;

            case BenchmarkInspectionPhase.FullRebuildUnique:
                state.textSource = $"uniqueTestStrings[{measuredIndex % uniqueTestStrings.Length}]";
                state.text = uniqueTestStrings[measuredIndex % uniqueTestStrings.Length];
                state.richText = false;
                ApplyRichText(false);
                ApplySingleText(state.text);
                break;

            case BenchmarkInspectionPhase.FullRebuildRichText:
                state.textSource = $"richTestStrings[{measuredIndex % richTestStrings.Length}]";
                state.text = richTestStrings[measuredIndex % richTestStrings.Length];
                state.richText = true;
                ApplyRichText(true);
                ApplySingleText(state.text);
                break;

            case BenchmarkInspectionPhase.LayoutWrapNoAuto:
                ApplyInspectionLayout(true, false, measuredIndex, ref state);
                break;

            case BenchmarkInspectionPhase.LayoutWrapAuto:
                ApplyInspectionLayout(true, true, measuredIndex, ref state);
                break;

            case BenchmarkInspectionPhase.LayoutNoWrapNoAuto:
                ApplyInspectionLayout(false, false, measuredIndex, ref state);
                break;

            case BenchmarkInspectionPhase.LayoutNoWrapAuto:
                ApplyInspectionLayout(false, true, measuredIndex, ref state);
                break;

            case BenchmarkInspectionPhase.MeshRebuildColor:
                state.textSource = "corpus";
                state.text = corpus;
                state.autoSize = false;
                state.hasAutoSize = true;
                state.hasRect = true;
                state.width = 300f;
                state.height = 300f;
                state.color = new Color((measuredIndex * 0.1f) % 1f, 0.5f, 0.5f, 1f);
                state.hasColor = true;
                ApplyRichText(false);
                ApplySingleText(corpus);
                for (int i = 0; i < objectCount; i++)
                {
                    SetAutoSize(instances[i], false);
                    SetRectSize(instances[i], state.width, state.height);
                    SetColor(instances[i], state.color);
                }
                break;
        }

        return state;
    }

    private void ApplyInspectionLayout(bool wordWrap, bool autoSize, int measuredIndex, ref InspectionState state)
    {
        state.textSource = "corpus";
        state.text = corpus;
        state.wordWrap = wordWrap;
        state.autoSize = autoSize;
        state.hasWordWrap = true;
        state.hasAutoSize = true;
        state.hasRect = true;
        state.width = state.height = 100f + (measuredIndex % 10) * 550f;
        ApplyRichText(false);
        for (int i = 0; i < objectCount; i++)
        {
            SetWordWrap(instances[i], wordWrap);
            SetAutoSize(instances[i], autoSize);
            SetText(instances[i], corpus);
            SetRectSize(instances[i], state.width, state.height);
        }
    }

    private void ApplyRichText(bool enabled)
    {
        for (int i = 0; i < objectCount; i++)
            SetRichText(instances[i], enabled);
    }

    private void ApplySingleText(string text)
    {
        for (int i = 0; i < objectCount; i++)
            SetText(instances[i], text);
    }

    private string BuildInspectionReport(InspectionState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{SystemName}] Inspection: {inspectionPhase}");
        sb.AppendLine($"Corpus: {(inspectionLatinCorpus ? "latin" : corpusName)}");
        sb.AppendLine($"Requested objects: {objectCount}");
        sb.AppendLine($"Live objects: {CountInspectionObjects()}");
        sb.AppendLine($"Variant index: {inspectionVariantIndex} (measured index {state.measuredIndex})");
        if (!string.IsNullOrEmpty(state.textSource)) sb.AppendLine($"Text source: {state.textSource}");
        if (state.text != null)
        {
            sb.AppendLine($"Text length: {state.text.Length}");
            sb.AppendLine($"Text preview: {Preview(state.text, 240)}");
        }
        sb.AppendLine($"Rich text: {state.richText}");
        if (state.hasWordWrap) sb.AppendLine($"Word wrap: {state.wordWrap}");
        if (state.hasAutoSize) sb.AppendLine($"Auto size: {state.autoSize}");
        if (state.hasRect) sb.AppendLine($"Rect: {state.width:F1}x{state.height:F1}");
        if (state.hasColor) sb.AppendLine($"Color: {state.color}");

        var count = instances == null ? 0 : instances.Length;
        var samples = Mathf.Min(Mathf.Max(0, inspectionSampleCount), count);
        for (int i = 0; i < samples; i++)
            sb.AppendLine($"Sample {i}: {DescribeInspectionInstance(instances[i], i)}");
        if (count > samples)
            sb.AppendLine($"... {count - samples} more object(s)");
        return sb.ToString();
    }

    private static string Preview(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var oneLine = text.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= maxChars ? oneLine : oneLine.Substring(0, maxChars) + "...";
    }

    private IEnumerator RunCreationDestruction()
    {
        var creation = TestMetrics.Create();
        var destruction = TestMetrics.Create();
        creation.managedAlloc = ManagedAllocationInitialValue;
        destruction.managedAlloc = ManagedAllocationInitialValue;
        creation.frameTimes.Capacity = Math.Max(0, iterations);
        destruction.frameTimes.Capacity = Math.Max(0, iterations);
        creation.memory.probes.Capacity = Math.Max(0, memoryProbeRepeats);
        yield return CollectAndSettle();
        creation.memory.beforeWarmup = ReadMemory();
        creation.memory.available = MemoryAvailable(creation.memory.beforeWarmup);
        ShipPhaseMemoryProfile("Creation/Destruction", "Creation-Destruction", "started", creation.memory);
        yield return CollectAndSettle();
        creation.memory.normalizedBaseline = ReadMemory();

        for (int iter = 0; iter < warmupIterations; iter++)
        {
            instances = new TInstance[objectCount];
            for (int i = 0; i < objectCount; i++) { instances[i] = CreateInstance(i); SetText(instances[i], corpus); }
            yield return null;
            ReadMemory();
            for (int i = 0; i < objectCount; i++) DestroyInstance(instances[i]);
            instances = null;
            yield return null;
            ReadMemory();
            yield return null;
        }

        yield return CollectAndSettle();
        creation.memory.afterWarmup = ReadMemory();
        creation.memory.measuredPeak = creation.memory.afterWarmup;

        for (int iter = 0; iter < iterations; iter++)
        {
            instances = new TInstance[objectCount];
            stopwatch.Restart();
            for (int i = 0; i < objectCount; i++) { instances[i] = CreateInstance(i); SetText(instances[i], corpus); }
            yield return waitForEndOfFrame;
            stopwatch.Stop();
            AddManagedAllocation(ref creation.managedAlloc);
            creation.frameTimes.Add((float)stopwatch.Elapsed.TotalMilliseconds);
            creation.memory.measuredPeak = MemorySnapshot.Max(creation.memory.measuredPeak, ReadMemory());

            stopwatch.Restart();
            for (int i = 0; i < objectCount; i++) DestroyInstance(instances[i]);
            yield return waitForEndOfFrame;
            stopwatch.Stop();
            AddManagedAllocation(ref destruction.managedAlloc);
            destruction.frameTimes.Add((float)stopwatch.Elapsed.TotalMilliseconds);
            instances = null;
            creation.memory.measuredPeak = MemorySnapshot.Max(creation.memory.measuredPeak, ReadMemory());
            yield return null;
        }

        creation.memory.measuredEnd = ReadMemory();
        creation.memory.normalizedEnd = creation.memory.measuredEnd;
        yield return CollectAndSettle();
        creation.memory.afterMeasured = ReadMemory();
        testResults.creation = creation;
        testResults.destruction = destruction;
        ShipPhaseMemoryProfile("Creation/Destruction", "Creation-Destruction", "measured", creation.memory);

        yield return CollectAndSettle();
        creation.memory.beforeProbes = ReadMemory();
        for (int repeat = 0; repeat < memoryProbeRepeats; repeat++)
        {
            var before = ReadMemory();
            var cycle = new MemoryCycle
            {
                before = before,
                peak = before,
                managedAlloc = ManagedAllocationInitialValue
            };
            for (int iter = 0; iter < iterations; iter++)
            {
                instances = new TInstance[objectCount];
                for (int i = 0; i < objectCount; i++) { instances[i] = CreateInstance(i); SetText(instances[i], corpus); }
                yield return waitForEndOfFrame;
                AddManagedAllocation(ref cycle.managedAlloc);
                cycle.peak = MemorySnapshot.Max(cycle.peak, ReadMemory());

                for (int i = 0; i < objectCount; i++) DestroyInstance(instances[i]);
                yield return waitForEndOfFrame;
                AddManagedAllocation(ref cycle.managedAlloc);
                instances = null;
                cycle.peak = MemorySnapshot.Max(cycle.peak, ReadMemory());
            }

            cycle.end = ReadMemory();
            yield return CollectAndSettle();
            cycle.afterCollect = ReadMemory();
            creation.memory.probes.Add(cycle);
            testResults.creation = creation;
            ShipPhaseMemoryProfile("Creation/Destruction", "Creation-Destruction",
                $"probes{creation.memory.probes.Count}", creation.memory);
            yield return CollectAndSettle();
        }

        testResults.creation = creation;
        LogMemory("Creation/Destruction");
    }

    private IEnumerator RunLayoutVariation(bool wordWrap, bool autoSize, string name, Action<TestMetrics> setResult)
    {
        for (int i = 0; i < objectCount; i++)
        {
            SetWordWrap(instances[i], wordWrap);
            SetAutoSize(instances[i], autoSize);
            SetText(instances[i], corpus);
        }
        yield return null;

        yield return RunMeasuredPhase($"Layout ({name})",
            w => ApplyRectSize(100f + (w % 3) * 2500f),
            iter => ApplyRectSize(100f + (iter % 10) * 550f),
            m => { setResult(m); },
            phaseHookName: $"Layout ({name})");
    }

    private void ApplyRectSize(float size)
    {
        for (int i = 0; i < objectCount; i++)
            SetRectSize(instances[i], size, size);
    }

    /// <summary>
    /// The one measured-phase envelope: warmup, then <see cref="TextBenchmarkBase.iterations"/> timed frames of
    /// <paramref name="iterationStep"/> with managed-allocation traffic, GC and memory-lifecycle bookkeeping.
    /// Every rebuild/layout/mesh phase runs through here so their timing windows can never drift apart.
    /// The measured index continues past the warmup indices (<c>iter + warmupIterations</c>) so the first
    /// timed frame applies a value different from the last warmup — otherwise the engine dedups the
    /// unchanged input and the frame measures a no-op, not a rebuild.
    /// </summary>
    protected IEnumerator RunMeasuredPhase(string reportName, Action<int> warmupStep, Action<int> iterationStep,
        Action<TestMetrics> commit, string phaseHookName = null)
    {
        var metrics = TestMetrics.Create();
        metrics.managedAlloc = ManagedAllocationInitialValue;
        metrics.frameTimes.Capacity = Math.Max(0, iterations);
        metrics.memory.probes.Capacity = Math.Max(0, memoryProbeRepeats);
        var profilePhase = phaseHookName ?? reportName;
        LogMemory($"{reportName}/Start");
        yield return CollectAndSettle();
        metrics.memory.beforeWarmup = ReadMemory();
        metrics.memory.available = MemoryAvailable(metrics.memory.beforeWarmup);
        ShipPhaseMemoryProfile(reportName, profilePhase, "started", metrics.memory);
        yield return CollectAndSettle();
        int anchorIndex = Math.Max(0, warmupIterations - 1);
        int iterationStartIndex = warmupIterations > 0 ? warmupIterations : 1;
        warmupStep(anchorIndex);
        yield return waitForEndOfFrame;
        yield return CollectAndSettle();
        metrics.memory.normalizedBaseline = ReadMemory();
        for (int w = 0; w < warmupIterations; w++)
        {
            warmupStep(w);
            yield return null;
            ReadMemory();
        }
        warmupStep(anchorIndex);
        yield return waitForEndOfFrame;
        ReadMemory();

        bool phaseHookActive = phaseHookName != null;
        try
        {
            if (phaseHookActive) OnBeforePhaseIterations(phaseHookName);
            yield return CollectAndSettle();
            metrics.memory.afterWarmup = ReadMemory();
            metrics.memory.measuredPeak = metrics.memory.afterWarmup;

            for (int iter = 0; iter < iterations; iter++)
            {
                stopwatch.Restart();
                iterationStep(iter + iterationStartIndex);
                yield return waitForEndOfFrame;
                stopwatch.Stop();
                AddManagedAllocation(ref metrics.managedAlloc);
                metrics.frameTimes.Add((float)stopwatch.Elapsed.TotalMilliseconds);
                metrics.memory.measuredPeak = MemorySnapshot.Max(metrics.memory.measuredPeak, ReadMemory());
            }
            metrics.memory.measuredEnd = ReadMemory();
            warmupStep(anchorIndex);
            yield return waitForEndOfFrame;
            metrics.memory.normalizedEnd = ReadMemory();
        }
        finally
        {
            if (phaseHookActive) OnAfterPhaseIterations(phaseHookName);
        }

        yield return CollectAndSettle();
        metrics.memory.afterMeasured = ReadMemory();
        LogMemory(reportName);
        commit(metrics);
        ShipPhaseMemoryProfile(reportName, profilePhase, "measured", metrics.memory);

        yield return CollectAndSettle();
        metrics.memory.beforeProbes = ReadMemory();
        for (int repeat = 0; repeat < memoryProbeRepeats; repeat++)
        {
            var before = ReadMemory();
            var cycle = new MemoryCycle
            {
                before = before,
                peak = before,
                managedAlloc = ManagedAllocationInitialValue
            };
            for (int iter = 0; iter < iterations; iter++)
            {
                iterationStep(iter + iterationStartIndex);
                yield return waitForEndOfFrame;
                AddManagedAllocation(ref cycle.managedAlloc);
                cycle.peak = MemorySnapshot.Max(cycle.peak, ReadMemory());
            }

            warmupStep(anchorIndex);
            yield return waitForEndOfFrame;
            AddManagedAllocation(ref cycle.managedAlloc);
            cycle.end = ReadMemory();
            cycle.peak = MemorySnapshot.Max(cycle.peak, cycle.end);
            yield return CollectAndSettle();
            cycle.afterCollect = ReadMemory();
            metrics.memory.probes.Add(cycle);
            commit(metrics);
            ShipPhaseMemoryProfile(reportName, profilePhase, $"probes{metrics.memory.probes.Count}", metrics.memory);
            yield return CollectAndSettle();
        }

        commit(metrics);
        if (captureProfile)
        {
            yield return CapturePhaseProfile(profilePhase, iterationStep, warmupStep, anchorIndex,
                iterationStartIndex);
            yield return CollectAndSettle();
        }
        AppendSingleTestResult(reportName, metrics);
    }

    IEnumerator CapturePhaseProfile(string profilePhase, Action<int> iterationStep, Action<int> restoreStep,
        int anchorIndex, int iterationStartIndex)
    {
        ProfCapture capture;
        try
        {
            ProfCounters.Arm();
            Prof.BeginCapture();
            for (int iter = 0; iter < iterations; iter++)
            {
                iterationStep(iter + iterationStartIndex);
                yield return waitForEndOfFrame;
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

        restoreStep(anchorIndex);
        yield return waitForEndOfFrame;
        try
        {
            if (capture == null) yield break;
            var tag = ProfileTag(profilePhase);
            ProfTransport.Ship(capture.ToText(), ProfilePath(tag, ".txt"));
            ProfTransport.Ship(capture.ToJson(), ProfilePath(tag, ".json"));
        }
        finally
        {
            capture = null;
            ProfCounters.Release();
            Prof.ReleaseCaptureBuffers();
        }
    }
}
