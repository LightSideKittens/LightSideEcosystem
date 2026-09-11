using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Drives every <see cref="IBenchmarkSuite"/> present in the scene: resolves which suites the launch
    /// asked for, runs them under a watchdog with failure isolation, and writes the combined document,
    /// the per-suite viewer streams and the device game-loop artifacts.
    /// </summary>
    /// <remarks>
    /// The unattended start is compiled in only under <c>LIGHTSIDE_BENCHMARK</c>; a player built without
    /// that symbol carries the runner but never starts itself.
    /// </remarks>
    [AddComponentMenu(LightSideCore.Menu.Root + "/Benchmarks/" + nameof(BenchmarkRunner))]
    public class BenchmarkRunner : MonoBehaviour
    {
        const float WatchdogTimeout = 1800f;
        const double InteractiveSelectionSeconds = 10.0;

        /// <summary>Repository-relative path of the submodule whose revision is stamped beside the project's own; empty when the project has none.</summary>
        public string submodulePath = "";

        BenchmarkRunData data;
        BenchmarkContext context;
        List<IBenchmarkSuite> suites;
        readonly HashSet<string> requested = new();
        bool suiteRunning;
        bool watchdogTriggered;
        bool runFailed;
        float suiteStartedAt;

#if LIGHTSIDE_BENCHMARK
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnRuntimeStart()
        {
            Debug.Log("[BenchmarkRunner] OnRuntimeStart called");

            var runner = ObjectUtils.FindAny<BenchmarkRunner>();
            if (runner == null)
            {
                Debug.LogError("[BenchmarkRunner] BenchmarkRunner not found on scene!");
                Application.Quit(1);
                return;
            }

#if UNITY_EDITOR
            return;
#else
            runner.StartCoroutine(runner.AutoStart());
#endif
        }
#endif

        /// <summary>Every suite in the scene, in scene order.</summary>
        public IReadOnlyList<IBenchmarkSuite> Suites => suites ??= DiscoverSuites();

        /// <summary>Starts every suite unless another run is already in flight.</summary>
        [ContextMenu("Run All Benchmarks")]
        public void RunFromMenu() => StartSuites(AllSuiteIds());

        /// <summary>Starts the named suite alone unless another run is already in flight.</summary>
        public void RunOnly(string suiteId) => StartSuites(new[] { suiteId });

        /// <summary>Every suite component alive in the loaded scenes, in scene order.</summary>
        public static List<IBenchmarkSuite> DiscoverSuites()
        {
            var found = new List<IBenchmarkSuite>();
            foreach (var behaviour in ObjectUtils.FindAll<MonoBehaviour>())
                if (behaviour is IBenchmarkSuite suite)
                    found.Add(suite);
            return found;
        }

        string[] AllSuiteIds()
        {
            var ids = new string[Suites.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = Suites[i].SuiteId;
            return ids;
        }

        IEnumerator AutoStart()
        {
            FirebaseTestLabAndroid.Initialize();
            FirebaseTestLabiOS.Initialize();
            var (selection, explicitSuite) = ResolveLaunchSelection();
            if (!Application.isBatchMode && !explicitSuite)
            {
                Debug.Log($"[BenchmarkRunner] Waiting up to {InteractiveSelectionSeconds:F0} seconds before the unattended start; a Run button starts immediately.");
                double deadline = Time.realtimeSinceStartupAsDouble + InteractiveSelectionSeconds;
                while (!suiteRunning && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
            }
            if (!suiteRunning)
                StartSuites(selection);
        }

        /// <summary>
        /// Launch-time selection: <c>-benchmarkSuite &lt;id|all&gt;</c> (or the <c>=</c> form) and the
        /// <c>BENCHMARK_SUITE</c> environment variable on desktop, the Firebase game-loop scenario on
        /// devices (1 selects every suite, higher numbers are claimed by <see cref="IBenchmarkSuite.Scenario"/>),
        /// and the page URL's <c>?suite=</c> query on WebGL. An explicit selection skips the interactive
        /// wait so CI starts immediately.
        /// </summary>
        /// <summary>
        /// Reads one launch parameter from <c>-flag value</c>, <c>-flag=value</c> or the environment,
        /// in that order. Every selection a run accepts arrives through one of those three, and each
        /// one parsing them for itself is how they drift apart.
        /// </summary>
        static string LaunchValue(string flag, string environmentVariable)
        {
            var args = Environment.GetCommandLineArgs();
            string inline = flag + "=";
            string value = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    value = args[i + 1];
                else if (args[i].StartsWith(inline, StringComparison.OrdinalIgnoreCase))
                    value = args[i].Substring(inline.Length);
            }
            value ??= Environment.GetEnvironmentVariable(environmentVariable);
            value = value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>The one participant this process measures, or null when it measures every one.</summary>
        static string LaunchParticipant() => LaunchValue("-benchmarkParticipant", "BENCHMARK_PARTICIPANT");

        /// <summary>Zero-based index of this process among repeats of the same measurement.</summary>
        static int LaunchRepeat() =>
            int.TryParse(LaunchValue("-benchmarkRepeat", "BENCHMARK_REPEAT"), out var repeat) && repeat > 0
                ? repeat
                : 0;

        (string[] selection, bool explicitSuite) ResolveLaunchSelection()
        {
            string suite = LaunchValue("-benchmarkSuite", "BENCHMARK_SUITE");

            if (suite == null)
            {
                int scenario = FirebaseTestLabAndroid.ScenarioNumber;
                if (scenario <= 0) scenario = FirebaseTestLabiOS.ScenarioNumber;
                if (scenario == 1) suite = "all";
                else if (scenario > 1)
                    foreach (var candidate in Suites)
                        if (candidate.Scenario == scenario)
                            suite = candidate.SuiteId;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (suite == null && !string.IsNullOrEmpty(Application.absoluteURL))
            {
                var url = Application.absoluteURL;
                int q = url.IndexOf("suite=", StringComparison.OrdinalIgnoreCase);
                if (q >= 0)
                {
                    int start = q + "suite=".Length;
                    int end = url.IndexOfAny(new[] { '&', '#' }, start);
                    suite = end < 0 ? url.Substring(start) : url.Substring(start, end - start);
                }
            }
#endif

            suite = suite?.Trim().ToLowerInvariant();
            if (suite != null)
                Debug.Log($"[BenchmarkRunner] Launch suite: {suite}");
            if (suite == null) return (AllSuiteIds(), false);
            if (suite == "all") return (AllSuiteIds(), true);
            return (new[] { suite }, true);
        }

        void StartSuites(IReadOnlyList<string> selection)
        {
            if (suiteRunning) return;
            suiteRunning = true;
            watchdogTriggered = false;
            runFailed = false;
            requested.Clear();
            foreach (var id in selection) requested.Add(id);
            suiteStartedAt = Time.realtimeSinceStartup;
            Debug.Log("[BenchmarkRunner] Starting benchmarks...");
            StartCoroutine(GuardedRunSuites());
        }

        IEnumerator GuardedRunSuites()
        {
            var routine = new OwnedEnumerator(RunSuites());
            Exception failure = null;
            bool cleanupFailure = false;
            bool completed = false;
            var sleepTimeout = Screen.sleepTimeout;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            try
            {
                while (true)
                {
                    if (!routine.MoveNext(out var current, out failure, out cleanupFailure))
                        break;
                    yield return current;
                }
                completed = failure == null && routine.Completed;
            }
            finally
            {
                failure = routine.Dispose(failure, ref cleanupFailure);
                if (!completed && failure == null)
                    failure = new InvalidOperationException("Benchmark suite ended before completion.");
                if (failure != null)
                {
                    EnsureRunData();
                    data.errors.Add($"suite: {failure.Message}");
                    Debug.LogError($"[BenchmarkRunner] Suite failed: {failure}");
                }
                bool successful = completed && failure == null && !watchdogTriggered && !runFailed && data.errors.Count == 0;
                if (!RequestedSuitesComplete(out var incompleteReason))
                {
                    successful = false;
                    EnsureRunData();
                    data.errors.Add(incompleteReason);
                    Debug.LogError($"[BenchmarkRunner] {incompleteReason}");
                }
                suiteRunning = false;
                Screen.sleepTimeout = sleepTimeout;
                PersistResults(successful);
            }
        }

        /// <summary>One combined result document is always written; <see cref="BenchmarkHistory"/> splits it into per-suite site streams, so a single-suite run persists only its selected stream.</summary>
        IEnumerator RunSuites()
        {
            data = new BenchmarkRunData
            {
                timestamp = DateTime.UtcNow.ToString("o")
            };
            FillMeta(data, submodulePath);
            var participant = LaunchParticipant();
            var repeat = LaunchRepeat();
            data.participant = participant;
            data.repeat = repeat;
            if (participant != null)
                Debug.Log($"[BenchmarkRunner] Measuring participant '{participant}' alone, repeat {repeat}.");
            RequestSustainedPerformance();
            context = new BenchmarkContext(CheckWatchdog, SafeRun, EngineCooldown,
                message => data.errors.Add(message), data.config, participant, repeat, ThermalSettle);

            for (int i = 0; i < 5; i++)
                yield return null;

            Debug.Log("[BenchmarkRunner] === BENCHMARK START ===");
            Debug.Log(BenchmarkEnvironment.EnvironmentSummary());

            bool first = true;
            foreach (var suite in Suites)
            {
                if (!requested.Contains(suite.SuiteId)) continue;
                if (!first)
                {
                    if (!CheckWatchdog()) yield break;
                    yield return EngineCooldown();
                }
                first = false;
                Debug.Log($"[BenchmarkRunner] Running suite '{suite.SuiteId}'...");
                yield return suite.Run(context);
            }
        }

        int stepsStarted;

        /// <summary>
        /// Announces a measured step and what it cost. A benchmark run is minutes of silence otherwise:
        /// the step names are the only account of where a run stands, and their individual durations are
        /// the only basis for judging how much of the watchdog budget remains.
        /// </summary>
        void AnnounceStep(int step, string name, float startedAt, bool? outcome)
        {
            float now = Time.realtimeSinceStartup;
            string elapsed = $"{now - suiteStartedAt:F0}s/{WatchdogTimeout:F0}s";
            Debug.Log(outcome == null
                ? $"[BenchmarkRunner] step {step} start  {name}  [{elapsed}]"
                : $"[BenchmarkRunner] step {step} {(outcome.Value ? "done " : "FAIL ")} {name}  " +
                  $"took {now - startedAt:F1}s  [{elapsed}]");
        }

        IEnumerator SafeRun(string name, Func<IEnumerator> coroutineFactory, Action onComplete,
            Action onFailure = null)
        {
            int step = ++stepsStarted;
            float stepStartedAt = Time.realtimeSinceStartup;
            AnnounceStep(step, name, stepStartedAt, null);
            IEnumerator coroutine = null;
            Exception startFailure = null;
            try
            {
                coroutine = coroutineFactory();
            }
            catch (Exception e)
            {
                startFailure = e;
            }
            if (startFailure != null)
            {
                runFailed = true;
                data.errors.Add($"{name}: {startFailure.Message}");
                Debug.LogError($"[BenchmarkRunner] Failed to start {name}: {startFailure}");
                yield break;
            }
            if (coroutine == null)
            {
                runFailed = true;
                var failure = new InvalidOperationException($"{name} returned no coroutine.");
                RecordRunFailure(name, failure);
                yield break;
            }

            var routine = new OwnedEnumerator(coroutine);
            bool succeeded = false;
            bool failureRecorded = false;
            Exception caught = null;
            bool cleanupFailure = false;
            try
            {
                while (true)
                {
                    if (!CheckWatchdog())
                    {
                        data.errors.Add($"{name}: watchdog timeout");
                        runFailed = true;
                        break;
                    }

                    if (!routine.MoveNext(out var current, out caught, out var moveCleanupFailure))
                    {
                        cleanupFailure |= moveCleanupFailure;
                        if (caught == null)
                            succeeded = true;
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                caught = routine.Dispose(caught, ref cleanupFailure);
                if (caught != null && !failureRecorded)
                {
                    RecordRunFailure(name, caught);
                    failureRecorded = true;
                }
                if (!succeeded)
                    runFailed = true;
                AnnounceStep(step, name, stepStartedAt, succeeded);
                var collect = succeeded ? onComplete : onFailure;
                if (collect != null)
                {
                    if (!CollectResults(name, collect))
                        runFailed = true;
                }
                if (cleanupFailure && caught != null)
                    throw caught is BenchmarkCleanupException
                        ? caught
                        : new BenchmarkCleanupException($"{name} cleanup failed.", caught);
            }
        }

        bool CollectResults(string name, Action collect)
        {
            try
            {
                collect();
                return true;
            }
            catch (Exception exception)
            {
                data.errors.Add($"{name} result collection: {exception.Message}");
                Debug.LogError($"[BenchmarkRunner] Failed to collect {name} results: {exception}");
                return false;
            }
        }

        void RecordRunFailure(string name, Exception exception)
        {
            data.errors.Add($"{name}: {exception.Message}");
            Debug.LogError($"[BenchmarkRunner] {name} failed: {exception}");
        }

        bool CheckWatchdog()
        {
            if (Time.realtimeSinceStartup - suiteStartedAt > WatchdogTimeout)
            {
                if (!watchdogTriggered)
                {
                    watchdogTriggered = true;
                    Debug.LogWarning($"[BenchmarkRunner] Watchdog timeout ({WatchdogTimeout}s), writing partial results");
                    data.errors.Add($"Watchdog timeout at {Time.realtimeSinceStartup:F0}s");
                }
                return false;
            }
            return true;
        }

        bool RequestedSuitesComplete(out string reason)
        {
            EnsureRunData();
            var missing = new List<string>();
            foreach (var suite in Suites)
            {
                if (!requested.Contains(suite.SuiteId)) continue;
                if (suite.Measured(out var refusal)) continue;
                if (!string.IsNullOrEmpty(refusal))
                {
                    reason = refusal;
                    return false;
                }
                missing.Add(suite.SuiteId);
            }

            foreach (var id in requested)
            {
                bool present = false;
                foreach (var suite in Suites)
                    present |= suite.SuiteId == id;
                if (!present) missing.Add($"{id} (no suite in scene)");
            }

            if (missing.Count == 0)
            {
                reason = null;
                return true;
            }
            reason = $"Requested benchmark suite data is missing: {string.Join(", ", missing)}.";
            return false;
        }

        /// <summary>
        /// Run provenance: UTC stamp, commit/branch (git in the editor, GITHUB_* env on CI runners),
        /// dirty flag and origin — the history viewer separates publishable numbers from tainted ones by these.
        /// </summary>
        static void FillMeta(BenchmarkRunData data, string submodulePath)
        {
            data.utc = DateTime.UtcNow.ToString("o");
            data.commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unknown";
            data.branch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME") ?? "unknown";
            data.source = Application.isEditor ? "editor"
                : Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? "ci"
                : "local-player";

#if UNITY_EDITOR
            data.commit = RunGit("rev-parse HEAD") ?? data.commit;
            data.branch = RunGit("rev-parse --abbrev-ref HEAD") ?? data.branch;
            data.dirty = GitDirty("diff-index --quiet HEAD");

            if (!string.IsNullOrEmpty(submodulePath))
            {
                data.submoduleCommit = RunGit($"-C {submodulePath} rev-parse HEAD") ?? data.submoduleCommit;
                data.submoduleBranch = RunGit($"-C {submodulePath} rev-parse --abbrev-ref HEAD") ?? data.submoduleBranch;
                data.submoduleDirty = GitDirty($"-C {submodulePath} diff-index --quiet HEAD");
            }
#else
            ApplyBakedBuildInfo(data);
#endif
        }

        /// <summary>Player builds have no git and cannot see the runner's env; <see cref="BenchmarkBuildStamp"/> baked the commit into Resources at build time.</summary>
        static void ApplyBakedBuildInfo(BenchmarkRunData data)
        {
            var asset = Resources.Load<TextAsset>(BenchmarkBuildInfo.ResourceName);
            if (asset == null) return;

            var info = JsonUtility.FromJson<BenchmarkBuildInfo>(asset.text);
            if (info == null) return;

            if (!string.IsNullOrEmpty(info.commit)) data.commit = info.commit;
            if (!string.IsNullOrEmpty(info.branch)) data.branch = info.branch;
            data.dirty = info.dirty;
            if (!string.IsNullOrEmpty(info.submoduleCommit)) data.submoduleCommit = info.submoduleCommit;
            if (!string.IsNullOrEmpty(info.submoduleBranch)) data.submoduleBranch = info.submoduleBranch;
            data.submoduleDirty = info.submoduleDirty;
        }

#if UNITY_EDITOR
        static string RunGit(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Directory.GetParent(Application.dataPath).FullName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                return p.ExitCode == 0 && output.Length > 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Working-tree dirtiness via <c>git diff-index --quiet HEAD</c> (exit 1 = tracked changes present).
        /// Used instead of <c>git status --porcelain</c> because status scans the whole tree (~3s on a large
        /// Unity repo) while diff-index short-circuits on the first change (~40ms). Untracked-only changes do
        /// not count as dirty. Bounded by a timeout + kill so a stalled git can never hang the run.
        /// </summary>
        static bool GitDirty(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Directory.GetParent(Application.dataPath).FullName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 1;
            }
            catch
            {
                return false;
            }
        }
#endif

        /// <summary>
        /// Heap normalization between engine runs: without it, later engines inherit the previous run's
        /// heap debt and pending finalizers, contaminating GC counts and alloc deltas in run order.
        /// </summary>
        static IEnumerator EngineCooldown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            for (var i = 0; i < 10; i++) yield return null;
        }

        internal static double thermalProbeSink;

        /// <summary>
        /// Fixed CPU work whose cost is only allowed to change with the machine underneath it. The
        /// result is kept so nothing can elide the loop.
        /// </summary>
        static double ThermalProbe()
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            double accumulator = 0d;
            for (int i = 1; i <= 200_000; i++)
                accumulator += 1d / i;
            thermalProbeSink = accumulator;
            return (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d /
                   System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>
        /// Waits until fixed work costs what it did at the start of the run. A phone answers a long
        /// benchmark by heating up and lowering its clocks, so a run that simply proceeds measures the
        /// thermal curve alongside the participants and charges the later ones for the earlier ones'
        /// heat. Alternating the order spreads that bias rather than removing it; only waiting removes
        /// it. Settled means consecutive probes agree — a device pinned at sustained clocks is a valid,
        /// stable bench; an absolute baseline would chase the launch boost the OS grants and then
        /// withdraws. Giving up is reported, never silent: clocks that never stop drifting are a finding
        /// about the device, not a result about the participants.
        /// </summary>
        IEnumerator ThermalSettle()
        {
            const double tolerance = 1.1d;
            const float quickRecheck = 0.25f;
            const float retryDelay = 1f;
            const float settleTimeout = 60f;
            const int agreementsNeeded = 2;

            var previous = ThermalProbe();
            float until = Time.realtimeSinceStartup + quickRecheck;
            while (Time.realtimeSinceStartup < until) yield return null;
            double cost = ThermalProbe();
            if (Agrees(previous, cost, tolerance)) yield break;

            previous = cost;
            var agreements = 0;
            float deadline = Time.realtimeSinceStartup + settleTimeout;
            while (true)
            {
                until = Time.realtimeSinceStartup + retryDelay;
                while (Time.realtimeSinceStartup < until) yield return null;
                cost = ThermalProbe();
                if (Agrees(previous, cost, tolerance))
                {
                    if (++agreements >= agreementsNeeded) yield break;
                }
                else agreements = 0;
                previous = cost;
                if (Time.realtimeSinceStartup < deadline) continue;

                var message = $"Device clocks kept drifting: fixed work cost still moving around " +
                              $"{cost:F2}ms after {settleTimeout:F0}s.";
                Debug.LogWarning($"[BenchmarkRunner] {message}");
                data?.errors.Add(message);
                yield break;
            }
        }

        static bool Agrees(double previous, double current, double tolerance) =>
            (current > previous ? current / previous : previous / current) <= tolerance;

        /// <summary>
        /// Asks Android for a clock ceiling it can hold indefinitely. Without it a device answers a
        /// benchmark with a burst it cannot sustain and then throttles mid-run, which turns run order
        /// into a result. Absent on other platforms and on devices that do not support the mode.
        /// </summary>
        static void RequestSustainedPerformance()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return;
                // The activity outlives this call: runOnUiThread defers, so disposing here would hand
                // the runnable a released reference. Both handles are released by the runnable itself.
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using var window = activity.Call<AndroidJavaObject>("getWindow");
                        window.Call("setSustainedPerformanceMode", true);
                    }
                    finally
                    {
                        activity.Dispose();
                    }
                }));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BenchmarkRunner] Sustained performance mode unavailable: {exception.Message}");
            }
#endif
        }

        void EnsureRunData()
        {
            data ??= new BenchmarkRunData
            {
                timestamp = DateTime.UtcNow.ToString("o")
            };
        }

        void PersistResults(bool completed)
        {
            EnsureRunData();
            try
            {
                var json = BenchmarkEnvironment.Serialize(data, SelectedSuites(), out var postRunSummary);
                Debug.Log(completed
                    ? "[BenchmarkRunner] === BENCHMARK COMPLETE ==="
                    : "[BenchmarkRunner] === PARTIAL BENCHMARK RESULTS ===");
                Debug.Log(postRunSummary);
                OutputResults(json, completed ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BenchmarkRunner] Failed to persist benchmark results: {exception}");
#if !UNITY_EDITOR && !UNITY_WEBGL
                Application.Quit(1);
#endif
            }
        }

        List<IBenchmarkSuite> SelectedSuites()
        {
            var selected = new List<IBenchmarkSuite>();
            foreach (var suite in Suites)
                if (requested.Contains(suite.SuiteId))
                    selected.Add(suite);
            return selected;
        }

        #region Output

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ReportBenchmarkResults(string json);
#endif

        void OutputResults(string json, int exitCode)
        {
            var jsonPath = Path.Combine(Application.persistentDataPath, "benchmarkResults.json");
            File.WriteAllText(jsonPath, json);
            Debug.Log($"[BenchmarkRunner] Results saved to: {jsonPath}");

            var streams = WriteSiteStreams(json);

#if UNITY_EDITOR
            // Every run is kept, including an interactive editor one whose frames carry overhead the
            // player never pays. Its numbers are not comparable, but withholding them leaves the run
            // that produced them impossible to inspect; the stream already carries meta.source, so the
            // viewer can mark or exclude them without the data being lost here.
            BenchmarkHistory.SaveRun(json);
            if (!Application.isBatchMode)
                Debug.Log("[BenchmarkRunner] Interactive editor run: recorded, but its frames carry editor overhead and are not comparable with player runs.");
#endif
            Debug.Log($"[BenchmarkRunner] JSON length: {json.Length} chars");

            Console.WriteLine($"BENCHMARK_RESULTS_PATH={jsonPath}");

#if UNITY_WEBGL && !UNITY_EDITOR
            ReportBenchmarkResults(json);
#elif UNITY_IOS && !UNITY_EDITOR
            FirebaseTestLabiOS.WriteResults("benchmarkResults.json", json);
            FirebaseTestLabiOS.NotifyTestComplete();
            System.Threading.Thread.Sleep(500);
#elif UNITY_ANDROID && !UNITY_EDITOR
            FirebaseTestLabAndroid.WriteResultsArchive(BuildBenchmarkArchive(json, streams), "benchmarkResults.zip");
            FirebaseTestLabAndroid.NotifyTestComplete();
#endif

#if !UNITY_EDITOR && !UNITY_WEBGL
            Application.Quit(exitCode);
#endif
        }

        /// <summary>
        /// Android game-loop collects exactly ONE output file (the intent fd), so the run's whole artifact
        /// set — combined JSON, per-suite viewer streams, benchmark screenshots — ships as one zip that CI
        /// unpacks (the same channel golden tests use for their screenshots).
        /// </summary>
        static byte[] BuildBenchmarkArchive(string json, List<(string fileName, string contents)> streams)
        {
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                using (var w = new StreamWriter(zip.CreateEntry("benchmarkResults.json").Open()))
                    w.Write(json);

                foreach (var s in streams)
                    using (var w = new StreamWriter(zip.CreateEntry("benchmark-streams/" + s.fileName).Open()))
                        w.Write(s.contents);

                var screenshotsDir = Path.Combine(Application.persistentDataPath, "Screenshots");
                if (Directory.Exists(screenshotsDir))
                {
                    foreach (var png in Directory.GetFiles(screenshotsDir, "*.png"))
                    {
                        using var entry = zip.CreateEntry("screenshots/" + Path.GetFileName(png)).Open();
                        var bytes = File.ReadAllBytes(png);
                        entry.Write(bytes, 0, bytes.Length);
                    }
                }

                var logsDir = Path.Combine(Application.persistentDataPath, "Logs");
                if (Directory.Exists(logsDir))
                {
                    foreach (var logFile in Directory.GetFiles(logsDir, "*.log"))
                    {
                        try
                        {
                            byte[] bytes;
                            using (var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                bytes = new byte[stream.Length];
                                int read = stream.Read(bytes, 0, bytes.Length);
                                if (read != bytes.Length) System.Array.Resize(ref bytes, read);
                            }
                            using var entry = zip.CreateEntry("logs/" + Path.GetFileName(logFile)).Open();
                            entry.Write(bytes, 0, bytes.Length);
                        }
                        catch (IOException) { }
                    }
                }
            }
            return ms.ToArray();
        }

        /// <summary>Device parity for the viewer files: the editor persists split streams to Benchmarks/runs via <see cref="BenchmarkHistory"/>, so player builds emit them next to benchmarkResults.json in persistentDataPath — ready to drop into Benchmarks/runs. iOS additionally copies them into the game-loop results dir; Android bundles them into the game-loop archive.</summary>
        List<(string fileName, string contents)> WriteSiteStreams(string json)
        {
            var written = new List<(string fileName, string contents)>();
            if (Application.isEditor) return written;
            try
            {
                var stamp = BenchmarkStreams.Stamp(Application.platform.ToString(), SystemInfo.deviceName);
                foreach (var f in BenchmarkStreams.Split(json, stamp, SelectedSuites()))
                {
                    var path = Path.Combine(Application.persistentDataPath, f.fileName);
                    File.WriteAllText(path, f.contents);
                    written.Add((f.fileName, f.contents));
#if UNITY_IOS && !UNITY_EDITOR
                    FirebaseTestLabiOS.WriteResults(f.fileName, f.contents);
#endif
                    Debug.Log($"[BenchmarkRunner] Site stream saved: {path}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BenchmarkRunner] Failed to write site streams: {e}");
            }
            return written;
        }

        #endregion
    }

    /// <summary>
    /// Drives a coroutine tree the caller owns rather than the engine: nested enumerators are stepped
    /// inline, so a throw surfaces to the owner instead of being swallowed by Unity, and every nested
    /// enumerator is disposed on the way out. Yield instructions pass through untouched.
    /// </summary>
    public sealed class OwnedEnumerator
    {
        readonly Stack<IEnumerator> stack = new();

        /// <summary>Takes ownership of <paramref name="root"/> and every enumerator it yields.</summary>
        public OwnedEnumerator(IEnumerator root) => stack.Push(root);

        /// <summary>Whether the whole tree ran to its end.</summary>
        public bool Completed => stack.Count == 0;

        /// <summary>
        /// Advances to the next value the caller must yield. Returns false when the tree ended or threw;
        /// <paramref name="failure"/> then carries the exception and <paramref name="cleanupFailure"/>
        /// tells whether it came from unwinding rather than from the work.
        /// </summary>
        public bool MoveNext(out object current, out Exception failure, out bool cleanupFailure)
        {
            current = null;
            failure = null;
            cleanupFailure = false;
            while (stack.Count != 0)
            {
                var routine = stack.Peek();
                bool moved;
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    cleanupFailure = exception is BenchmarkCleanupException;
                    return false;
                }

                if (!moved)
                {
                    stack.Pop();
                    try
                    {
                        (routine as IDisposable)?.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        cleanupFailure = true;
                        return false;
                    }
                    continue;
                }
                if (current is IEnumerator nested && current is not CustomYieldInstruction)
                {
                    stack.Push(nested);
                    current = null;
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Disposes whatever is left of the tree. Returns <paramref name="failure"/>, or both failures
        /// aggregated when disposal throws as well.
        /// </summary>
        public Exception Dispose(Exception failure, ref bool cleanupFailure)
        {
            while (stack.Count != 0)
            {
                var routine = stack.Pop();
                try
                {
                    (routine as IDisposable)?.Dispose();
                }
                catch (Exception exception)
                {
                    cleanupFailure = true;
                    failure = failure == null ? exception : new AggregateException(failure, exception);
                }
            }
            return failure;
        }
    }

    /// <summary>Marks a failure raised while a suite unwound, so the runner reports the cleanup rather than the work as the cause.</summary>
    public sealed class BenchmarkCleanupException : Exception
    {
        /// <summary>Wraps the failure that unwinding raised, keeping it as the inner exception.</summary>
        public BenchmarkCleanupException(string message, Exception innerException)
            : base($"{message} {innerException.Message}", innerException)
        {
        }
    }

    /// <summary>Runs mandatory teardown without losing the failure that triggered it.</summary>
    public static class BenchmarkCleanup
    {
        /// <summary>Returns <paramref name="failure"/>, or both failures aggregated when the cleanup throws too.</summary>
        public static Exception Capture(Exception failure, Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                return failure == null ? exception : new AggregateException(failure, exception);
            }
            return failure;
        }
    }
}
