using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.LowLevel;

namespace LightSide.Benchmark
{
    /// <summary>
    /// The CPU clock state a timing run measures under: every paced frame's main thread starts with a busy-wait that brings
    /// its load to one target (the fill), and with <c>-benchmarkClock hint</c> (the default) an Android Performance Hint session where the device grants
    /// one (Android 12+) is told every frame is late; with <c>none</c> or without a session the governor alone sets the
    /// clocks. <see cref="Start"/> also prices <see cref="Busy"/> at the settled clock.
    /// </summary>
    /// <remarks>
    /// A phone sets the main thread's speed from the load it sees in the frame, and a core waking from the frame's display
    /// wait runs slowly for its first moments: while a frame leaves the main thread idle, more work in it, or work placed
    /// earlier in it, changes how fast the frame's other work runs. The fill runs at the end of <c>Initialization</c>,
    /// before every measured window, for <see cref="FillMilliseconds"/>, which the scheduler sets per slot to
    /// <see cref="LoadMilliseconds"/> minus the arm's own work: every slot then loads the main thread alike and starts its
    /// work on a core that is already running. An arm whose work exceeds the load target gets no fill. The fill's time
    /// per frame is <see cref="LastFillMilliseconds"/> and its running total <see cref="FillTicks"/>; main-thread and
    /// process CPU are read without it.
    /// A frame's interval is at least the refresh interval, far above the 1 ms target, so every frame reads late and the
    /// session holds the highest boost the system grants whatever a slot's own work is. That boost has a ceiling the system
    /// sets; above it the main thread's speed still follows its load, which the fill equalizes.
    /// The session covers the main thread from <see cref="Start"/> and every thread of the process from each
    /// <see cref="CoverProcessThreads"/>, so worker threads an engine waits on run under the same boost as the main thread.
    /// <see cref="Busy"/> runs a fixed number of iterations, so its time measured inside a slot against
    /// <see cref="Milliseconds"/> tells how fast the main thread ran there relative to the calibration.
    /// </remarks>
    public static class BenchmarkClock
    {
        struct ClockMarker { }
        struct FillMarker { }

        const double HintTargetMilliseconds = 1d;
        const double FillMarginMilliseconds = 4d;
        const double FillChunkMilliseconds = 0.01;
        const double CalibrationWarmupMilliseconds = 100d;
        const int CalibrationProbes = 8;
        const long CalibrationIterations = 200_000;

        static bool hintRequested = true;
        static string mode;
        static string freeReason;
        static double nanosecondsPerIteration;
        static bool started;
        static bool installed;
        static string failure;
        static double sink;
        static bool fillInstalled;
        static string fillReason;
        static double frameIntervalMilliseconds;
        static long fillTicksPerFrame;
        static double fillMilliseconds;
        static long fillChunkIterations;

        /// <summary>
        /// The main-thread load every paced frame is brought to, in milliseconds of fill plus update and canvas work: the
        /// frame interval minus 4 ms or a quarter of it, whichever is more; 0 where frames are not paced.
        /// </summary>
        internal static double LoadMilliseconds { get; private set; }

        /// <summary>Milliseconds the fill busy-waits at the start of every frame from the next one on; 0 while nothing sets it.</summary>
        internal static double FillMilliseconds
        {
            get => fillMilliseconds;
            set
            {
                fillMilliseconds = Math.Max(0d, value);
                fillTicksPerFrame = (long)(fillMilliseconds * Stopwatch.Frequency / 1000d);
            }
        }

        /// <summary>Milliseconds the fill took in the most recently completed frame; zero where it did not run.</summary>
        internal static double LastFillMilliseconds { get; private set; }

        /// <summary>Stopwatch ticks the fill has taken since <see cref="Start"/>.</summary>
        internal static long FillTicks { get; private set; }

        /// <summary>Sets the mode from a <c>-benchmarkClock</c> value: null, empty or <c>hint</c> asks for a hint session, <c>none</c> leaves the clock free.</summary>
        /// <exception cref="InvalidOperationException">The clock is running (<see cref="Start"/> without <see cref="Stop"/>).</exception>
        /// <exception cref="ArgumentException">The value is neither <c>hint</c> nor <c>none</c>.</exception>
        public static void Configure(string value)
        {
            if (started) throw new InvalidOperationException("The clock mode cannot change while the clock runs.");
            if (string.IsNullOrEmpty(value) || value.Equals("hint", StringComparison.OrdinalIgnoreCase))
                hintRequested = true;
            else if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                hintRequested = false;
            else
                throw new ArgumentException($"Unknown clock mode '{value}': use hint or none.");
        }

        /// <summary>
        /// Prices <see cref="Busy"/> at the current clock and starts the configured mode; call once the clock has settled. A
        /// device that grants no hint session runs free, and <see cref="Describe"/> names why.
        /// </summary>
        /// <exception cref="InvalidOperationException">The clock is already running.</exception>
        public static void Start()
        {
            if (started) throw new InvalidOperationException("The clock is already running.");
            started = true;
            Calibrate();
            InstallFill();
            freeReason = hintRequested ? OpenHintSession() : "-benchmarkClock none";
            mode = freeReason == null ? "hint" : "none";
            if (freeReason != null) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            BenchmarkFrameProbe.Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update),
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), false, typeof(ClockMarker), ReportFrame);
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
        }

        /// <summary>
        /// Extends the hint session to every thread the process runs now, the threads an engine created while its arms were
        /// built included; returns how many it covers, 0 when the clock runs free.
        /// </summary>
        /// <exception cref="InvalidOperationException">The device granted the main thread a session but refuses one for the process's threads.</exception>
        public static int CoverProcessThreads() => mode == "hint" ? CoverThreads() : 0;

        /// <summary>Removes the per-frame report and closes the hint session; the calibration stays until the next <see cref="Start"/>.</summary>
        public static void Stop()
        {
            started = false;
            failure = null;
            mode = null;
            if (fillInstalled)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                BenchmarkFrameProbe.Remove(ref loop, typeof(UnityEngine.PlayerLoop.Initialization), typeof(FillMarker));
                PlayerLoop.SetPlayerLoop(loop);
                fillInstalled = false;
            }
            FillMilliseconds = 0;
            LoadMilliseconds = 0;
            LastFillMilliseconds = 0;
            if (installed)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                BenchmarkFrameProbe.Remove(ref loop, typeof(UnityEngine.PlayerLoop.Update), typeof(ClockMarker));
                PlayerLoop.SetPlayerLoop(loop);
                installed = false;
            }
            CloseHintSession();
        }

        /// <summary>Throws when the per-frame report failed since <see cref="Start"/>; measured windows must not outlive such a failure.</summary>
        /// <exception cref="InvalidOperationException">The report failed.</exception>
        public static void ThrowOnFailure()
        {
            if (failure != null) throw new InvalidOperationException(failure);
        }

        /// <summary>Iterations of <see cref="Busy"/> that take <paramref name="milliseconds"/> at the calibrated clock.</summary>
        /// <exception cref="InvalidOperationException"><see cref="Start"/> has not run.</exception>
        public static long Iterations(double milliseconds)
        {
            if (nanosecondsPerIteration <= 0) throw new InvalidOperationException("The clock is not calibrated: BenchmarkClock.Start has not run in this process.");
            return Math.Max(1L, (long)Math.Round(milliseconds * 1e6 / nanosecondsPerIteration));
        }

        /// <summary>Milliseconds <paramref name="iterations"/> of <see cref="Busy"/> took at the calibrated clock.</summary>
        /// <exception cref="InvalidOperationException"><see cref="Start"/> has not run.</exception>
        public static double Milliseconds(long iterations)
        {
            if (nanosecondsPerIteration <= 0) throw new InvalidOperationException("The clock is not calibrated: BenchmarkClock.Start has not run in this process.");
            return iterations * nanosecondsPerIteration / 1e6;
        }

        /// <summary>Fixed arithmetic work whose cost changes only with the clock and the core it runs on.</summary>
        public static void Busy(long iterations)
        {
            var accumulator = 0d;
            for (long i = 1; i <= iterations; i++)
                accumulator += 1d / i;
            sink = accumulator;
        }

        /// <summary>
        /// The requested and the running mode, why the clock runs free where it does, the hint target, the fill and the
        /// calibration, as results publish them.
        /// </summary>
        public static JObject Describe()
        {
            var description = new JObject
            {
                ["requested"] = hintRequested ? "hint" : "none",
                ["mode"] = mode,
                ["nanosecondsPerIteration"] = Math.Round(nanosecondsPerIteration, 4),
                ["fill"] = fillInstalled
                    ? new JObject
                    {
                        ["frameIntervalMs"] = Math.Round(frameIntervalMilliseconds, 3),
                        ["loadMs"] = Math.Round(LoadMilliseconds, 3)
                    }
                    : new JObject { ["status"] = "none", ["reason"] = fillReason }
            };
            if (mode == "hint") description["hintTargetMs"] = HintTargetMilliseconds;
            else description["reason"] = freeReason;
            return description;
        }

        static void InstallFill()
        {
            frameIntervalMilliseconds = FrameIntervalMilliseconds();
            var load = frameIntervalMilliseconds - Math.Max(FillMarginMilliseconds, frameIntervalMilliseconds / 4d);
            if (load <= 0)
            {
                fillReason = frameIntervalMilliseconds > 0
                    ? $"Frames are paced every {frameIntervalMilliseconds:0.###} ms, too short to fill."
                    : "Frames are not paced: the main thread waits for no display.";
                return;
            }
            fillReason = null;
            LoadMilliseconds = load;
            FillMilliseconds = 0;
            fillChunkIterations = Iterations(FillChunkMilliseconds);
            FillTicks = 0;
            BenchmarkFrameProbe.Install();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            BenchmarkFrameProbe.Insert(ref loop, typeof(UnityEngine.PlayerLoop.Initialization),
                typeof(BenchmarkFrameProbe.StartMarker), false, typeof(FillMarker), Fill);
            PlayerLoop.SetPlayerLoop(loop);
            fillInstalled = true;
        }

        /// <summary>
        /// The interval frames are paced at: mobile and web players at the target frame rate capped by the display, other
        /// players at the display divided by the vertical-sync count, or at the target frame rate without one; 0 when frames
        /// run unpaced.
        /// </summary>
        static double FrameIntervalMilliseconds()
        {
            var refresh = Screen.currentResolution.refreshRateRatio.value;
            var target = Application.targetFrameRate;
            double rate;
#if UNITY_WEBGL && !UNITY_EDITOR
            rate = target > 0 && target < refresh ? target : refresh;
#else
            if (Application.isMobilePlatform)
                rate = Math.Min(target > 0 ? target : 30, refresh);
            else if (QualitySettings.vSyncCount > 0)
                rate = refresh / QualitySettings.vSyncCount;
            else
                rate = target;
#endif
            return rate > 0 ? 1000d / rate : 0d;
        }

        static void Fill()
        {
            var begin = Stopwatch.GetTimestamp();
            var deadline = begin + fillTicksPerFrame;
            var now = begin;
            while (now < deadline)
            {
                Busy(fillChunkIterations);
                now = Stopwatch.GetTimestamp();
            }
            FillTicks += now - begin;
            LastFillMilliseconds = (now - begin) * 1000d / Stopwatch.Frequency;
        }

        static void Calibrate()
        {
            var warmupEnd = Stopwatch.GetTimestamp() + (long)(CalibrationWarmupMilliseconds * Stopwatch.Frequency / 1000d);
            while (Stopwatch.GetTimestamp() < warmupEnd)
                Busy(CalibrationIterations);
            var fastest = double.MaxValue;
            for (var i = 0; i < CalibrationProbes; i++)
            {
                var begin = Stopwatch.GetTimestamp();
                Busy(CalibrationIterations);
                fastest = Math.Min(fastest, (Stopwatch.GetTimestamp() - begin) * 1e9 / Stopwatch.Frequency);
            }
            nanosecondsPerIteration = fastest / CalibrationIterations;
        }

        static void ReportFrame() => ReportWork(Time.unscaledDeltaTime * 1000d);

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject hintManager;
        static AndroidJavaObject hintSession;
        static IntPtr reportMethod;
        static readonly jvalue[] reportArguments = new jvalue[1];

        static string OpenHintSession()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            var apiLevel = version.GetStatic<int>("SDK_INT");
            if (apiLevel < 31)
                return $"Performance Hint sessions need Android 12 (API 31); this device runs API {apiLevel}.";
            using var context = new AndroidJavaClass("android.content.Context");
            var serviceName = context.GetStatic<string>("PERFORMANCE_HINT_SERVICE");
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            hintManager = activity.Call<AndroidJavaObject>("getSystemService", serviceName);
            if (hintManager == null)
                return "The device offers no PerformanceHintManager.";
            using var process = new AndroidJavaClass("android.os.Process");
            hintSession = CreateSession(new[] { process.CallStatic<int>("myTid") });
            if (hintSession == null)
            {
                hintManager.Dispose();
                hintManager = null;
                return "The device grants no Performance Hint session.";
            }
            var sessionClass = AndroidJNI.GetObjectClass(hintSession.GetRawObject());
            reportMethod = AndroidJNI.GetMethodID(sessionClass, "reportActualWorkDuration", "(J)V");
            AndroidJNI.DeleteLocalRef(sessionClass);
            return null;
        }

        static AndroidJavaObject CreateSession(int[] threads) =>
            hintManager.Call<AndroidJavaObject>("createHintSession", threads, (long)Math.Round(HintTargetMilliseconds * 1e6));

        static int CoverThreads()
        {
            for (var attempt = 0; ; attempt++)
            {
                var threads = ProcessThreads();
                var session = CreateSession(threads);
                if (session != null)
                {
                    hintSession.Call("close");
                    hintSession.Dispose();
                    hintSession = session;
                    return threads.Length;
                }
                if (attempt == 1)
                    throw new InvalidOperationException($"The device granted the main thread a Performance Hint session but refuses one for the process's {threads.Length} threads.");
            }
        }

        static int[] ProcessThreads()
        {
            var directories = Directory.GetDirectories("/proc/self/task");
            var threads = new int[directories.Length];
            for (var i = 0; i < directories.Length; i++)
                threads[i] = int.Parse(Path.GetFileName(directories[i]), CultureInfo.InvariantCulture);
            return threads;
        }

        static void ReportWork(double milliseconds)
        {
            if (failure != null) return;
            reportArguments[0].j = Math.Max(1L, (long)(milliseconds * 1e6));
            AndroidJNI.CallVoidMethod(hintSession.GetRawObject(), reportMethod, reportArguments);
            var exception = AndroidJNI.ExceptionOccurred();
            if (exception == IntPtr.Zero) return;
            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(exception);
            failure = "The Performance Hint session rejected a work report; the clock state of the frames after it is unknown.";
        }

        static void CloseHintSession()
        {
            if (hintSession == null) return;
            hintSession.Call("close");
            hintSession.Dispose();
            hintSession = null;
            hintManager.Dispose();
            hintManager = null;
            reportMethod = IntPtr.Zero;
        }
#else
        static string OpenHintSession() => "Performance Hint sessions exist only in Android players.";

        static int CoverThreads() =>
            throw new InvalidOperationException("A hint session runs only in Android players.");

        static void ReportWork(double milliseconds) { }

        static void CloseHintSession() { }
#endif
    }
}
